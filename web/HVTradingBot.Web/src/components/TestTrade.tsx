import { useEffect, useState } from "react";
import type { Market } from "../types";
import { api } from "../api";
import { money, time } from "../format";
import { Badge, Card, ErrorNote } from "./Ui";

interface TestTradeData {
  id: string;
  instrument: string;
  status: "Pending" | "Opening" | "Open" | "Closing" | "Closed" | "Failed";
  message: string | null;
  requestedBy: string;
  requestedAtUtc: string;
  openedAtUtc: string | null;
  closedAtUtc: string | null;
  clientOrderId: string | null;
  fillPrice: number | null;
  exitPrice: number | null;
  realizedPnl: number | null;
  holdSeconds: number;
}

const STEPS: { key: TestTradeData["status"][]; label: string }[] = [
  { key: ["Pending", "Opening", "Open", "Closing", "Closed"], label: "Requested" },
  { key: ["Open", "Closing", "Closed"], label: "Order filled" },
  { key: ["Closing", "Closed"], label: "Closed at broker" },
  { key: ["Closed"], label: "Result recorded" },
];

const ACTIVE = ["Pending", "Opening", "Open", "Closing"];

/** Places one real order through the full engine on the demo account, holds it for a minute and closes it. */
export function TestTrade() {
  const [markets, setMarkets] = useState<Market[]>([]);
  const [instrument, setInstrument] = useState("");
  const [trades, setTrades] = useState<TestTradeData[]>([]);
  const [error, setError] = useState<string | null>(null);
  const [busy, setBusy] = useState(false);

  const load = async () => {
    try {
      const [m, t] = await Promise.all([api.get<Market[]>("/api/markets"), api.get<TestTradeData[]>("/api/test-trades")]);
      const usable = m.filter((x) => x.isOpen && x.isTradable && !x.isLoading);
      setMarkets(usable);
      setInstrument((current) => current || usable[0]?.instrument || "");
      setTrades(t);
    } catch (e) {
      setError((e as Error).message);
    }
  };

  useEffect(() => {
    void load();
  }, []);

  const active = trades.find((t) => ACTIVE.includes(t.status));
  useEffect(() => {
    if (!active) return;
    const id = window.setInterval(() => void load(), 3000);
    return () => window.clearInterval(id);
  }, [active?.id, active?.status]);

  const place = async () => {
    const name = markets.find((m) => m.instrument === instrument)?.displayName ?? instrument;
    if (!window.confirm(`Place a test trade on ${name}?\n\nA real order is sent to your Deriv DEMO account through the full engine (risk checks, broker, journal, email), held for 60 seconds and then closed.`)) return;
    setBusy(true);
    setError(null);
    try {
      await api.post("/api/test-trades", { instrument });
      await load();
    } catch (e) {
      setError((e as Error).message);
    } finally {
      setBusy(false);
    }
  };

  const latest = trades[0];

  return (
    <Card title="Test trade">
      <p className="hint">
        See the whole pipeline work on demand: one real order on your demo account, checked by every risk rule, sent to the broker, held for
        60 seconds, closed, and recorded in the journal, trade history and your email. It is sized like a normal trade, but because it is
        closed after a minute the cost is usually just the spread and commission.
      </p>
      <div className="form-row">
        <label>
          Market
          <select value={instrument} onChange={(e) => setInstrument(e.target.value)} disabled={!!active || markets.length === 0}>
            {markets.length === 0 && <option value="">No open, tradable market</option>}
            {markets.map((m) => (
              <option key={m.instrument} value={m.instrument}>{m.displayName}</option>
            ))}
          </select>
        </label>
        <button className="primary" onClick={place} disabled={busy || !!active || !instrument}>
          {active ? "Test trade running…" : busy ? "Requesting…" : "Place test trade"}
        </button>
      </div>
      <ErrorNote error={error} />

      {latest && (
        <div className="test-trade">
          <div className="steps">
            {STEPS.map((step, i) => {
              // For a failed trade: "Requested" happened, "Order filled" only if there was a fill; the next step is where it stopped.
              const failedAt = latest.status === "Failed" ? (latest.fillPrice != null ? 2 : 1) : -1;
              const done = latest.status === "Failed" ? i < failedAt : step.key.includes(latest.status);
              const failed = i === failedAt;
              return (
                <div key={step.label} className={`step ${done ? "done" : ""} ${failed ? "failed" : ""}`}>
                  <span className="step-dot">{done ? "✓" : failed ? "✕" : i + 1}</span>
                  {step.label}
                </div>
              );
            })}
          </div>
          <p className="test-trade-message">
            <Badge tone={latest.status === "Closed" ? "good" : latest.status === "Failed" ? "bad" : "info"}>{latest.status}</Badge>{" "}
            <span className="strong">{latest.instrument}</span> · {latest.message}
          </p>
          <p className="muted small">
            Requested {time(latest.requestedAtUtc)}
            {latest.fillPrice != null && ` · filled at ${latest.fillPrice}`}
            {latest.exitPrice != null && ` · closed at ${latest.exitPrice}`}
            {latest.realizedPnl != null && <> · result <span className={latest.realizedPnl < 0 ? "neg" : "pos"}>{money(latest.realizedPnl)}</span></>}
            {latest.clientOrderId && ` · order ${latest.clientOrderId}`}
          </p>
        </div>
      )}
    </Card>
  );
}
