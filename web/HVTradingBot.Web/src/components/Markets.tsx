import type { Market } from "../types";
import { num, price, time } from "../format";
import { useData } from "../useData";
import { Badge, Card, Empty, ErrorNote, RegimeBadge, stateTone } from "./Ui";

export function Markets({ refreshKey }: { refreshKey: unknown }) {
  const { data, error } = useData<Market[]>("/api/markets", refreshKey);
  return (
    <Card title="Market overview">
      <ErrorNote error={error} />
      {data && data.length === 0 && <Empty>Waiting for the worker to publish market data…</Empty>}
      {data && data.length > 0 && (
        <div className="table-wrap">
          <table>
            <thead>
              <tr>
                <th>Market</th><th className="num">Bid</th><th className="num">Ask</th><th className="num">Spread (pips/pts)</th>
                <th>Regime (4H/1H)</th><th className="num">RSI 1H</th><th className="num">ADX 1H</th><th>Last decision</th><th>Market time</th>
              </tr>
            </thead>
            <tbody>
              {data.map((m) => (
                <tr key={m.instrument}>
                  <td className="strong">
                    {m.displayName}
                    {m.displayName !== m.instrument && <span className="muted small"> {m.instrument}</span>}
                    {!m.isTradable && <> <Badge tone="neutral">analysis only</Badge></>}
                  </td>
                  <td className="num mono">{price(m.bid, m.instrument, m.priceDecimals)}</td>
                  <td className="num mono">{price(m.ask, m.instrument, m.priceDecimals)}</td>
                  <td className="num">{num(m.spreadPips, 1)}</td>
                  <td><RegimeBadge regime={m.regime} /></td>
                  <td className="num">{num(m.indicators?.rsi, 1)}</td>
                  <td className="num">{num(m.indicators?.adx, 1)}</td>
                  <td>{m.lastDecision ? <Badge tone={stateTone(m.lastDecision)}>{m.lastDecision}</Badge> : "—"}</td>
                  <td className="mono muted">{time(m.marketTimeUtc)}</td>
                </tr>
              ))}
            </tbody>
          </table>
        </div>
      )}
    </Card>
  );
}
