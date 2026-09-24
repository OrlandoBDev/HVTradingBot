import { useEffect, useState } from "react";
import type { DerivSettings } from "../types";
import { api } from "../api";
import { time } from "../format";
import { Badge, Card, ErrorNote } from "./Ui";
import { MarketSettings } from "./MarketSettings";

const connectionTone = (s: DerivSettings["connection"]) =>
  !s || !s.isCurrent ? "warn" : s.state === "Connected" ? "good" : s.state === "Failed" ? "bad" : "neutral";

const connectionLabel = (s: DerivSettings["connection"]) =>
  !s ? "not checked yet" : !s.isCurrent ? "checking new settings…" : s.state === "Connected" ? "connected" : s.state === "Failed" ? "failed" : "not configured";

export function Settings() {
  const [settings, setSettings] = useState<DerivSettings | null>(null);
  const [loadError, setLoadError] = useState<string | null>(null);
  const [appId, setAppId] = useState("");
  const [token, setToken] = useState("");
  const [accountId, setAccountId] = useState("");
  const [saving, setSaving] = useState(false);
  const [saveError, setSaveError] = useState<string | null>(null);
  const [saved, setSaved] = useState(false);

  const load = async (initial = false) => {
    try {
      const s = await api.get<DerivSettings>("/api/settings/deriv");
      setSettings(s);
      setLoadError(null);
      if (initial) {
        setAppId(s.appId ?? "");
        setAccountId(s.accountId ?? "");
      }
    } catch (e) {
      setLoadError((e as Error).message);
    }
  };

  useEffect(() => {
    void load(true);
  }, []);

  // The worker checks new settings within a few seconds; poll until its result for this version arrives.
  useEffect(() => {
    if (!settings || (settings.connection?.isCurrent && settings.connection.state !== "NotConfigured")) return;
    const id = window.setInterval(() => void load(), 3000);
    return () => window.clearInterval(id);
  }, [settings]);

  const save = async () => {
    setSaving(true);
    setSaveError(null);
    setSaved(false);
    try {
      const s = await api.put<DerivSettings>("/api/settings/deriv", { appId, apiToken: token || null, accountId: accountId || null });
      setSettings(s);
      setToken("");
      setSaved(true);
    } catch (e) {
      setSaveError((e as Error).message);
    } finally {
      setSaving(false);
    }
  };

  const remove = async () => {
    if (!window.confirm("Remove the stored Deriv App ID and token? The worker will stop sending orders to Deriv.")) return;
    await api.del("/api/settings/deriv").catch((e: Error) => setSaveError(e.message));
    setAppId("");
    setToken("");
    setAccountId("");
    await load();
  };

  const accounts = settings?.connection?.accounts ?? [];
  const demoAccounts = accounts.filter((a) => a.accountType.toLowerCase() === "demo");

  return (
    <>
      <ErrorNote error={loadError} />
      {settings && settings.brokerProvider !== "Deriv" && (
        <div className="banner info-banner">
          The worker is configured with <b>{settings.brokerProvider}</b> as broker (BROKER_PROVIDER in .env). Settings saved here are used when the broker is Deriv.
        </div>
      )}

      <Card title="Deriv account">
        <p className="hint">
          Orders are placed as Deriv multiplier contracts with broker-side stop loss and take profit. Only <b>demo</b> accounts can be used in this
          release. Create an App ID and a Personal Access Token at{" "}
          <a href="https://developers.deriv.com" target="_blank" rel="noreferrer">developers.deriv.com</a>.
        </p>
        <div className="settings-form">
          <label>
            App ID
            <input value={appId} onChange={(e) => setAppId(e.target.value)} placeholder="e.g. 12345" autoComplete="off" spellCheck={false} />
          </label>
          <label>
            Personal Access Token
            <input
              type="password"
              value={token}
              onChange={(e) => setToken(e.target.value)}
              placeholder={settings?.tokenConfigured ? `stored (ends in ${settings.tokenHint}) — leave empty to keep` : "paste your token"}
              autoComplete="new-password"
              spellCheck={false}
            />
          </label>
          <label>
            Account
            {demoAccounts.length > 0 ? (
              <select value={accountId} onChange={(e) => setAccountId(e.target.value)}>
                <option value="">First demo account</option>
                {demoAccounts.map((a) => (
                  <option key={a.accountId} value={a.accountId}>{a.accountId} · {a.currency}</option>
                ))}
              </select>
            ) : (
              <input value={accountId} onChange={(e) => setAccountId(e.target.value)} placeholder="optional, e.g. DOT90004580" spellCheck={false} />
            )}
          </label>
          <label>
            Account type
            <input value={`${settings?.accountType ?? "Demo"} (real accounts disabled)`} disabled />
          </label>
        </div>
        <div className="form-actions">
          <button className="primary" onClick={save} disabled={saving || appId.trim() === "" || (!settings?.tokenConfigured && token.trim() === "")}>
            {saving ? "Saving…" : "Save and connect"}
          </button>
          {settings?.tokenConfigured && settings.source === "database" && (
            <button className="danger" onClick={remove}>Remove credentials</button>
          )}
          {saved && <span className="pos small">Saved. The worker will connect within a few seconds.</span>}
        </div>
        <ErrorNote error={saveError} />
        <p className="hint small">
          Stored in the PostgreSQL database (Docker). The token is encrypted with keys kept outside the database, is never sent back to this page,
          and only the trading worker uses it.
          {settings?.source === "environment" && " Currently using credentials from environment variables (.env); saving here overrides them."}
          {settings?.updatedAtUtc && ` Last changed ${time(settings.updatedAtUtc)} by ${settings.updatedBy}.`}
        </p>
      </Card>

      <MarketSettings />

      <Card title="Connection">
        <dl className="kv">
          <dt>Status</dt>
          <dd><Badge tone={connectionTone(settings?.connection ?? null)}>{connectionLabel(settings?.connection ?? null)}</Badge></dd>
          <dt>Details</dt>
          <dd>{settings?.connection?.message ?? "The worker reports here after it tries the saved settings."}</dd>
          <dt>Active account</dt>
          <dd>{settings?.connection?.connectedAccountId ?? "—"}</dd>
          <dt>Checked</dt>
          <dd>{settings?.connection ? time(settings.connection.checkedAtUtc) : "—"}</dd>
        </dl>
        {accounts.length > 0 && (
          <div className="table-wrap" style={{ marginTop: 12 }}>
            <table>
              <thead><tr><th>Account</th><th>Type</th><th>Currency</th><th>Usable</th></tr></thead>
              <tbody>
                {accounts.map((a) => (
                  <tr key={a.accountId}>
                    <td className="mono">{a.accountId}</td>
                    <td>{a.accountType}</td>
                    <td>{a.currency}</td>
                    <td>{a.accountType.toLowerCase() === "demo" ? <Badge tone="good">yes</Badge> : <Badge tone="neutral">no (real)</Badge>}</td>
                  </tr>
                ))}
              </tbody>
            </table>
          </div>
        )}
      </Card>
    </>
  );
}
