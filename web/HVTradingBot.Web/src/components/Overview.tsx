import type { PnlPeriods, ProfitSummary, SystemStatus } from "../types";
import type { PageId } from "../pages";
import { ago, localTimeZone, money, signClass, time } from "../format";
import { Badge, Card, Stat } from "./Ui";
import { useData } from "../useData";
import { Markets } from "./Markets";
import { OpenPositions } from "./Positions";

type Navigate = (page: PageId, section?: string) => void;

export function Overview({ status, refreshKey, navigate }: { status: SystemStatus; refreshKey: unknown; navigate: Navigate }) {
  const a = status.account;
  const { data: profit } = useData<ProfitSummary>(`/api/profit?tz=${encodeURIComponent(localTimeZone())}`, refreshKey);
  const account = profit?.account;
  const scope = profit?.accountFromBroker ? "whole account" : "all trades";
  return (
    <div className="stack">
      {/* The whole broker account: every contract, including ones opened outside this app. */}
      <div className="grid-stats">
        <Stat label="Equity" value={money(a.equity, a.currency)} sub={`balance ${money(a.balance, a.currency)}`} />
        <Stat label="Open P&L" value={money(a.unrealizedPnl, a.currency)} tone={signClass(a.unrealizedPnl)} sub={`${status.openPositions} open position(s)`} />
        <Stat label="Today" value={money(account?.today, a.currency)} tone={signClass(account?.today)} sub={`realized, ${scope}`} />
        <Stat label="This week" value={money(account?.week, a.currency)} tone={signClass(account?.week)} sub={`realized since Monday, ${scope}`} />
        <Stat label="This month" value={money(account?.month, a.currency)} tone={signClass(account?.month)} sub={`realized, ${scope}`} />
      </div>

      {profit && <AppPnl app={profit.app} currency={profit.currency} fromBroker={profit.accountFromBroker} />}
      {profit?.signals && <SignalPnl signals={profit.signals} currency={profit.currency} />}

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

/** The only app-specific numbers on the overview: profit from trades this app placed. */
function AppPnl({ app, currency, fromBroker }: { app: PnlPeriods; currency: string; fromBroker: boolean }) {
  const winRate = app.closedTrades === 0 ? null : (app.wins / app.closedTrades) * 100;
  return (
    <Card title="App P/L">
      <p className="hint">
        Only trades the bot placed (signal trades are shown separately){fromBroker ? "; the figures above cover every trade on the broker account" : ""}. Days and weeks in your local time.
      </p>
      <div className="grid-stats">
        <Stat label="Today" value={money(app.today, currency)} tone={signClass(app.today)} sub="realized" />
        <Stat label="This week" value={money(app.week, currency)} tone={signClass(app.week)} sub="realized since Monday" />
        <Stat label="This month" value={money(app.month, currency)} tone={signClass(app.month)} sub="realized" />
        <Stat label="All time" value={money(app.allTime, currency)} tone={signClass(app.allTime)}
          sub={`${app.closedTrades} trade(s)${winRate == null ? "" : `, ${winRate.toFixed(0)}% won`}`} />
        <Stat label="Open" value={money(app.open, currency)} tone={signClass(app.open)} sub={`${app.openTrades} open position(s)`} />
      </div>
    </Card>
  );
}

/** Trades you took from signals: kept apart from the bot's own results. */
function SignalPnl({ signals, currency }: { signals: PnlPeriods; currency: string }) {
  const winRate = signals.closedTrades === 0 ? null : (signals.wins / signals.closedTrades) * 100;
  return (
    <Card title="Signal trades P/L">
      <p className="hint">Trades you chose from signals. They have their own slots and loss budget and are not part of App P/L.</p>
      <div className="grid-stats">
        <Stat label="Today" value={money(signals.today, currency)} tone={signClass(signals.today)} sub="realized" />
        <Stat label="This week" value={money(signals.week, currency)} tone={signClass(signals.week)} sub="realized since Monday" />
        <Stat label="All time" value={money(signals.allTime, currency)} tone={signClass(signals.allTime)}
          sub={`${signals.closedTrades} trade(s)${winRate == null ? "" : `, ${winRate.toFixed(0)}% won`}`} />
        <Stat label="Open" value={money(signals.open, currency)} tone={signClass(signals.open)} sub={`${signals.openTrades} open position(s)`} />
      </div>
    </Card>
  );
}
