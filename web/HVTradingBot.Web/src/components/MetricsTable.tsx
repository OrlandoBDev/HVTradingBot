import type { Metrics } from "../types";
import { money, num, signClass } from "../format";

export function MetricsSummary({ m }: { m: Metrics }) {
  const rows: [string, string, string?][] = [
    ["Trades", `${m.totalTrades} (${m.wins} W / ${m.losses} L)`],
    ["Win rate", `${num(m.winRate, 1)}%`],
    ["Net P&L", money(m.netPnl), signClass(m.netPnl)],
    ["Profit factor", m.profitFactor == null ? "—" : num(m.profitFactor)],
    ["Expectancy / trade", money(m.expectancy), signClass(m.expectancy)],
    ["Average R", num(m.averageR), signClass(m.averageR)],
    ["Max drawdown", `${money(m.maxDrawdown)} (${num(m.maxDrawdownPercent)}%)`],
    ["Longest losing streak", String(m.longestLosingStreak)],
    ["Avg MAE / MFE (pips)", `${num(m.averageMaePips, 1)} / ${num(m.averageMfePips, 1)}`],
  ];
  return (
    <dl className="kv">
      {rows.map(([k, v, tone]) => (
        <div key={k} className="kv-row"><dt>{k}</dt><dd className={tone}>{v}</dd></div>
      ))}
    </dl>
  );
}

export function MetricsByGroup({ groups, label }: { groups: Record<string, Metrics>; label: string }) {
  const entries = Object.entries(groups);
  if (entries.length === 0) return <p className="empty">No closed trades yet.</p>;
  return (
    <div className="table-wrap">
      <table>
        <thead>
          <tr>
            <th>{label}</th><th className="num">Trades</th><th className="num">Win %</th><th className="num">Net P&L</th>
            <th className="num">PF</th><th className="num">Avg R</th><th className="num">Max DD</th>
          </tr>
        </thead>
        <tbody>
          {entries.map(([name, m]) => (
            <tr key={name}>
              <td className="strong">{name}</td>
              <td className="num">{m.totalTrades}</td>
              <td className="num">{num(m.winRate, 1)}</td>
              <td className={`num ${signClass(m.netPnl)}`}>{money(m.netPnl)}</td>
              <td className="num">{m.profitFactor == null ? "—" : num(m.profitFactor)}</td>
              <td className={`num ${signClass(m.averageR)}`}>{num(m.averageR)}</td>
              <td className="num">{money(m.maxDrawdown)}</td>
            </tr>
          ))}
        </tbody>
      </table>
    </div>
  );
}
