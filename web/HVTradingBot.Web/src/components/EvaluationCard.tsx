import { useState } from "react";
import { api } from "../api";
import { money, num, signClass, time } from "../format";
import type { Robustness } from "../types";
import { useData } from "../useData";
import { Badge, Card, ErrorNote, Stat } from "./Ui";

interface Evaluation {
  targetTrades: number;
  forexTrades: number;
  sinceUtc: string | null;
  metrics: { averageR: number; winRate: number; netPnl: number; maxDrawdownPercent: number; profitFactor: number | null };
  robustness: Robustness;
  otherTrades: number;
  otherPnl: number;
  checks: { key: string; label: string; passed: boolean; detail: string }[];
  ready: boolean;
  summary: string;
}

const SETUP_CHECKS = ["extras", "forexOnly"];

/**
 * Before any real money: the bot's own Forex trades on demo, judged on the average trade (and whether it is more
 * than luck) and the drawdown, over enough trades, on the simple setup being evaluated.
 */
export function EvaluationCard({ refreshKey }: { refreshKey: unknown }) {
  const [reload, setReload] = useState(0);
  const { data, error } = useData<Evaluation>("/api/evaluation", `${refreshKey}|${reload}`);
  const [busy, setBusy] = useState(false);
  const [actionError, setActionError] = useState<string | null>(null);

  if (!data) return error ? <ErrorNote error={error} /> : null;

  const progress = Math.min(100, (data.forexTrades / data.targetTrades) * 100);
  const setupMissing = data.checks.filter((c) => SETUP_CHECKS.includes(c.key) && !c.passed);
  const mc = data.robustness.monteCarlo;

  const applySetup = async () => {
    if (!window.confirm("Use the evaluation setup?\n\n• Only Forex markets stay selected (the engine restarts)\n• High-score extra positions are turned off\n\nBoth can be changed back under Settings.")) return;
    setBusy(true);
    setActionError(null);
    try {
      await api.post("/api/evaluation/setup", {});
      setReload((n) => n + 1);
    } catch (e) {
      setActionError((e as Error).message);
    } finally {
      setBusy(false);
    }
  };

  return (
    <Card title="Ready for real money?" actions={<Badge tone={data.ready ? "good" : "warn"}>{data.ready ? "checks pass" : "not yet"}</Badge>}>
      <p>{data.summary}</p>
      <div className="eval-progress" title={`${data.forexTrades} of ${data.targetTrades} closed Forex trades`}>
        <div className="eval-progress-bar" style={{ width: `${progress}%` }} />
      </div>
      <p className="muted small">
        {data.forexTrades} of {data.targetTrades} closed Forex trades by the bot{data.sinceUtc ? ` since ${time(data.sinceUtc).slice(0, 10)}` : ""}. Signal
        trades, test trades{data.otherTrades ? ` and ${data.otherTrades} trade(s) on other markets (${money(data.otherPnl)})` : " and other markets"} are
        not part of it.
      </p>

      {data.forexTrades > 0 && (
        <div className="grid-stats">
          <Stat label="Average trade" value={`${num(data.metrics.averageR)}R`} tone={signClass(data.metrics.averageR)}
            sub={mc ? `at least ${num(mc.averageRLowerBound)}R (95%)` : "needs 30 trades to judge"} />
          <Stat label="Largest drop" value={`${num(data.metrics.maxDrawdownPercent, 1)}%`}
            tone={data.metrics.maxDrawdownPercent >= 10 ? "neg" : ""} sub="from a peak, limit 10%" />
          <Stat label="Net" value={money(data.metrics.netPnl)} tone={signClass(data.metrics.netPnl)}
            sub={`win rate ${num(data.metrics.winRate, 0)}% (not the measure)`} />
          <Stat label="Chance it's luck" value={mc ? `${num(mc.probabilityOfLossPercent, 0)}%` : "—"}
            sub="reshuffled results ending in a loss" />
        </div>
      )}

      <ul className="eval-checks">
        {data.checks.map((c) => (
          <li key={c.key} className={c.passed ? "pass" : "fail"}>
            <span className="eval-mark">{c.passed ? "✓" : "○"}</span>
            <span><span className="strong">{c.label}</span> <span className="muted small">{c.detail}</span></span>
          </li>
        ))}
      </ul>

      {setupMissing.length > 0 && (
        <div className="form-actions">
          <button className="primary" onClick={applySetup} disabled={busy}>{busy ? "Applying…" : "Use the evaluation setup"}</button>
          <span className="muted small">Forex only, no high-score extras: simpler, so the result means something.</span>
        </div>
      )}
      <ErrorNote error={actionError} />
    </Card>
  );
}
