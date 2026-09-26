import { useEffect, useState } from "react";
import { api } from "../api";
import { localTimeZone, time } from "../format";
import type { SignalSettings as Settings } from "../types";
import { Badge, Card, ErrorNote, Segmented } from "./Ui";
import { useMarketNames } from "../useMarketNames";

interface MarketSelection {
  selected: string[];
}

type NumberKey = "nearMissMinScore" | "maxOpenPositions" | "riskPerTradePercent" | "dailyLossLimitPercent" | "expiryMinutes" | "maxPriceMoveFraction";

const LIMITS: { key: NumberKey; label: string; step: number; min: number; max: number; unit: string; what: string }[] = [
  { key: "maxOpenPositions", label: "Signal slots", step: 1, min: 0, max: 10, unit: "positions",
    what: "Signal trades open at the same time. Separate from the bot's positions — signals never take the bot's slots." },
  { key: "riskPerTradePercent", label: "Risk per signal trade", step: 0.05, min: 0.05, max: 2, unit: "%",
    what: "How much of your equity one signal trade may lose at its stop. Position size is calculated from this." },
  { key: "dailyLossLimitPercent", label: "Signal daily loss budget", step: 0.1, min: 0.1, max: 10, unit: "%",
    what: "Once signal trades have lost this much today, new signals can't be traded (unless you allow loss-limit overrides below). The bot's own budget is not touched." },
  { key: "expiryMinutes", label: "Signal expires after", step: 1, min: 1, max: 120, unit: "min",
    what: "A signal you haven't acted on by then is dropped (it is still followed as a virtual trade so you can see what it would have done)." },
  { key: "maxPriceMoveFraction", label: "Refuse after price moved", step: 0.05, min: 0.05, max: 1, unit: "of the way",
    what: "When you trade a signal, the entry is re-priced at the live quote. If the price has already covered this share of the distance to the stop or the target, the signal is refused — the setup is gone." },
];

