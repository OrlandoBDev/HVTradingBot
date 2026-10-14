import { useEffect, useState } from "react";
import { api } from "../api";
import { money, num, price, signClass, time } from "../format";
import type { PageId } from "../pages";
import type { Signal, SignalCheck, SignalStats } from "../types";
import { Badge, Card, Empty, ErrorNote, MarketName, Stat } from "./Ui";
import { Pagination, type Paged } from "./Pagination";

const OPEN: Signal["status"][] = ["Pending", "NeedsReview"];
const WORKING: Signal["status"][] = ["Accepted", "Placing"];
const LOSS_LIMIT_CONFIRMATION = "ACCEPT";

const STATUS: Record<Signal["status"], { label: string; tone: "good" | "bad" | "warn" | "neutral" | "info" }> = {
  Pending: { label: "waiting for you", tone: "info" },
  NeedsReview: { label: "review risks", tone: "warn" },
  Accepted: { label: "placing…", tone: "info" },
  Placing: { label: "placing…", tone: "info" },
  Placed: { label: "traded", tone: "good" },
  Skipped: { label: "skipped", tone: "neutral" },
  Expired: { label: "expired", tone: "neutral" },
  Failed: { label: "not traded", tone: "bad" },
};

const side = (s: Signal) => (s.direction === "Long" ? "Buy" : "Sell");

/** Ticks every second while mounted (for the expiry countdowns). */
function useNow() {
  const [now, setNow] = useState(Date.now());
  useEffect(() => {
    const id = window.setInterval(() => setNow(Date.now()), 1000);
    return () => window.clearInterval(id);
  }, []);
  return now;
}

function countdown(expiresAtUtc: string, now: number) {
  const seconds = Math.max(0, Math.round((Date.parse(expiresAtUtc) - now) / 1000));
  return seconds === 0 ? "expired" : `${Math.floor(seconds / 60)}:${String(seconds % 60).padStart(2, "0")} left`;
}

/**
 * Signals: setups sent to you instead of being traded by the bot. Waiting ones at the top (with a countdown), the one
 * opened from a notification (#/signals/{id}) as a review, then results and history.
 */
export function Signals({ refreshKey, section, navigate }: { refreshKey: string; section?: string; navigate: (page: PageId, section?: string) => void }) {
  const [active, setActive] = useState<Signal[]>([]);
  const [history, setHistory] = useState<Paged<Signal> | null>(null);
  const [stats, setStats] = useState<SignalStats | null>(null);
  const [page, setPage] = useState(1);
  const [pageSize, setPageSize] = useState(25);
  const [error, setError] = useState<string | null>(null);
  const now = useNow();

  const load = async () => {
    try {
      const [a, h, s] = await Promise.all([
        api.get<Paged<Signal>>("/api/signals?active=true&pageSize=50"),
        api.get<Paged<Signal>>(`/api/signals?page=${page}&pageSize=${pageSize}`),
        api.get<SignalStats>("/api/signals/stats"),
      ]);
      setActive(a.items);
      setHistory(h);
      setStats(s);
      setError(null);
    } catch (e) {
      setError((e as Error).message);
    }
  };

  // Faster while something is waiting or being placed.
  const busy = active.length > 0;
  useEffect(() => {
    void load();
    const id = window.setInterval(() => void load(), busy ? 3000 : 10000);
    return () => window.clearInterval(id);
  }, [page, pageSize, refreshKey, busy]);

  const open = (id?: string) => navigate("signals", id);

  return (
    <div className="stack">
      <ErrorNote error={error} />
      {section && <SignalReview id={section} now={now} onChanged={load} onClose={() => open()} />}

      <Card title={`Waiting for you${active.length ? ` (${active.length})` : ""}`}>
        {active.length === 0 ? (
          <Empty>
            No signals right now. Set markets to “Signals” or turn on near misses under{" "}
            <a href="#/settings/signals" onClick={(e) => { e.preventDefault(); navigate("settings", "signals"); }}>Settings › Signals</a>.
          </Empty>
        ) : (
          <div className="signal-list">
            {active.map((s) => (
              <SignalCard key={s.id} signal={s} now={now} selected={s.id === section} onOpen={() => open(s.id)} onChanged={load} />
            ))}
          </div>
        )}
      </Card>

      {stats && <SignalResults stats={stats} />}

      <Card title="History">
        {!history || history.items.length === 0 ? (
          <Empty>No signals yet.</Empty>
        ) : (
          <>
            <div className="table-wrap">
              <table>
                <thead>
                  <tr>
                    <th>Sent</th>
                    <th>Market</th>
                    <th>Side</th>
                    <th>Kind</th>
                    <th className="num">Score</th>
                    <th>Outcome</th>
                    <th className="num" title="Profit of the trade you took, or what a skipped signal would have made (in R)">Result</th>
                  </tr>
                </thead>
                <tbody>
                  {history.items.map((s) => (
                    <tr key={s.id} className="clickable" onClick={() => open(s.id)}>
                      <td className="nowrap">{time(s.createdAtUtc)}</td>
                      <td><MarketName symbol={s.instrument} /></td>
                      <td><Badge tone={s.direction === "Long" ? "good" : "bad"}>{side(s)}</Badge></td>
                      <td className="muted">{s.kind === "NearMiss" ? "near miss" : "signals market"}</td>
                      <td className="num">{s.score}</td>
                      <td>
                        <Badge tone={STATUS[s.status].tone}>{STATUS[s.status].label}</Badge>
                        {s.checks.some((c) => c.overridden) && <> <Badge tone="warn">risk accepted</Badge></>}
                      </td>
                      <td className="num"><SignalResult signal={s} /></td>
                    </tr>
                  ))}
                </tbody>
              </table>
            </div>
            <Pagination page={history.page} pageSize={history.pageSize} total={history.total} onPage={setPage}
              onPageSize={(size) => { setPageSize(size); setPage(1); }} />
          </>
        )}
      </Card>
    </div>
  );
}

