import type { SystemStatus } from "../types";
import type { PageId } from "../pages";
import { ago, money, signClass, time } from "../format";
import { Badge, Stat } from "./Ui";
import { Markets } from "./Markets";
import { OpenPositions } from "./Positions";

type Navigate = (page: PageId, section?: string) => void;

export function Overview({ status, refreshKey, navigate }: { status: SystemStatus; refreshKey: unknown; navigate: Navigate }) {
  const a = status.account;
  const totalReturn = a.balance - a.startingBalance;
  return (
    <div className="stack">
      <div className="grid-stats">
        <Stat label="Equity" value={money(a.equity, a.currency)} sub={`balance ${money(a.balance, a.currency)}`} />
        <Stat label="Open P&L" value={money(a.unrealizedPnl, a.currency)} tone={signClass(a.unrealizedPnl)} sub={`${status.openPositions} open position(s)`} />
        <Stat label="Today" value={money(status.dailyRealizedPnl, a.currency)} tone={signClass(status.dailyRealizedPnl)} sub="realized, market day" />
        <Stat label="This week" value={money(status.weeklyRealizedPnl, a.currency)} tone={signClass(status.weeklyRealizedPnl)} sub="realized" />
        <Stat label="Since start" value={money(totalReturn, a.currency)} tone={signClass(totalReturn)} sub={`from ${money(a.startingBalance, a.currency)}`} />
      </div>

      <div className="status-strip">
        <span>
          <Badge tone={status.workerHealthy ? "good" : "bad"}>{status.workerHealthy ? "Worker running" : "Worker down"}</Badge>
          <span className="muted small"> heartbeat {ago(status.workerHeartbeatUtc, status.serverTimeUtc)}</span>
        </span>
        <span>
          <Badge tone={status.marketData.isStale ? "bad" : "good"}>{status.marketData.isStale ? "Data stale" : "Data fresh"}</Badge>
          <span className="muted small"> last bar {time(status.marketData.lastBarTimeUtc)}</span>
        </span>
        <span>
          <Badge tone={status.broker.isDemo ? "good" : "bad"}>{status.broker.name}{status.broker.isDemo ? " demo" : ""}</Badge>
          <span className="muted small"> {status.broker.accountId ?? "not connected"}</span>
        </span>
        {status.consecutiveLosses > 0 && <Badge tone="warn">{status.consecutiveLosses} loss(es) in a row</Badge>}
        {status.cooldownUntilUtc && Date.parse(status.cooldownUntilUtc) > Date.parse(status.marketData.lastBarTimeUtc ?? "") && (
          <Badge tone="warn">cooldown until {time(status.cooldownUntilUtc)}</Badge>
        )}
      </div>

      <Markets refreshKey={refreshKey} navigate={navigate} compact />
      <OpenPositions refreshKey={refreshKey} compact onMore={() => navigate("trades")} />
    </div>
  );
}
