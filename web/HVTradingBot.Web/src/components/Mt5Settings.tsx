import { useEffect, useState } from "react";
import { api } from "../api";
import { money, time } from "../format";
import { Badge, Card, ErrorNote } from "./Ui";

interface Mt5Settings {
  enabled: boolean;
  tokenConfigured: boolean;
  tokenHint: string | null;
  accountId: string | null;
  region: string;
  symbolSuffix: string;
  commissionPercent: number;
  version: number;
  updatedAtUtc: string | null;
  updatedBy: string | null;
  active: boolean;
  status: {
    state: "Connected" | "Failed";
    message: string | null;
    isCurrent: boolean;
    login: string | null;
    server: string | null;
    broker: string | null;
    isDemo: boolean | null;
    balance: number | null;
    currency: string | null;
    checkedAtUtc: string;
  } | null;
  regions: string[];
}

/**
 * Settings › Broker account › MetaTrader 5: when switched on, Forex trades on this MT5 demo account through MetaApi
 * instead of Deriv multipliers (Deriv's commission is several times a normal Forex spread). Prices still come from Deriv.
 */
export function Mt5SettingsCard() {
  const [data, setData] = useState<Mt5Settings | null>(null);
  const [enabled, setEnabled] = useState(false);
  const [token, setToken] = useState("");
  const [accountId, setAccountId] = useState("");
  const [region, setRegion] = useState("new-york");
  const [suffix, setSuffix] = useState("");
  const [commission, setCommission] = useState(0.007);
  const [error, setError] = useState<string | null>(null);
  const [busy, setBusy] = useState(false);
  const [saved, setSaved] = useState(false);

  const apply = (s: Mt5Settings, fields: boolean) => {
    setData(s);
    if (fields) {
      setEnabled(s.enabled);
      setAccountId(s.accountId ?? "");
      setRegion(s.region);
      setSuffix(s.symbolSuffix);
      setCommission(s.commissionPercent);
    }
  };

  const load = (fields = false) => api.get<Mt5Settings>("/api/settings/mt5").then((s) => apply(s, fields)).catch((e: Error) => setError(e.message));

  useEffect(() => {
    void load(true);
  }, []);

  // After saving, the worker restarts and checks the account within a few seconds: poll for its result.
  const waiting = !!data && data.enabled && (!data.status || !data.status.isCurrent);
  useEffect(() => {
    if (!waiting) return;
    const id = window.setInterval(() => void load(), 3000);
    return () => window.clearInterval(id);
  }, [waiting]);

  const save = async () => {
    if (enabled && !data?.enabled && !window.confirm("Trade Forex on MT5 instead of Deriv?\n\nThe trading engine restarts to switch. Open Deriv positions keep their stop loss and take profit.")) return;
    setBusy(true);
    setError(null);
    setSaved(false);
    try {
      apply(await api.put<Mt5Settings>("/api/settings/mt5", { enabled, token: token || null, accountId, region, symbolSuffix: suffix, commissionPercent: commission }), true);
      setToken("");
      setSaved(true);
    } catch (e) {
      setError((e as Error).message);
    } finally {
      setBusy(false);
    }
  };

  const remove = async () => {
    if (!window.confirm("Remove the MetaApi token and switch MT5 off? Forex goes back to Deriv multipliers.")) return;
    try {
      apply(await api.del("/api/settings/mt5").then(() => api.get<Mt5Settings>("/api/settings/mt5")), true);
      setToken("");
    } catch (e) {
      setError((e as Error).message);
    }
  };

  const s = data?.status;
  const statusBadge = !data?.enabled ? <Badge tone="neutral">off</Badge>
    : !s || !s.isCurrent ? <Badge tone="warn">checking…</Badge>
    : s.state === "Connected" ? <Badge tone="good">connected</Badge>
    : <Badge tone="bad">failed</Badge>;

  return (
    <Card title="MetaTrader 5 (Forex)" actions={statusBadge}>
      <p className="hint">
        Trade Forex on an MT5 <b>demo</b> account instead of Deriv multipliers, whose commission is several times a normal Forex spread. The app
        talks to MT5 through <a href="https://metaapi.cloud" target="_blank" rel="noreferrer">MetaApi</a>: add your MT5 demo login there, then
        paste its account id and an API token here. Prices still come from Deriv; other markets are not traded while MT5 is on.
      </p>
      {data && (
        <>
          <label className="event-option">
            <input type="checkbox" checked={enabled} onChange={(e) => setEnabled(e.target.checked)} />
            <span><span className="strong">Trade Forex on MT5</span><span className="muted small"> — saving restarts the trading engine to switch.</span></span>
          </label>
          <div className="settings-form">
            <label>
              MetaApi token
              <input type="password" value={token} onChange={(e) => setToken(e.target.value)} autoComplete="new-password" spellCheck={false}
                placeholder={data.tokenConfigured ? `stored (ends in ${data.tokenHint}) — leave empty to keep` : "paste your MetaApi API token"} />
            </label>
            <label>
              MetaApi account id
              <input value={accountId} onChange={(e) => setAccountId(e.target.value)} placeholder="from the MetaApi web app" spellCheck={false} />
            </label>
            <label>
              Region
              <select value={region} onChange={(e) => setRegion(e.target.value)}>
                {data.regions.map((r) => <option key={r} value={r}>{r}</option>)}
              </select>
            </label>
            <label>
              Symbol suffix
              <input value={suffix} onChange={(e) => setSuffix(e.target.value)} placeholder="usually empty; e.g. .r if symbols are EURUSD.r" spellCheck={false} />
            </label>
            <label>
              Commission (round trip, % of position)
              <input type="number" min={0} max={0.1} step={0.001} value={commission} onChange={(e) => setCommission(Number(e.target.value))} />
            </label>
          </div>
          <p className="muted small">
            Commission is used to size positions: 0.007% is $7 per standard lot, typical of raw-spread accounts; use 0 for accounts priced only
            through the spread.
          </p>
          <div className="form-actions">
            <button className="primary" onClick={save} disabled={busy}>{busy ? "Saving…" : "Save"}</button>
            {data.tokenConfigured && <button className="danger" onClick={remove}>Remove token</button>}
            {saved && <span className="pos small">Saved.{enabled ? " The engine restarts and connects within a few seconds." : ""}</span>}
          </div>
          {data.enabled && (
            <dl className="kv" style={{ marginTop: 12 }}>
              <dt>Status</dt>
              <dd>{s && s.isCurrent ? (s.state === "Connected" ? "Connected" : s.message ?? "Failed") : "Waiting for the trading engine to check the account…"}</dd>
              <dt>Account</dt>
              <dd>{s?.login ? `${s.login} · ${s.server ?? ""}${s.isDemo ? " (demo)" : ""}` : "—"}</dd>
              <dt>Balance</dt>
              <dd>{s?.balance != null ? money(s.balance, s.currency ?? "USD") : "—"}</dd>
              <dt>In use</dt>
              <dd>{data.active ? <Badge tone="good">Forex trades on MT5</Badge> : <span className="muted">not yet (Deriv)</span>}</dd>
              <dt>Checked</dt>
              <dd>{s ? time(s.checkedAtUtc) : "—"}</dd>
            </dl>
          )}
          <p className="hint small" style={{ marginTop: 12 }}>
            The token is encrypted, never shown again, and only used by the trading engine. Real-money MT5 accounts are refused.
            {data.updatedAtUtc && ` Last changed ${time(data.updatedAtUtc)} by ${data.updatedBy}.`}
          </p>
        </>
      )}
      <ErrorNote error={error} />
    </Card>
  );
}