function SignalResult({ signal }: { signal: Signal }) {
  const r = signal.result;
  if (!r) return <span className="muted">—</span>;
  if (r.taken) {
    return r.open ? <span className="muted">open</span> : <span className={signClass(r.pnl)}>{money(r.pnl)} · {num(r.rMultiple)}R</span>;
  }
  return r.open
    ? <span className="muted" title="Followed as a virtual trade until its stop or target">following…</span>
    : <span className={signClass(r.rMultiple)} title="What this signal would have made if you had traded it">would be {num(r.rMultiple)}R</span>;
}

function SignalCard({ signal: s, now, selected, onOpen, onChanged }: {
  signal: Signal; now: number; selected: boolean; onOpen: () => void; onChanged: () => void;
}) {
  const failing = s.checks.filter((c) => !c.passed);
  const hard = failing.filter((c) => !c.mayAccept);
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState<string | null>(null);

  const decide = async (trade: boolean) => {
    if (trade && !window.confirm(`${side(s)} ${s.instrument} now?\n\nThe entry is re-priced at the live quote and every risk rule is checked again.`)) return;
    setBusy(true);
    setError(null);
    try {
      await api.post(`/api/signals/${s.id}/${trade ? "accept" : "skip"}`, trade ? { acceptedRules: [] } : {});
      onChanged();
    } catch (e) {
      setError((e as Error).message);
    } finally {
      setBusy(false);
    }
  };

  const working = WORKING.includes(s.status);
  return (
    <div className={`signal-card ${selected ? "selected" : ""} ${s.status === "NeedsReview" ? "review" : ""}`}>
      <div className="signal-head">
        <Badge tone={s.direction === "Long" ? "good" : "bad"}>{side(s)}</Badge>
        <MarketName symbol={s.instrument} />
        <Badge tone={STATUS[s.status].tone}>{STATUS[s.status].label}</Badge>
        {!working && <span className="muted small signal-timer">{countdown(s.expiresAtUtc, now)}</span>}
      </div>
      <div className="signal-meta muted small">
        {s.kind === "NearMiss" ? "Near miss" : "Signals market"} · {s.strategy} · score {s.score}
        {s.regime && ` · ${s.regime}`}
      </div>
      <div className="signal-levels">
        <span>Entry <b>{price(s.entry, s.instrument)}</b></span>
        <span>Stop <b>{price(s.stopLoss, s.instrument)}</b></span>
        <span>Target <b>{price(s.takeProfit, s.instrument)}</b></span>
        <span>R:R <b>{num(s.rewardToRisk, 1)}</b></span>
        {s.riskAmount != null && <span>Risk <b>{money(s.riskAmount)}</b></span>}
      </div>
      <div className="small">
        {failing.length === 0
          ? <span className="pos">✓ Every risk rule passes</span>
          : <span className={hard.length ? "neg" : "warn-text"}>
              {hard.length ? "✕ " : "⚠ "}{failing.map((c) => c.rule).join(", ")}
              {hard.length ? " — blocked" : " — you can accept the risk"}
            </span>}
      </div>
      {s.message && s.status !== "Pending" && <p className="muted small">{s.message}</p>}
      {OPEN.includes(s.status) && (
        <div className="form-actions">
          {failing.length === 0 && s.status === "Pending"
            ? <button className="primary" onClick={() => decide(true)} disabled={busy}>Trade</button>
            : <button className="primary" onClick={onOpen}>Review</button>}
          <button onClick={() => decide(false)} disabled={busy}>Skip</button>
          {failing.length === 0 && s.status === "Pending" && <button className="link" onClick={onOpen}>Details</button>}
        </div>
      )}
      <ErrorNote error={error} />
    </div>
  );
}

