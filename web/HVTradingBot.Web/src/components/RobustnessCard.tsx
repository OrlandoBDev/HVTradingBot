import { money, num, signClass, time } from "../format";
import type { Robustness } from "../types";
import { Badge, Card, Stat } from "./Ui";

const VERDICT: Record<Robustness["verdict"], { label: string; tone: "good" | "bad" | "warn" | "neutral" }> = {
  Holds: { label: "edge holds", tone: "good" },
  Unproven: { label: "not proven", tone: "warn" },
  NoEdge: { label: "no edge", tone: "bad" },
  TooFewTrades: { label: "too few trades", tone: "neutral" },
};

/**
 * Is a backtest more than luck? Consecutive periods (each one uses only what was learned before it: walk forward)
 * and a Monte Carlo reshuffle of the trades.
 */
export function RobustnessCard({ r, source }: { r: Robustness; source: string }) {
  const mc = r.monteCarlo;
  const verdict = VERDICT[r.verdict];
  return (
    <Card title="Is it more than luck?" actions={<Badge tone={verdict.tone}>{verdict.label}</Badge>}>
      <p className={verdict.tone === "good" ? "pos" : verdict.tone === "bad" ? "neg" : ""}>{r.summary}</p>
      {source === "simulated" && (
        <p className="muted small">
          Simulated prices have no real patterns, so this run only shows the engine works; results on stored Deriv candles are what count.
        </p>
      )}

      <h3>Period by period</h3>
      <p className="muted small">
        The run is split into consecutive periods. The engine learns only from trades already closed, so each period is traded with what
        it learned before it. A real edge shows up in most periods, not one lucky stretch.
      </p>
      <div className="table-wrap">
        <table>
          <thead>
            <tr><th>Period</th><th className="num">Trades</th><th className="num">Net P&L</th><th className="num">Avg R</th><th className="num">Win rate</th><th className="num">Profit factor</th></tr>
          </thead>
          <tbody>
            {r.periods.map((p) => (
              <tr key={p.index}>
                <td className="nowrap">{time(p.fromUtc).slice(0, 10)} → {time(p.toUtc).slice(0, 10)}</td>
                <td className="num">{p.trades}</td>
                <td className={`num ${signClass(p.netPnl)}`}>{p.trades ? money(p.netPnl) : "—"}</td>
                <td className={`num ${signClass(p.averageR)}`}>{p.trades ? num(p.averageR) : "—"}</td>
                <td className="num">{p.trades ? `${num(p.winRate, 0)}%` : "—"}</td>
                <td className="num">{p.trades ? num(p.profitFactor) : "—"}</td>
              </tr>
            ))}
          </tbody>
        </table>
      </div>

      {mc && (
        <>
          <h3>Monte Carlo ({mc.simulations.toLocaleString()} reshuffles)</h3>
          <p className="muted small">
            The same {mc.tradesPerRun} trades drawn at random in a different order and mix, to show the range of results luck alone could
            give. R = multiples of the amount risked per trade.
          </p>
          <div className="grid-stats">
            <Stat label="Total, typical" value={`${num(mc.totalRMedian, 1)}R`} tone={signClass(mc.totalRMedian)}
              sub={`bad case ${num(mc.totalRWorst5, 1)}R · good case ${num(mc.totalRBest5, 1)}R`} />
            <Stat label="Chance of a loss" value={`${num(mc.probabilityOfLossPercent, 0)}%`}
              tone={mc.probabilityOfLossPercent > 25 ? "neg" : mc.probabilityOfLossPercent < 5 ? "pos" : ""} sub="over the same number of trades" />
            <Stat label="Drawdown to expect" value={`${num(mc.maxDrawdownRMedian, 1)}R`} sub={`bad case ${num(mc.maxDrawdownRWorst5, 1)}R`} />
            <Stat label="Average trade, at least" value={`${num(mc.averageRLowerBound, 2)}R`} tone={signClass(mc.averageRLowerBound)}
              sub="with 95% confidence" />
          </div>
        </>
      )}
    </Card>
  );
}
