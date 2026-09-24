import type { Position } from "../types";
import { money, num, price, signClass, time } from "../format";
import { useData } from "../useData";
import { Badge, Card, Empty, ErrorNote } from "./Ui";

export function Positions({ refreshKey }: { refreshKey: unknown }) {
  const { data, error } = useData<Position[]>("/api/positions/open", refreshKey);
  return (
    <Card title="Open paper positions">
      <ErrorNote error={error} />
      {data && data.length === 0 && <Empty>No open positions. The engine only trades when strategy, score and every risk rule agree.</Empty>}
      {data && data.length > 0 && (
        <div className="table-wrap">
          <table>
            <thead>
              <tr>
                <th>Instrument</th><th>Side</th><th className="num">Units</th><th className="num">Entry</th><th className="num">Stop</th>
                <th className="num">Target</th><th className="num">Current</th><th className="num">Unrealized</th><th className="num">Risk</th>
                <th>Strategy</th><th className="num">Score</th><th>Opened</th>
              </tr>
            </thead>
            <tbody>
              {data.map((p) => (
                <tr key={p.id}>
                  <td className="strong">{p.instrument}</td>
                  <td><Badge tone={p.direction === "Long" ? "good" : "bad"}>{p.direction}</Badge></td>
                  <td className="num">{num(p.units, 0)}</td>
                  <td className="num mono">{price(p.entryPrice, p.instrument)}</td>
                  <td className="num mono">{price(p.stopLoss, p.instrument)}</td>
                  <td className="num mono">{price(p.takeProfit, p.instrument)}</td>
                  <td className="num mono">{price(p.currentPrice, p.instrument)}</td>
                  <td className={`num ${signClass(p.unrealizedPnl)}`}>{money(p.unrealizedPnl)}</td>
                  <td className="num">{money(p.initialRiskAmount)}</td>
                  <td>{p.strategy}</td>
                  <td className="num">{p.score}</td>
                  <td className="mono muted">{time(p.openedAtUtc)}</td>
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
    <Card title="Trade history">
      <ErrorNote error={error} />
      {data && data.length === 0 && <Empty>No closed trades yet.</Empty>}
      {data && data.length > 0 && (
        <div className="table-wrap">
          <table>
            <thead>
              <tr>
                <th>Closed</th><th>Instrument</th><th>Side</th><th>Strategy</th><th className="num">Entry</th><th className="num">Exit</th>
                <th>Exit reason</th><th className="num">P&L</th><th className="num">R</th><th className="num">MAE / MFE (pips)</th>
              </tr>
            </thead>
            <tbody>
              {data.map((p) => (
                <tr key={p.id}>
                  <td className="mono muted">{time(p.closedAtUtc)}</td>
                  <td className="strong">{p.instrument}</td>
                  <td>{p.direction}</td>
                  <td>{p.strategy}</td>
                  <td className="num mono">{price(p.entryPrice, p.instrument)}</td>
                  <td className="num mono">{price(p.exitPrice, p.instrument)}</td>
                  <td><Badge tone={p.exitReason === "TakeProfit" ? "good" : "bad"}>{p.exitReason}</Badge></td>
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
