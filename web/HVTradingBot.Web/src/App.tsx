import { useCallback, useEffect, useState } from "react";
import { api, UNAUTHORIZED_EVENT } from "./api";
import { useStatus } from "./useStatus";
import { useRoute } from "./router";
import { GROUPS, PAGES, pageInfo } from "./pages";
import type { AuthStatus, SystemStatus } from "./types";
import { Badge } from "./components/Ui";
import { PageHeader } from "./components/PageHeader";
import { Overview } from "./components/Overview";
import { Markets } from "./components/Markets";
import { Decisions } from "./components/Decisions";
import { Trades } from "./components/Positions";
import { Risk } from "./components/Risk";
import { PerformanceView } from "./components/PerformanceView";
import { Backtest } from "./components/Backtest";
import { Audit } from "./components/Audit";
import { KillSwitch } from "./components/KillSwitch";
import { Settings } from "./components/Settings";
import { LearningView } from "./components/LearningView";
import { Login } from "./components/Login";

/** Shows the sign-in page until there is a session, then the dashboard. */
export function App() {
  const [auth, setAuth] = useState<AuthStatus | null>(null);
  const [authError, setAuthError] = useState<string | null>(null);

  const check = useCallback(() => {
    api.get<AuthStatus>("/api/auth/me").then((a) => { setAuth(a); setAuthError(null); }).catch((e: Error) => setAuthError(e.message));
  }, []);

  useEffect(() => {
    check();
    window.addEventListener(UNAUTHORIZED_EVENT, check);
    return () => window.removeEventListener(UNAUTHORIZED_EVENT, check);
  }, [check]);

  if (!auth) {
    return <div className="login-page"><p className="empty">{authError ? `Cannot reach the API: ${authError}` : "Loading…"}</p></div>;
  }

  if (!auth.authenticated) {
    return <Login setupRequired={auth.setupRequired} onSignedIn={setAuth} />;
  }

  const signOut = () => api.post("/api/auth/logout", {}).finally(check);
  return <Dashboard username={auth.username ?? ""} onSignOut={signOut} />;
}

function Dashboard({ username, onSignOut }: { username: string; onSignOut: () => void }) {
  const { status, connected, setStatus } = useStatus();
  const [route, navigate] = useRoute();
  const [menuOpen, setMenuOpen] = useState(false);

  // Tables refetch when the market advances (a new bar) or the kill switch changes, not on every status push.
  const refreshKey = `${status?.marketData.lastBarTimeUtc}|${status?.killSwitchActive}|${status?.openPositions}`;
  const refreshStatus = () => api.get<SystemStatus>("/api/status").then(setStatus).catch(() => undefined);
  const page = pageInfo(route.page);

  const go = (id: typeof route.page, section?: string) => {
    setMenuOpen(false);
    navigate(id, section);
  };

  return (
    <div className="shell">
      <header className="topbar">
        <button className="menu-button" aria-label="Menu" onClick={() => setMenuOpen((o) => !o)}>☰</button>
        <div className="brand">
          <span className="logo">HV</span>
          <span className="brand-name">HVTradingBot</span>
        </div>
        {status && (
          <div className="topbar-status">
            <span className="pill-group">
              <Badge tone="info">{status.mode}</Badge>
              <Badge tone={status.broker.isDemo ? "good" : "bad"}>
                {status.broker.name}
                {status.broker.name === "Deriv" ? (status.broker.isDemo ? " demo" : " REAL") : ""}
              </Badge>
            </span>
            <span className="health" title={`Worker ${status.workerHealthy ? "running" : "not running"} · data ${status.marketData.isStale ? "stale" : "fresh"} · dashboard ${connected ? "live" : "reconnecting"}`}>
              <span className={`dot ${status.workerHealthy ? "ok" : "bad"}`} />worker
              <span className={`dot ${status.marketData.isStale ? "bad" : "ok"}`} />data
              <span className={`dot ${connected ? "ok" : "warn"}`} />live
            </span>
            <KillSwitch status={status} onChanged={refreshStatus} />
          </div>
        )}
        <div className="user-menu">
          <span className="user-name" title="Signed in">{username}</span>
          <button onClick={onSignOut}>Sign out</button>
        </div>
      </header>

      <div className="body">
        <nav className={`sidebar ${menuOpen ? "open" : ""}`}>
          {GROUPS.map((group) => (
            <div key={group} className="nav-group">
              <div className="nav-group-label">{group}</div>
              {PAGES.filter((p) => p.group === group).map((p) => (
                <button key={p.id} className={`nav-item ${p.id === route.page ? "active" : ""}`} onClick={() => go(p.id)}>
                  <span className="nav-icon">{p.icon}</span>
                  {p.label}
                </button>
              ))}
            </div>
          ))}
          <p className="sidebar-note">Demo account · results are not evidence of a real edge · not financial advice.</p>
        </nav>
        {menuOpen && <div className="scrim" onClick={() => setMenuOpen(false)} />}

        <main className="content">
          {status?.killSwitchActive && (
            <div className="banner">
              <b>Kill switch active</b> — {status.killSwitchReason}. New orders are blocked; open positions still exit at their broker-side stop or target.
            </div>
          )}
          <PageHeader page={page} />
          {!status && <p className="empty">Connecting to the API…</p>}
          {status && route.page === "overview" && <Overview status={status} refreshKey={refreshKey} navigate={go} />}
          {route.page === "markets" && <Markets refreshKey={refreshKey} navigate={go} />}
          {route.page === "trades" && <Trades refreshKey={refreshKey} view={route.section} navigate={go} />}
          {route.page === "decisions" && <Decisions refreshKey={refreshKey} />}
          {route.page === "learning" && <LearningView refreshKey={refreshKey} />}
          {route.page === "performance" && <PerformanceView refreshKey={refreshKey} />}
          {route.page === "backtest" && <Backtest />}
          {route.page === "risk" && <Risk refreshKey={refreshKey} navigate={go} />}
          {route.page === "audit" && <Audit refreshKey={refreshKey} />}
          {route.page === "settings" && <Settings section={route.section} navigate={go} />}
        </main>
      </div>
    </div>
  );
}