/** One signal in full: every risk check, and accepting individual failed rules before trading. */
function SignalReview({ id, now, onChanged, onClose }: { id: string; now: number; onChanged: () => void; onClose: () => void }) {
  const [signal, setSignal] = useState<Signal | null>(null);
  const [accepted, setAccepted] = useState<Set<string>>(new Set());
  const [confirmation, setConfirmation] = useState("");
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState<string | null>(null);

  useEffect(() => {
    let cancelled = false;
    const load = () => api.get<Signal>(`/api/signals/${id}`).then((s) => !cancelled && setSignal(s)).catch((e: Error) => !cancelled && setError(e.message));
    void load();
    const timer = window.setInterval(load, 3000);
    return () => {
      cancelled = true;
      window.clearInterval(timer);
    };
  }, [id]);

  // A new review (the worker found other risks) starts with nothing accepted.
  useEffect(() => {
    setAccepted(new Set());
    setConfirmation("");
  }, [signal?.status, signal?.checks.map((c) => c.rule + c.passed).join()]);

  if (!signal) return error ? <Card title="Signal"><ErrorNote error={error} /></Card> : null;

  const failing = signal.checks.filter((c) => !c.passed);
  const blocking = failing.filter((c) => !c.mayAccept);
  const lossLimits = failing.filter((c) => c.kind === "LossLimit" && accepted.has(c.rule));
  const decidable = OPEN.includes(signal.status) && Date.parse(signal.expiresAtUtc) > now;
  const allAccepted = failing.every((c) => accepted.has(c.rule));
  const confirmed = lossLimits.length === 0 || confirmation.trim() === LOSS_LIMIT_CONFIRMATION;
  const canTrade = decidable && blocking.length === 0 && allAccepted && confirmed;

  const toggle = (rule: string) => {
    const next = new Set(accepted);
    if (next.has(rule)) next.delete(rule);
    else next.add(rule);
    setAccepted(next);
  };

  const trade = async () => {
    const risks = [...accepted];
    const text = risks.length
      ? `Trade ${side(signal)} ${signal.instrument} accepting these risks?\n\n${risks.join("\n")}\n\nThe trade is recorded as overridden in the audit log.`
      : `${side(signal)} ${signal.instrument} now?`;
    if (!window.confirm(`${text}\n\nThe entry is re-priced at the live quote and every rule is checked again.`)) return;
    await act(() => api.post(`/api/signals/${signal.id}/accept`, { acceptedRules: risks, confirmation }));
  };

  const act = async (call: () => Promise<unknown>) => {
    setBusy(true);
    setError(null);
    try {
      await call();
      setSignal(await api.get<Signal>(`/api/signals/${signal.id}`));
      onChanged();
    } catch (e) {
      setError((e as Error).message);
    } finally {
      setBusy(false);
    }
  };

  return (
    <Card
      title={<h2><Badge tone={signal.direction === "Long" ? "good" : "bad"}>{side(signal)}</Badge> <MarketName symbol={signal.instrument} /></h2>}
      actions={<button className="link" onClick={onClose}>Close</button>}
    >
      <p className="signal-meta">
        <Badge tone={STATUS[signal.status].tone}>{STATUS[signal.status].label}</Badge>{" "}
        {decidable && <span className="muted small">{countdown(signal.expiresAtUtc, now)} · </span>}
        <span className="muted small">
          {signal.kind === "NearMiss" ? "Near miss" : "Signals market"} · {signal.strategy} · score {signal.score} · sent {time(signal.createdAtUtc)}
        </span>
      </p>
      <div className="signal-levels">
        <span>Entry <b>{price(signal.entry, signal.instrument)}</b></span>
        <span>Stop <b>{price(signal.stopLoss, signal.instrument)}</b></span>
        <span>Target <b>{price(signal.takeProfit, signal.instrument)}</b></span>
        <span>R:R <b>{num(signal.rewardToRisk, 1)}</b></span>
        {signal.riskAmount != null && <span>Risk <b>{money(signal.riskAmount)}</b></span>}
        {signal.fillPrice != null && <span>Filled <b>{price(signal.fillPrice, signal.instrument)}</b></span>}
      </div>
      {signal.message && <p className={signal.status === "Failed" ? "neg" : "muted"}>{signal.message}</p>}
      {signal.result && <p>Result: <SignalResult signal={signal} /></p>}

      <h3>Risk check</h3>
      <p className="muted small">
        {decidable
          ? "Checked when the signal was sent; every rule runs again when you trade. Red rules protect the account and can't be accepted."
          : "The rules as last checked for this signal."}
      </p>
      <div className="signal-checks">
        {failing.map((c) => (
          <CheckRow key={c.rule} check={c} decidable={decidable} accepted={accepted.has(c.rule)} onToggle={() => toggle(c.rule)} />
        ))}
        {signal.checks.filter((c) => c.passed && c.overridden).map((c) => <CheckRow key={c.rule} check={c} decidable={false} accepted onToggle={() => undefined} />)}
      </div>
      <details className="signal-passing">
        <summary>{signal.checks.filter((c) => c.passed && !c.overridden).length} rule(s) pass</summary>
        <div className="signal-checks">
          {signal.checks.filter((c) => c.passed && !c.overridden).map((c) => (
            <CheckRow key={c.rule} check={c} decidable={false} accepted={false} onToggle={() => undefined} />
          ))}
        </div>
      </details>

      {decidable && lossLimits.length > 0 && (
        <label className="risk-input signal-confirm">
          <span className="strong neg">You are trading past a loss limit. Type {LOSS_LIMIT_CONFIRMATION} to confirm.</span>
          <input value={confirmation} onChange={(e) => setConfirmation(e.target.value)} placeholder={LOSS_LIMIT_CONFIRMATION} autoCapitalize="characters" />
        </label>
      )}

      {decidable && (
        <div className="form-actions">
          <button className={failing.length ? "danger" : "primary"} onClick={trade} disabled={busy || !canTrade}>
            {failing.length ? "Accept risk & trade" : "Trade"}
          </button>
          <button onClick={() => act(() => api.post(`/api/signals/${signal.id}/skip`, {}))} disabled={busy}>Skip</button>
          {blocking.length > 0 && <span className="neg small">Blocked by {blocking.map((c) => c.rule).join(", ")}.</span>}
          {blocking.length === 0 && !allAccepted && <span className="muted small">Tick each failed rule to accept its risk.</span>}
        </div>
      )}
      <ErrorNote error={error} />
    </Card>
  );
}

