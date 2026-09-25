import { useState } from "react";
import type { BacktestRun } from "../types";
import { api } from "../api";
import { money, signClass, time } from "../format";
import { useData } from "../useData";
import { useMarketNames } from "../useMarketNames";
import { Card, Empty, ErrorNote } from "./Ui";
import { MetricsByGroup, MetricsSummary } from "./MetricsTable";

export function Backtest() {
  const marketName = useMarketNames();
  const [days, setDays] = useState(90);
  const [seed, setSeed] = useState("");
  const [source, setSource] = useState("simulated");
  const [running, setRunning] = useState(false);
  const [runError, setRunError] = useState<string | null>(null);
  const [latest, setLatest] = useState<BacktestRun | null>(null);
  const [version, setVersion] = useState(0);
  const { data: runs, error } = useData<BacktestRun[]>("/api/backtests?limit=20", version);

  const run = async () => {
    setRunning(true);
    setRunError(null);
    try {
      const result = await api.post<BacktestRun>("/api/backtests", {
        source,
        days,
        seed: seed.trim() === "" ? null : Number(seed),
      });
      setLatest(result);
      setVersion((v) => v + 1);
    } catch (e) {
      setRunError((e as Error).message);
    } finally {
      setRunning(false);
    }
  };

  const shown = latest ?? runs?.[0] ?? null;

  return (
    <>
      <Card title="Run a backtest">
        <p className="hint">
          Replays 5-minute bars through the same strategy, scoring, risk and fill code as paper trading, including spread, slippage and commission.
          A good backtest is not proof of a live edge.
        </p>
        <div className="form-row">
          <label>Source
            <select value={source} onChange={(e) => setSource(e.target.value)}>
              <option value="simulated">Simulated (reproducible by seed)</option>
              <option value="stored">Stored candles from the worker</option>
            </select>
          </label>
          <label>Days
            <input type="number" min={20} max={365} value={days} onChange={(e) => setDays(Number(e.target.value))} />
          </label>
          <label>Seed
            <input placeholder="default" value={seed} onChange={(e) => setSeed(e.target.value.replace(/[^0-9]/g, ""))} disabled={source !== "simulated"} />
          </label>
          <button className="primary" onClick={run} disabled={running}>{running ? "Running…" : "Run backtest"}</button>
        </div>
        <ErrorNote error={runError} />
      </Card>

      {shown && (
        <div className="two-col">
          <Card title={`Result: ${shown.parameters.source}, ${shown.parameters.days} days${shown.parameters.seed != null ? `, seed ${shown.parameters.seed}` : ""}`}>
            <p className="muted">{time(shown.summary.fromUtc)} → {time(shown.summary.toUtc)} · {shown.summary.barsProcessed.toLocaleString()} bars · {shown.parameters.instruments.map(marketName).join(", ")}</p>
            <MetricsSummary m={shown.summary.metrics} />
          </Card>
          <Card title="Decisions">
            <dl className="kv">
              {Object.entries(shown.summary.decisionCounts).map(([k, v]) => (
                <div key={k} className="kv-row"><dt>{k}</dt><dd>{v.toLocaleString()}</dd></div>
              ))}
            </dl>
          </Card>
        </div>
      )}
      {shown && <Card title="By strategy"><MetricsByGroup groups={shown.summary.byStrategy} label="Strategy" /></Card>}

      <Card title="Previous runs">
        <ErrorNote error={error} />
        {runs && runs.length === 0 && <Empty>No backtests yet.</Empty>}
        {runs && runs.length > 0 && (
          <div className="table-wrap">
            <table>
              <thead><tr><th>Run at</th><th>Source</th><th className="num">Days</th><th className="num">Seed</th><th className="num">Trades</th><th className="num">Net P&L</th></tr></thead>
              <tbody>
                {runs.map((r) => (
                  <tr key={r.id} className="clickable" onClick={() => setLatest(r)}>
                    <td className="mono muted">{time(r.createdAtUtc)}</td>
                    <td>{r.source}</td>
                    <td className="num">{r.parameters.days}</td>
                    <td className="num">{r.parameters.seed ?? "—"}</td>
                    <td className="num">{r.totalTrades}</td>
                    <td className={`num ${signClass(r.netPnl)}`}>{money(r.netPnl)}</td>
                  </tr>
                ))}
              </tbody>
            </table>
          </div>
        )}
      </Card>
    </>
  );
}
