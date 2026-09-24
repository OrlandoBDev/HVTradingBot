import type { SystemStatus } from "../types";
import { ago, money, signClass, time } from "../format";
import { Badge, Card, Stat } from "./Ui";
import { Markets } from "./Markets";
import { Positions } from "./Positions";

export function Overview({ status, refreshKey }: { status: SystemStatus; refreshKey: unknown }) {
  const a = status.account;
  return (
    <>
      <div className="grid-stats">
        <Stat label="Equity" value={money(a.equity, a.currency)} />
        <Stat label="Balance" value={money(a.balance, a.currency)} />
        <Stat label="Unrealized P&L" value={money(a.unrealizedPnl, a.currency)} tone={signClass(a.unrealizedPnl)} />
        <Stat label="Total return" value={money(a.balance - a.startingBalance, a.currency)} tone={signClass(a.balance - a.startingBalance)} />
        <Stat label="Today (market day)" value={money(status.dailyRealizedPnl, a.currency)} tone={signClass(status.dailyRealizedPnl)} />
        <Stat label="This week" value={money(status.weeklyRealizedPnl, a.currency)} tone={signClass(status.weeklyRealizedPnl)} />
        <Stat label="Open positions" value={status.openPositions} />
        <Stat label="Loss streak" value={status.consecutiveLosses} />
      </div>
      <Card title="System">
        <dl className="kv">
          <dt>Mode</dt>
          <dd><Badge tone="info">{status.mode}</Badge></dd>
          <dt>Broker</dt>
          <dd>
            {status.broker.name === "Deriv" ? (
              <>
                <Badge tone={status.broker.isDemo ? "good" : "bad"}>{status.broker.isDemo ? "Deriv DEMO" : "Deriv REAL"}</Badge>{" "}
                account {status.broker.accountId ?? "connecting…"} · multiplier contracts with broker-side stop loss / take profit
              </>
            ) : (
              <><Badge tone="neutral">{status.broker.name}</Badge> local simulated fills</>
            )}
          </dd>
          <dt>Worker</dt>
          <dd>
            <Badge tone={status.workerHealthy ? "good" : "bad"}>{status.workerHealthy ? "running" : "not running"}</Badge> heartbeat{" "}
            {ago(status.workerHeartbeatUtc, status.serverTimeUtc)}
          </dd>
          <dt>Market data</dt>
          <dd>
            <Badge tone={status.marketData.isStale ? "bad" : "good"}>{status.marketData.isStale ? "stale" : "fresh"}</Badge> market time{" "}
            {time(status.marketData.lastBarTimeUtc)}{status.broker.name === "Deriv" ? " (Deriv)" : ""}
          </dd>
          <dt>Cooldown</dt>
          <dd>{status.cooldownUntilUtc && Date.parse(status.cooldownUntilUtc) > Date.parse(status.marketData.lastBarTimeUtc ?? "") ? `until ${time(status.cooldownUntilUtc)}` : "none"}</dd>
        </dl>
      </Card>
      <Markets refreshKey={refreshKey} />
      <Positions refreshKey={refreshKey} />
    </>
  );
}
