import { useEffect, useState } from "react";
import { api } from "../api";
import { money } from "../format";
import { Badge, Card, ErrorNote } from "./Ui";

interface RiskLimits {
  maxRiskPerTradePercent: number;
  maxDailyLossPercent: number;
  maxWeeklyLossPercent: number;
  maxOpenPositions: number;
  minRewardToRisk: number;
  maxConsecutiveLosses: number;
  cooldownMinutes: number;
  maxCurrencyExposure: number;
  maxCommissionShareOfRisk: number;
  maxDerivedOpenPositions: number;
  derivedRiskPerTradePercent: number;
  maxDerivedDailyLossPercent: number;
}

interface RiskSettingsData {
  effective: RiskLimits;
  defaults: RiskLimits;
  isCustomized: boolean;
  version: number;
  updatedAtUtc: string | null;
  updatedBy: string | null;
  balance: number;
  currency: string;
}

type Ctx = { v: number; f: RiskLimits; b: number; c: string };
const pct = (b: number, p: number, c: string) => money((b * p) / 100, c);

type Field = {
  key: keyof RiskLimits;
  label: string;
  step: number;
  min: number;
  max: number;
  unit: string;
  what: string;
  example: (x: Ctx) => string;
};

const GROUPS: { title: string; intro: string; fields: Field[] }[] = [
  {
    title: "All markets",
    intro: "Apply to every trade, Forex and Derived together.",
    fields: [
      {
        key: "maxRiskPerTradePercent", label: "Risk per trade", step: 0.1, min: 0.1, max: 3, unit: "%",
        what: "How much of your equity one trade may lose if its stop loss is hit. Position size is calculated from this.",
        example: ({ v, b, c }) => `At ${money(b, c)}: each Forex trade risks up to ${pct(b, v, c)}.`,
      },
      {
        key: "maxDailyLossPercent", label: "Daily loss limit", step: 0.5, min: 0.5, max: 10, unit: "%",
        what: "Once realized losses for the day reach this, no new trades are placed until the next day and the kill switch turns on.",
        example: ({ v, f, b, c }) => `${pct(b, v, c)} a day — about ${Math.max(1, Math.floor(v / f.maxRiskPerTradePercent))} full losing trade(s) at ${f.maxRiskPerTradePercent}% each.`,
      },
      {
        key: "maxWeeklyLossPercent", label: "Weekly loss limit", step: 0.5, min: 1, max: 25, unit: "%",
        what: "Same as the daily limit, measured Monday to Sunday.",
        example: ({ v, b, c }) => `Trading pauses for the rest of the week after losing ${pct(b, v, c)}.`,
      },
      {
        key: "maxOpenPositions", label: "Max open positions", step: 1, min: 1, max: 10, unit: "",
        what: "Total positions open at the same time, across all markets.",
        example: ({ v, f, b, c }) => `Up to ${v} at once; worst case all stop out together: about ${pct(b, v * f.maxRiskPerTradePercent, c)}.`,
      },
      {
        key: "minRewardToRisk", label: "Min reward : risk", step: 0.1, min: 1, max: 5, unit: ": 1",
        what: "The take-profit distance must be at least this many times the stop-loss distance.",
        example: ({ v, f, b, c }) => `Risking ${pct(b, f.maxRiskPerTradePercent, c)} requires a target worth at least ${pct(b, f.maxRiskPerTradePercent * v, c)}.`,
      },
      {
        key: "maxCurrencyExposure", label: "Max same-currency exposure", step: 1, min: 1, max: 5, unit: "positions",
        what: "Limits positions that all bet on the same currency, because they tend to win or lose together.",
        example: ({ v }) => `With ${v}: long EUR/USD + long GBP/USD are allowed (USD −2), a third USD-short trade is refused.`,
      },
    ],
  },
  {
    title: "Derived markets",
    intro: "Extra limits for Derived (synthetic) markets so they cannot crowd out Forex. Derived prices are random by design, so they get less money.",
    fields: [
      {
        key: "maxDerivedOpenPositions", label: "Max Derived positions at once", step: 1, min: 0, max: 10, unit: "",
        what: "Derived positions count toward the total, but never more than this many — the remaining slots stay free for Forex. 0 = no Derived trading.",
        example: ({ v, f }) => `With ${f.maxOpenPositions} total and ${v} Derived: at least ${Math.max(0, f.maxOpenPositions - v)} slot(s) are always available for Forex.`,
      },
      {
        key: "derivedRiskPerTradePercent", label: "Risk per Derived trade", step: 0.1, min: 0.1, max: 3, unit: "%",
        what: "Replaces the normal risk per trade for Derived markets (it can never be higher).",
        example: ({ v, f, b, c }) => `A Derived trade risks ${pct(b, v, c)} instead of ${pct(b, f.maxRiskPerTradePercent, c)}.`,
      },
      {
        key: "maxDerivedDailyLossPercent", label: "Max Derived loss per day", step: 0.1, min: 0.1, max: 10, unit: "%",
        what: "After Derived trades lose this much in a day, only Derived pauses until tomorrow — Forex keeps trading.",
        example: ({ v, f, b, c }) => `Derived stops for the day after losing ${pct(b, v, c)} (about ${Math.max(1, Math.floor(v / f.derivedRiskPerTradePercent))} losing Derived trade(s)).`,
      },
    ],
  },
  {
    title: "Losing streaks & costs",
    intro: "Protection against bad runs and trades that cost more than they could earn.",
    fields: [
      {
        key: "maxConsecutiveLosses", label: "Losses before cooldown", step: 1, min: 1, max: 10, unit: "",
        what: "After this many losing trades in a row, trading pauses for the cooldown period.",
        example: ({ v, f }) => `After ${v} losses in a row the engine waits ${f.cooldownMinutes} minutes before trading again.`,
      },
      {
        key: "cooldownMinutes", label: "Cooldown", step: 15, min: 0, max: 1440, unit: "min",
        what: "How long trading pauses after the losing streak above (market time).",
        example: ({ v }) => `${v} minutes = ${(v / 60).toFixed(v % 60 === 0 ? 0 : 1)} hour(s).`,
      },
      {
        key: "maxCommissionShareOfRisk", label: "Max broker fee share of risk", step: 0.05, min: 0.05, max: 0.5, unit: "",
        what: "Refuses trades whose broker commission would be too large compared with the amount at risk — common with very small positions.",
        example: ({ v, f, b, c }) => `Risking ${pct(b, f.maxRiskPerTradePercent, c)}, the fee may be at most ${pct(b, f.maxRiskPerTradePercent * v, c)} (${Math.round(v * 100)}%).`,
      },
    ],
  },
];