function CheckRow({ check: c, decidable, accepted, onToggle }: { check: SignalCheck; decidable: boolean; accepted: boolean; onToggle: () => void }) {
  const result = c.overridden
    ? <Badge tone="warn">accepted</Badge>
    : c.passed
      ? <Badge tone="good">pass</Badge>
      : <Badge tone={c.mayAccept ? "warn" : "bad"}>{c.mayAccept ? "fails" : "blocks"}</Badge>;
  return (
    <div className={`signal-check ${!c.passed && !c.mayAccept ? "bad" : !c.passed || c.overridden ? "warn" : ""}`}>
      <div className="signal-check-head">
        {result}
        <span className="strong">{c.rule}</span>
        {c.kind === "LossLimit" && <span className="muted small">loss limit</span>}
        {decidable && !c.passed && (c.mayAccept
          ? <label className="check signal-accept"><input type="checkbox" checked={accepted} onChange={onToggle} /> accept risk</label>
          : <span className="muted small signal-accept">{c.kind === "LossLimit" ? "not allowed in settings" : "safety rule"}</span>)}
      </div>
      <div className="small muted">{c.detail}</div>
    </div>
  );
}

/** Taken signals (overridden ones separately) and what the skipped and expired ones would have made. */
function SignalResults({ stats: s }: { stats: SignalStats }) {
  const winRate = s.takenClosed === 0 ? null : (s.takenWins / s.takenClosed) * 100;
  const missedWinRate = s.missedResolved === 0 ? null : (s.missedWins / s.missedResolved) * 100;
  return (
    <Card title="Signal results">
      <div className="grid-stats">
        <Stat label="Taken" value={money(s.takenPnl)} tone={signClass(s.takenPnl)}
          sub={`${s.taken} trade(s), ${num(s.takenR)}R${winRate == null ? "" : `, ${winRate.toFixed(0)}% won`}`} />
        <Stat label="Risk accepted" value={money(s.overriddenPnl)} tone={signClass(s.overriddenPnl)} sub={`${s.overridden} trade(s) past a failed rule`} />
        <Stat label="Skipped / expired" value={`${s.skipped} / ${s.expired}`} />
        <Stat label="Missed (what if)" value={`${num(s.missedR)}R`} tone={signClass(s.missedR)}
          sub={s.missedResolved === 0 ? "followed until stop or target" : `${s.missedResolved} resolved${missedWinRate == null ? "" : `, ${missedWinRate.toFixed(0)}% would have won`}`} />
      </div>
    </Card>
  );
}
