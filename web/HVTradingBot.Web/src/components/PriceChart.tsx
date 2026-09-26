import { useEffect, useRef, useState } from "react";
import {
  CandlestickSeries,
  ColorType,
  createChart,
  createSeriesMarkers,
  LineStyle,
  type CandlestickData,
  type IChartApi,
  type IPriceLine,
  type ISeriesApi,
  type ISeriesMarkersPluginApi,
  type SeriesMarker,
  type Time,
  type UTCTimestamp,
} from "lightweight-charts";
import { api } from "../api";
import { onBar, onReconnected } from "../hub";
import type { CandleBar, CandleSeries, Position } from "../types";
import { Empty, ErrorNote } from "./Ui";

const TIME_FRAME = "M5";
const BAR_SECONDS = 5 * 60;
const HISTORY_BARS = 500;

// The chart library shows timestamps as UTC; shifting them by the local offset shows local time on the axis. Offsets are
// whole quarter hours, so 5-minute bars stay aligned.
const toTime = (iso: string) => {
  const ms = Date.parse(iso);
  return (Math.floor(ms / 1000) - new Date(ms).getTimezoneOffset() * 60) as UTCTimestamp;
};
const toBar = (c: Pick<CandleBar, "openTimeUtc" | "open" | "high" | "low" | "close">): CandlestickData<Time> => ({ time: toTime(c.openTimeUtc), open: c.open, high: c.high, low: c.low, close: c.close });

/** The browser's language when Intl accepts it (some report tags like "en-US@posix" that make the chart throw). */
function chartLocale() {
  try {
    return Intl.getCanonicalLocales(navigator.language)[0] ?? "en-US";
  } catch {
    return "en-US";
  }
}

/** The dashboard's colour tokens, read from CSS so the chart follows the light/dark theme. */
function palette() {
  const css = getComputedStyle(document.documentElement);
  const v = (name: string) => css.getPropertyValue(name).trim();
  return { panel: v("--panel"), text: v("--muted"), border: v("--border"), accent: v("--accent"), good: v("--good"), bad: v("--bad") };
}

function applyTheme(chart: IChartApi, series: ISeriesApi<"Candlestick">) {
  const c = palette();
  chart.applyOptions({
    layout: { background: { type: ColorType.Solid, color: c.panel }, textColor: c.text },
    grid: { vertLines: { color: c.border }, horzLines: { color: c.border } },
    rightPriceScale: { borderColor: c.border },
    timeScale: { borderColor: c.border },
  });
  series.applyOptions({ upColor: c.good, downColor: c.bad, borderVisible: false, wickUpColor: c.good, wickDownColor: c.bad });
}

/**
 * 5-minute candles for one market from GET /api/candles, extended live by the hub's "bar" messages, with each open
 * position's entry, stop and take-profit drawn as price lines and its entry marked on the bar it opened in.
 */
