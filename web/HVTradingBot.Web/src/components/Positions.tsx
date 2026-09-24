import type { Position } from "../types";
import type { PageId } from "../pages";
import { money, num, price, signClass, time } from "../format";
import { useData } from "../useData";
import { Badge, Card, Empty, ErrorNote, Segmented } from "./Ui";

type Navigate = (page: PageId, section?: string) => void;

/** Trades page: open positions and closed-trade history. */
export function Trades({ refreshKey, view, navigate }: { refreshKey: unknown; view?: string; navigate: Navigate }) {
  const current = view === "history" ? "history" : "open";
  return (
    <div className="stack">
      <Segmented
        value={current}
        options={[{ value: "open", label: "Open positions" }, { value: "history", label: "Trade history" }]}
        onChange={(v) => navigate("trades", v === "open" ? undefined : v)}
      />
      {current === "open" ? <OpenPositions refreshKey={refreshKey} /> : <TradeHistory refreshKey={refreshKey} />}
    </div>
  );
}

export function OpenPositions({ refreshKey, compact = false, onMore }: { refreshKey: unknown; compact?: boolean; onMore?: () => void }) {
  const { data, error } = useData<Position[]>("/api/positions/open", refreshKey);
  return (
    <Card title={compact ? "Open positions" : undefined} actions={onMore && <button className="link" onClick={onMore}>All trades →</button>}>
      <ErrorNote error={error} />
      {data && data.length === 0 && <Empty>No open positions. The engine trades only when a setup scores 75+ and every risk rule passes.</Empty>}
      {data && data.length > 0 && (
        <div className="table-wrap">
          <table>
            <thead>
              <tr>
                <th>Market</th><th>Side</th>
                {!compact && <th className="num">Units</th>}
                <th className="num">Entry</th><th className="num">Stop</th><th className="num">Target</th>
                {!compact && <th className="num">Current</th>}
                <th className="num">Open P&L</th>
                {!compact && <th className="num">Risk</th>}
                <th>Strategy</th>
                {!compact && <th className="num">Score</th>}
                {!compact && <th>Opened</th>}
              </tr>
            </thead>
            <tbody>
              {data.map((p) => (
                <tr key={p.id}>
                  <td className="strong">{p.instrument}</td>
                  <td><Badge tone={p.direction === "Long" ? "good" : "bad"}>{p.direction === "Long" ? "Buy" : "Sell"}</Badge></td>
                  {!compact && <td className="num">{num(p.units, p.units < 10 ? 4 : 0)}</td>}
                  <td className="num mono">{price(p.entryPrice, p.instrument)}</td>
                  <td className="num mono">{price(p.stopLoss, p.instrument)}</td>
                  <td className="num mono">{price(p.takeProfit, p.instrument)}</td>
                  {!compact && <td className="num mono">{price(p.currentPrice, p.instrument)}</td>}
                  <td className={`num ${signClass(p.unrealizedPnl)}`}>{money(p.unrealizedPnl)}</td>
                  {!compact && <td className="num">{money(p.initialRiskAmount)}</td>}
                  <td>{p.strategy}</td>
                  {!compact && <td className="num">{p.score}</td>}
                  {!compact && <td className="mono muted">{time(p.openedAtUtc)}</td>}
                </tr>
              ))}
            </tbody>
          </table>
        </div>
      )}
    </Card>
  );
}

export function TradeHistory({ refreshKey }: { refreshKey: unknown }) {
  const { data, error } = useData<Position[]>("/api/trades?limit=200", refreshKey);
  return (
    <Card>
      <ErrorNote error={error} />
      {data && data.length === 0 && <Empty>No closed trades yet.</Empty>}
      {data && data.length > 0 && (
        <div className="table-wrap">
          <table>
            <thead>
              <tr>
                <th>Closed</th><th>Market</th><th>Side</th><th>Strategy</th><th className="num">Entry</th><th className="num">Exit</th>
                <th>Exit</th><th className="num">P&L</th><th className="num">R</th><th className="num">MAE / MFE</th>
              </tr>
            </thead>
            <tbody>
              {data.map((p) => (
                <tr key={p.id}>
                  <td className="mono muted">{time(p.closedAtUtc)}</td>
                  <td className="strong">{p.instrument}</td>
                  <td>{p.direction === "Long" ? "Buy" : "Sell"}</td>
                  <td>{p.strategy}</td>
                  <td className="num mono">{price(p.entryPrice, p.instrument)}</td>
                  <td className="num mono">{price(p.exitPrice, p.instrument)}</td>
                  <td><Badge tone={p.exitReason === "TakeProfit" ? "good" : p.exitReason === "StopLoss" ? "bad" : "neutral"}>{p.exitReason}</Badge></td>
                  <td className={`num ${signClass(p.realizedPnl)}`}>{money(p.realizedPnl)}</td>
                  <td className={`num ${signClass(p.rMultiple)}`}>{num(p.rMultiple)}</td>
                  <td className="num">{num(p.maePips, 1)} / {num(p.mfePips, 1)}</td>
                </tr>
              ))}
            </tbody>
          </table>
        </div>
      )}
    </Card>
  );
}
