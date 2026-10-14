import { lazy, Suspense, useMemo, useState } from "react";
import type { Market, MarketSettings, Position } from "../types";
import type { PageId } from "../pages";
import { num, price, time } from "../format";
import { useData } from "../useData";
import { Badge, Card, Empty, ErrorNote, RegimeBadge, stateTone } from "./Ui";

// The chart library is only needed on the Markets page, so it loads separately from the rest of the dashboard.
const PriceChart = lazy(() => import("./PriceChart").then((m) => ({ default: m.PriceChart })));

type Navigate = (page: PageId, section?: string) => void;

export function Markets({ refreshKey, navigate, compact = false }: { refreshKey: unknown; navigate: Navigate; compact?: boolean }) {
  const { data: all, error } = useData<Market[]>("/api/markets", refreshKey);
  const { data: selection } = useData<MarketSettings>("/api/settings/markets", refreshKey);
  const applying = !!selection && selection.version > 0 && selection.version !== selection.appliedVersion;
  const loading = all?.some((m) => m.isLoading) ?? false;
  // Closed markets (no prices for 10+ minutes) are hidden until they trade again.
  const data = all?.filter((m) => m.isOpen);
  const closed = all?.filter((m) => !m.isOpen) ?? [];
  const [chosen, setChosen] = useState<string | null>(null);
  const chartable = data?.filter((m) => !m.isLoading) ?? [];
  const charted = chartable.find((m) => m.instrument === chosen) ?? chartable[0];
  const table = (
    <Card
      title={compact ? "Markets" : undefined}
      actions={
        <button className="link" onClick={() => navigate(compact ? "markets" : "settings", compact ? undefined : "markets")}>
          {compact ? "Details →" : "Choose markets →"}
        </button>
      }
    >
      <ErrorNote error={error} />
      {(applying || loading) && (
        <p className="notice">
          {applying
            ? "Applying your new market selection — the worker is restarting and loading history. New markets appear here within a minute or two."
            : "Loading history for newly added markets…"}
        </p>
      )}
      {all && all.length === 0 && (
        <Empty>
          No market data yet. The worker publishes prices once it has loaded history.{" "}
          <button className="link" onClick={() => navigate("settings", "markets")}>Choose markets</button>
        </Empty>
      )}
      {all && all.length > 0 && data && data.length === 0 && <Empty>All selected markets are closed right now.</Empty>}
      {data && data.length > 0 && (
        <div className="table-wrap">
          <table>
            <thead>
              <tr>
                <th>Market</th>
                <th className="num">Bid</th>
                {!compact && <th className="num">Ask</th>}
                <th className="num">Spread</th>
                <th>Regime</th>
                {!compact && <th className="num">RSI</th>}
                {!compact && <th className="num">ADX</th>}
                <th>Last decision</th>
                {!compact && <th>Updated</th>}
              </tr>
            </thead>
            <tbody>
              {data.map((m) => (
                <tr
                  key={m.instrument}
                  className={compact || m.isLoading ? undefined : `clickable ${m.instrument === charted?.instrument ? "selected" : ""}`}
                  onClick={compact || m.isLoading ? undefined : () => setChosen(m.instrument)}
                >
                  <td>
                    <span className="strong">{m.displayName}</span>
                    {m.displayName !== m.instrument && <span className="muted small"> {m.instrument}</span>}
                    {!m.isTradable && <> <Badge tone="neutral">analysis only</Badge></>}
                  </td>
                  <td className="num mono">{m.isLoading ? "—" : price(m.bid, m.instrument, m.priceDecimals)}</td>
                  {!compact && <td className="num mono">{m.isLoading ? "—" : price(m.ask, m.instrument, m.priceDecimals)}</td>}
                  <td className="num">{m.isLoading ? "—" : num(m.spreadPips, 1)}</td>
                  <td>{m.isLoading ? <span className="muted">loading history…</span> : <RegimeBadge regime={m.regime} />}</td>
                  {!compact && <td className="num">{num(m.indicators?.rsi, 1)}</td>}
                  {!compact && <td className="num">{num(m.indicators?.adx, 1)}</td>}
                  <td>
                    {m.isPaused ? (
                      <span title="Derived markets trade only while Forex is closed (Settings › Markets)"><Badge tone="neutral">waiting · Forex open</Badge></span>
                    ) : m.lastDecision ? (
                      <Badge tone={stateTone(m.lastDecision)}>{m.lastDecision}</Badge>
                    ) : (
                      <span className="muted">—</span>
                    )}
                  </td>
                  {!compact && <td className="mono muted">{m.isLoading ? "—" : time(m.marketTimeUtc)}</td>}
                </tr>
              ))}
            </tbody>
          </table>
        </div>
      )}
      {closed.length > 0 && (
        <p className="muted small closed-note">
          Closed now, shown again when trading resumes: {closed.map((m) => m.displayName).join(", ")}.
        </p>
      )}
    </Card>
  );
  if (compact) return table;
  return (
    <div className="stack">
      {charted && (
        <Card
          title={
            <select aria-label="Chart market" value={charted.instrument} onChange={(e) => setChosen(e.target.value)}>
              {chartable.map((m) => (
                <option key={m.instrument} value={m.instrument}>{m.displayName}</option>
              ))}
            </select>
          }
          actions={<span className="muted small">5-minute bars · local time</span>}
        >
          <MarketChart market={charted} refreshKey={refreshKey} />
        </Card>
      )}
      {table}
    </div>
  );
}

function MarketChart({ market, refreshKey }: { market: Market; refreshKey: unknown }) {
  const { data: positions } = useData<Position[]>("/api/positions/open", refreshKey);
  // Memoised so the chart redraws its overlays only when the positions change, not on every status push.
  const open = useMemo(() => positions?.filter((p) => p.instrument === market.instrument) ?? [], [positions, market.instrument]);
  return (
    <>
      <Suspense fallback={<div className="price-chart" />}>
        <PriceChart instrument={market.instrument} priceDecimals={market.priceDecimals} positions={open} />
      </Suspense>
      <p className="chart-legend muted small">
        {open.length === 0 ? (
          "No open position in this market."
        ) : (
          <>
            <span className="entry">Entry</span>
            <span className="stop">Stop</span>
            <span className="target">Target</span>
          </>
        )}
      </p>
    </>
  );
}