export function RiskSettings() {
  const [data, setData] = useState<RiskSettingsData | null>(null);
  const [form, setForm] = useState<RiskLimits | null>(null);
  const [error, setError] = useState<string | null>(null);
  const [saving, setSaving] = useState(false);
  const [saved, setSaved] = useState(false);

  const apply = (d: RiskSettingsData) => {
    setData(d);
    setForm(d.effective);
  };

  useEffect(() => {
    api.get<RiskSettingsData>("/api/settings/risk").then(apply).catch((e: Error) => setError(e.message));
  }, []);

  const save = async () => {
    if (!form) return;
    setSaving(true);
    setError(null);
    setSaved(false);
    try {
      apply(await api.put<RiskSettingsData>("/api/settings/risk", form));
      setSaved(true);
    } catch (e) {
      setError((e as Error).message);
    } finally {
      setSaving(false);
    }
  };

  const reset = async () => {
    if (!window.confirm("Reset all risk limits to the configured defaults?")) return;
    try {
      apply(await api.del("/api/settings/risk").then(() => api.get<RiskSettingsData>("/api/settings/risk")));
    } catch (e) {
      setError((e as Error).message);
    }
  };

  const dirty = !!data && !!form && GROUPS.flatMap((g) => g.fields).some((f) => form[f.key] !== data.effective[f.key]);

  return (
    <Card
      title="Limits"
      actions={data && <Badge tone={data.isCustomized ? "info" : "neutral"}>{data.isCustomized ? "custom" : "defaults"}</Badge>}
    >
      <p className="hint">
        Dollar amounts use the current balance{data ? ` (${money(data.balance, data.currency)})` : ""}. Changes apply within a few seconds;
        ranges are restricted so limits can be tuned but not switched off.
      </p>
      {form && data &&
        GROUPS.map((group) => (
          <section key={group.title} className="risk-group">
            <h3>{group.title}</h3>
            <p className="muted small">{group.intro}</p>
            <div className="risk-fields">
              {group.fields.map((f) => (
                <div key={f.key} className="risk-field">
                  <label className="risk-input">
                    <span className="strong">{f.label}</span>
                    <span className="input-unit">
                      <input
                        type="number"
                        step={f.step}
                        min={f.min}
                        max={f.max}
                        value={form[f.key]}
                        onChange={(e) => setForm({ ...form, [f.key]: Number(e.target.value) })}
                      />
                      {f.unit && <span className="muted">{f.unit}</span>}
                    </span>
                    {form[f.key] !== data.defaults[f.key] && <span className="muted small">default {data.defaults[f.key]}</span>}
                  </label>
                  <div className="risk-help">
                    <div>{f.what}</div>
                    <div className="risk-example">Example: {f.example({ v: form[f.key], f: form, b: data.balance, c: data.currency })}</div>
                  </div>
                </div>
              ))}
            </div>
          </section>
        ))}
      <div className="form-actions">
        <button className="primary" onClick={save} disabled={saving || !dirty}>{saving ? "Saving…" : "Save risk limits"}</button>
        {dirty && data && <button onClick={() => setForm(data.effective)}>Discard changes</button>}
        {data?.isCustomized && <button onClick={reset}>Reset to defaults</button>}
        {saved && <span className="pos small">Saved and applied.</span>}
      </div>
      <ErrorNote error={error} />
    </Card>
  );
}
