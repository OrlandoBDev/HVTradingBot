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

type Field = { key: keyof RiskLimits; label: string; step: number; min: number; max: number; unit: string; hint?: (v: number, balance: number, c: string) => string };

const FIELDS: Field[] = [
  { key: "maxRiskPerTradePercent", label: "Risk per trade", step: 0.1, min: 0.1, max: 3, unit: "%", hint: (v, b, c) => `${money((b * v) / 100, c)} per trade` },
  { key: "maxDailyLossPercent", label: "Daily loss limit", step: 0.5, min: 0.5, max: 10, unit: "%", hint: (v, b, c) => `stops trading after ${money((b * v) / 100, c)} lost in a day` },
  { key: "maxWeeklyLossPercent", label: "Weekly loss limit", step: 0.5, min: 1, max: 25, unit: "%", hint: (v, b, c) => `${money((b * v) / 100, c)} per week` },
  { key: "maxOpenPositions", label: "Max open positions", step: 1, min: 1, max: 10, unit: "" },
  { key: "minRewardToRisk", label: "Min reward : risk", step: 0.1, min: 1, max: 5, unit: ": 1" },
  { key: "maxConsecutiveLosses", label: "Losses before cooldown", step: 1, min: 1, max: 10, unit: "" },
  { key: "cooldownMinutes", label: "Cooldown", step: 15, min: 0, max: 1440, unit: "min" },
  { key: "maxCurrencyExposure", label: "Max same-currency exposure", step: 1, min: 1, max: 5, unit: "positions" },
  { key: "maxCommissionShareOfRisk", label: "Max broker fee share of risk", step: 0.05, min: 0.05, max: 0.5, unit: "", hint: (v) => `${Math.round(v * 100)}% — trades whose fee exceeds this are refused` },
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

  const dirty = !!data && !!form && FIELDS.some((f) => form[f.key] !== data.effective[f.key]);

  return (
    <Card
      title="Limits"
      actions={data && <Badge tone={data.isCustomized ? "info" : "neutral"}>{data.isCustomized ? "custom" : "defaults"}</Badge>}
    >
      <p className="hint">
        Dollar amounts use the current balance{data ? ` (${money(data.balance, data.currency)})` : ""}. Changes apply within a few seconds;
        ranges are restricted so limits can be tuned but not switched off.
      </p>
      {form && data && (
        <div className="settings-form">
          {FIELDS.map((f) => (
            <label key={f.key}>
              {f.label}
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
              <span className="muted small">
                {f.hint ? f.hint(form[f.key], data.balance, data.currency) : ""}
                {form[f.key] !== data.defaults[f.key] ? ` · default ${data.defaults[f.key]}` : ""}
              </span>
            </label>
          ))}
        </div>
      )}
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
