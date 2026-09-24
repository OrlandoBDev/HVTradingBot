import type { Market } from "../types";
import type { PageId } from "../pages";
import { num, price, time } from "../format";
import { useData } from "../useData";
import { Badge, Card, Empty, ErrorNote, RegimeBadge, stateTone } from "./Ui";

type Navigate = (page: PageId, section?: string) => void;

export function Markets({ refreshKey, navigate, compact = false }: { refreshKey: unknown; navigate: Navigate; compact?: boolean }) {
  const { data, error } = useData<Market[]>("/api/markets", refreshKey);
  return (
    <Card
      title={compact ? "Markets" : undefined}
      actions={
        <button className="link" onClick={() => navigate(compact ? "markets" : "settings", compact ? undefined : "markets")}>
          {compact ? "Details →" : "Choose markets →"}
        </button>
      }
    >
      <ErrorNote error={error} />
      {data && data.length === 0 && (
        <Empty>
          No market data yet. The worker publishes prices once it has loaded history.{" "}
          <button className="link" onClick={() => navigate("settings", "markets")}>Choose markets</button>
        </Empty>
      )}
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
                <tr key={m.instrument}>
                  <td>
                    <span className="strong">{m.displayName}</span>
                    {m.displayName !== m.instrument && <span className="muted small"> {m.instrument}</span>}
                    {!m.isTradable && <> <Badge tone="neutral">analysis only</Badge></>}
                  </td>
                  <td className="num mono">{price(m.bid, m.instrument, m.priceDecimals)}</td>
                  {!compact && <td className="num mono">{price(m.ask, m.instrument, m.priceDecimals)}</td>}
                  <td className="num">{num(m.spreadPips, 1)}</td>
                  <td><RegimeBadge regime={m.regime} /></td>
                  {!compact && <td className="num">{num(m.indicators?.rsi, 1)}</td>}
                  {!compact && <td className="num">{num(m.indicators?.adx, 1)}</td>}
                  <td>{m.lastDecision ? <Badge tone={stateTone(m.lastDecision)}>{m.lastDecision}</Badge> : <span className="muted">—</span>}</td>
                  {!compact && <td className="mono muted">{time(m.marketTimeUtc)}</td>}
                </tr>
              ))}
            </tbody>
          </table>
        </div>
      )}
    </Card>
  );
}