/** Settings › Signals: which markets send signals, near misses, signal limits, notifications and quiet hours. */
export function SignalSettings() {
  const [data, setData] = useState<Settings | null>(null);
  const [form, setForm] = useState<Settings | null>(null);
  const [markets, setMarkets] = useState<string[]>([]);
  const [error, setError] = useState<string | null>(null);
  const [saving, setSaving] = useState(false);
  const [saved, setSaved] = useState(false);
  const name = useMarketNames();

  const apply = (s: Settings) => {
    setData(s);
    setForm(s);
  };

  useEffect(() => {
    api.get<Settings>("/api/settings/signals").then(apply).catch((e: Error) => setError(e.message));
    api.get<MarketSelection>("/api/settings/markets").then((m) => setMarkets(m.selected ?? [])).catch(() => undefined);
  }, []);

  if (!form || !data) {
    return <Card title="Signals"><ErrorNote error={error} />{!error && <p className="empty">Loading…</p>}</Card>;
  }

  const set = (patch: Partial<Settings>) => {
    setSaved(false);
    setForm({ ...form, ...patch });
  };

  const setMode = (instrument: string, mode: "auto" | "signals") =>
    set({
      signalOnlyInstruments: mode === "signals"
        ? [...form.signalOnlyInstruments.filter((i) => i !== instrument), instrument]
        : form.signalOnlyInstruments.filter((i) => i !== instrument),
    });

  const save = async () => {
    if (form.allowLossLimitOverride && !data.allowLossLimitOverride
      && !window.confirm("Allow trading signals past the signal daily loss budget?\n\nYou will still have to type ACCEPT for each such trade.")) return;
    setSaving(true);
    setError(null);
    try {
      apply(await api.put<Settings>("/api/settings/signals", { ...form, timeZone: localTimeZone() }));
      setSaved(true);
    } catch (e) {
      setError((e as Error).message);
    } finally {
      setSaving(false);
    }
  };

  const dirty = JSON.stringify(form) !== JSON.stringify(data);
  // Markets no longer selected can still be listed as signals-only; show them so they can be switched back.
  const listed = [...new Set([...markets, ...form.signalOnlyInstruments])];

  return (
    <Card title="Signals" actions={<Badge tone={form.enabled ? "good" : "neutral"}>{form.enabled ? "on" : "off"}</Badge>}>
      <p className="hint">
        A signal is a setup the bot sends you instead of trading it. You decide on the Signals page (or from the phone notification); signal trades
        run through the same risk rules but have their own slots, risk and daily loss budget, so they never get in the way of the bot.
      </p>

      <label className="event-option">
        <input type="checkbox" checked={form.enabled} onChange={(e) => set({ enabled: e.target.checked })} />
        <span><span className="strong">Send signals</span><span className="muted small"> — off: markets set to “Signals” go back to automatic trading and no near misses are sent.</span></span>
      </label>

      <section className="risk-group">
        <h3>Market modes</h3>
        <p className="muted small">
          Auto: the bot trades the market itself. Signals: the bot never trades it — every qualifying setup is sent to you instead. To stop a
          market completely, deselect it under Settings › Markets.
        </p>
        {listed.length === 0 && <p className="empty">No markets selected.</p>}
        <div className="signal-modes">
          {listed.map((instrument) => (
            <div key={instrument} className="signal-mode">
              <span className="strong" title={instrument}>{name(instrument)}</span>
              {!markets.includes(instrument) && <span className="muted small"> not selected</span>}
              <Segmented
                value={form.signalOnlyInstruments.includes(instrument) ? "signals" : "auto"}
                options={[{ value: "auto", label: "Auto" }, { value: "signals", label: "Signals" }]}
                onChange={(mode) => setMode(instrument, mode)}
              />
            </div>
          ))}
        </div>
      </section>

      <section className="risk-group">
        <h3>Near misses</h3>
        <label className="event-option">
          <input type="checkbox" checked={form.nearMissEnabled} onChange={(e) => set({ nearMissEnabled: e.target.checked })} />
          <span>
            <span className="strong">Also send setups just below the automatic threshold</span>
            <span className="muted small"> — on any market (Auto ones too). The bot only trades 75+ on its own; these are yours to judge.</span>
          </span>
        </label>
        <div className="risk-fields">
          <div className="risk-field">
            <label className="risk-input">
              <span className="strong">Near miss from score</span>
              <span className="input-unit">
                <input type="number" min={40} max={100} step={1} value={form.nearMissMinScore} disabled={!form.nearMissEnabled}
                  onChange={(e) => set({ nearMissMinScore: Number(e.target.value) })} />
              </span>
            </label>
            <div className="risk-help">Setups scoring from this up to the automatic threshold are sent as near-miss signals.</div>
          </div>
        </div>
      </section>

      <section className="risk-group">
        <h3>Signal limits</h3>
        <div className="risk-fields">
          {LIMITS.map((f) => (
            <div key={f.key} className="risk-field">
              <label className="risk-input">
                <span className="strong">{f.label}</span>
                <span className="input-unit">
                  <input type="number" step={f.step} min={f.min} max={f.max} value={form[f.key]} onChange={(e) => set({ [f.key]: Number(e.target.value) })} />
                  <span className="muted">{f.unit}</span>
                </span>
              </label>
              <div className="risk-help">{f.what}</div>
            </div>
          ))}
        </div>
        <label className="event-option">
          <input type="checkbox" checked={form.allowLossLimitOverride} onChange={(e) => set({ allowLossLimitOverride: e.target.checked })} />
          <span>
            <span className="strong">Allow trading past a loss limit</span>
            <span className="muted small">
              {" "}— off by default. When on, a signal blocked by a loss limit can still be traded after you type ACCEPT. Safety rules (kill switch,
              stale data, closed market, sizing) can never be overridden.
            </span>
          </span>
        </label>
      </section>

      <section className="risk-group">
        <h3>Phone notifications</h3>
        <label className="event-option">
          <input type="checkbox" checked={form.oneTapFromNotification} onChange={(e) => set({ oneTapFromNotification: e.target.checked })} />
          <span>
            <span className="strong">Trade button on the notification</span>
            <span className="muted small">
              {" "}— trade with one tap, without opening the app. Only offered when every risk rule passes; otherwise the notification opens the
              review screen.
            </span>
          </span>
        </label>
        <div className="risk-fields">
          <div className="risk-field">
            <label className="risk-input">
              <span className="strong">Quiet hours</span>
              <span className="input-unit">
                <input type="time" value={form.quietHoursStart ?? ""} onChange={(e) => set({ quietHoursStart: e.target.value || null })} />
                <span className="muted">to</span>
                <input type="time" value={form.quietHoursEnd ?? ""} onChange={(e) => set({ quietHoursEnd: e.target.value || null })} />
              </span>
            </label>
            <div className="risk-help">
              No signal notifications between these times ({localTimeZone()}). Signals still appear on the Signals page. Leave empty for none.
            </div>
          </div>
        </div>
      </section>

      <div className="form-actions">
        <button className="primary" onClick={save} disabled={saving || !dirty}>{saving ? "Saving…" : "Save signal settings"}</button>
        {dirty && <button onClick={() => setForm(data)}>Discard changes</button>}
        {saved && <span className="pos small">Saved and applied.</span>}
      </div>
      {data.updatedAtUtc && <p className="muted small">Last changed {time(data.updatedAtUtc)} by {data.updatedBy}.</p>}
      <ErrorNote error={error} />
    </Card>
  );
}
