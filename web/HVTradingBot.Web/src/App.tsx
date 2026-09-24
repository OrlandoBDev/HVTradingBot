import { useState } from "react";
import { api } from "./api";
import { useStatus } from "./useStatus";
import type { SystemStatus } from "./types";
import { Badge } from "./components/Ui";
import { Overview } from "./components/Overview";
import { Markets } from "./components/Markets";
import { Decisions } from "./components/Decisions";
import { Positions, TradeHistory } from "./components/Positions";
import { Risk } from "./components/Risk";
import { PerformanceView } from "./components/PerformanceView";
import { Backtest } from "./components/Backtest";
import { Audit } from "./components/Audit";
import { KillSwitch } from "./components/KillSwitch";
import { Settings } from "./components/Settings";
import { LearningView } from "./components/LearningView";

const TABS = ["Overview", "Markets", "Decisions", "Positions", "History", "Risk", "Performance", "Learning", "Backtest", "Audit", "Settings"] as const;
type Tab = (typeof TABS)[number];

export function App() {
  const { status, connected, setStatus } = useStatus();
  const [tab, setTab] = useState<Tab>("Overview");

  // Tables refetch when the market advances (a new bar) or the kill switch changes, not on every status push.
  const refreshKey = `${status?.marketData.lastBarTimeUtc}|${status?.killSwitchActive}|${status?.openPositions}`;
  const refreshStatus = () => api.get<SystemStatus>("/api/status").then(setStatus).catch(() => undefined);

  return (
    <div className="app">
      <header className="topbar">
        <div className="brand">
          <span className="logo">HV</span>
          <div>
            <h1>HVTradingBot</h1>
            <span className="muted small">Risk-first multi-market engine · evaluates every 5 minutes · default decision is NO_TRADE</span>
          </div>
        </div>
        {status && (
          <div className="topbar-status">
            <Badge tone="info">{status.mode}</Badge>
            <Badge tone={status.broker.isDemo ? "good" : "bad"}>
              {status.broker.name}{status.broker.name === "Deriv" ? (status.broker.isDemo ? " demo" : " REAL") : ""}
              {status.broker.accountId ? ` · ${status.broker.accountId}` : ""}
            </Badge>
            <Badge tone={status.workerHealthy ? "good" : "bad"}>worker {status.workerHealthy ? "up" : "down"}</Badge>
            <Badge tone={status.marketData.isStale ? "bad" : "good"}>data {status.marketData.isStale ? "stale" : "fresh"}</Badge>
            <Badge tone={connected ? "good" : "warn"}>{connected ? "live" : "reconnecting"}</Badge>
            <KillSwitch status={status} onChanged={refreshStatus} />
          </div>
        )}
      </header>

      {status?.killSwitchActive && (
        <div className="banner">Kill switch active: {status.killSwitchReason}. New orders are blocked; open positions still exit at their broker-side stop or target.</div>
      )}

      <nav className="tabs">
        {TABS.map((t) => (
          <button key={t} className={t === tab ? "active" : ""} onClick={() => setTab(t)}>{t}</button>
        ))}
      </nav>

      <main>
        {!status && <p className="empty">Connecting to the API…</p>}
        {status && tab === "Overview" && <Overview status={status} refreshKey={refreshKey} />}
        {tab === "Markets" && <Markets refreshKey={refreshKey} />}
        {tab === "Decisions" && <Decisions refreshKey={refreshKey} />}
        {tab === "Positions" && <Positions refreshKey={refreshKey} />}
        {tab === "History" && <TradeHistory refreshKey={refreshKey} />}
        {tab === "Risk" && <Risk refreshKey={refreshKey} />}
        {tab === "Performance" && <PerformanceView refreshKey={refreshKey} />}
        {tab === "Learning" && <LearningView refreshKey={refreshKey} />}
        {tab === "Backtest" && <Backtest />}
        {tab === "Audit" && <Audit refreshKey={refreshKey} />}
        {tab === "Settings" && <Settings />}
      </main>
      <footer className="muted small">Demo-account trading. Demo and backtest results are not evidence of a real trading edge. Not financial advice.</footer>
    </div>
  );
}