export function PriceChart({ instrument, priceDecimals, positions }: { instrument: string; priceDecimals: number; positions: Position[] }) {
  const container = useRef<HTMLDivElement>(null);
  const chartRef = useRef<{ chart: IChartApi; series: ISeriesApi<"Candlestick">; markers: ISeriesMarkersPluginApi<Time> } | null>(null);
  const [bars, setBars] = useState<CandlestickData<Time>[] | null>(null);
  const [error, setError] = useState<string | null>(null);
  const [reloadKey, setReloadKey] = useState(0);

  // One chart per mounted component; the theme follows the system setting like the rest of the dashboard.
  useEffect(() => {
    const chart = createChart(container.current!, {
      autoSize: true,
      timeScale: { timeVisible: true, secondsVisible: false },
      crosshair: { horzLine: { labelVisible: true } },
      localization: { locale: chartLocale(), dateFormat: "yyyy-MM-dd" },
    });
    const series = chart.addSeries(CandlestickSeries);
    const markers = createSeriesMarkers(series, []);
    applyTheme(chart, series);
    chartRef.current = { chart, series, markers };

    const scheme = window.matchMedia("(prefers-color-scheme: light)");
    const onScheme = () => applyTheme(chart, series);
    scheme.addEventListener("change", onScheme);
    const offReconnect = onReconnected(() => setReloadKey((k) => k + 1));
    return () => {
      scheme.removeEventListener("change", onScheme);
      offReconnect();
      chartRef.current = null;
      chart.remove();
    };
  }, []);

  useEffect(() => {
    const minMove = 1 / 10 ** priceDecimals;
    chartRef.current?.series.applyOptions({ priceFormat: { type: "price", precision: priceDecimals, minMove } });
  }, [priceDecimals]);

  // Reset the view when switching markets.
  useEffect(() => {
    setBars(null);
    setReloadKey(0);
    chartRef.current?.series.setData([]);
  }, [instrument]);

  // History for the selected market; reloaded after a reconnect in case live bars were missed.
  useEffect(() => {
    let cancelled = false;
    setError(null);
    api
      .get<CandleSeries>(`/api/candles?instrument=${encodeURIComponent(instrument)}&timeframe=${TIME_FRAME}&limit=${HISTORY_BARS}`)
      .then((series) => {
        if (cancelled || !chartRef.current) return;
        const data = series.candles.map(toBar);
        chartRef.current.series.setData(data);
        if (reloadKey === 0) chartRef.current.chart.timeScale().fitContent();
        setBars(data);
      })
      .catch((e: Error) => !cancelled && setError(e.message));
    return () => {
      cancelled = true;
    };
  }, [instrument, reloadKey]);

  // Live bars for this market extend the series without refetching.
  useEffect(
    () =>
      onBar((candle) => {
        const current = chartRef.current;
        if (candle.instrument !== instrument || candle.timeFrame !== TIME_FRAME || !current) return;
        const bar = toBar(candle);
        const last = current.series.data().at(-1);
        if (last && (last.time as number) > (bar.time as number)) return; // older than what is shown; the next reload has it
        current.series.update(bar);
        setBars((b) => (b ? [...b.filter((x) => x.time !== bar.time), bar] : [bar]));
      }),
    [instrument],
  );

  // Entry, stop and take-profit lines plus an entry marker for each open position in this market.
  useEffect(() => {
    const current = chartRef.current;
    if (!current || !bars) return;
    const c = palette();
    const lines: IPriceLine[] = [];
    const markers: SeriesMarker<Time>[] = [];
    const first = bars[0]?.time as number | undefined;
    for (const p of positions) {
      const long = p.direction === "Long";
      // External contracts may have no known entry, stop or target: draw only the levels there are.
      const line = (price: number | null, color: string, title: string, lineStyle: LineStyle) =>
        price != null && lines.push(current.series.createPriceLine({ price, color, title, lineStyle, lineWidth: 1, axisLabelVisible: true }));
      line(p.entryPrice, c.accent, `${long ? "Long" : "Short"} entry`, LineStyle.Solid);
      line(p.stopLoss, c.bad, "Stop", LineStyle.Dashed);
      line(p.takeProfit, c.good, "Target", LineStyle.Dashed);

      const opened = toTime(p.openedAtUtc);
      const barTime = (opened - (opened % BAR_SECONDS)) as UTCTimestamp;
      if (first !== undefined && barTime >= first) {
        markers.push({
          time: barTime,
          position: long ? "belowBar" : "aboveBar",
          shape: long ? "arrowUp" : "arrowDown",
          color: c.accent,
          text: `${long ? "Buy" : "Sell"} ${p.strategy}`,
        });
      }
    }
    markers.sort((a, b) => (a.time as number) - (b.time as number));
    current.markers.setMarkers(markers);
    return () => {
      lines.forEach((l) => current.series.removePriceLine(l));
      current.markers.setMarkers([]);
    };
  }, [positions, bars]);

  return (
    <>
      <ErrorNote error={error} />
      <div className="price-chart" ref={container}>
        {bars && bars.length === 0 && (
          <div className="price-chart-empty">
            <Empty>No stored bars for this market yet. They appear once the worker has loaded its history.</Empty>
          </div>
        )}
      </div>
    </>
  );
}
