import { useEffect, useState } from "react";
import { api } from "../api";
import { time } from "../format";
import { Badge, Card, ErrorNote } from "./Ui";

interface NotificationSettingsData {
  enabled: boolean;
  smtpHost: string;
  smtpPort: number;
  username: string | null;
  passwordConfigured: boolean;
  passwordHint: string | null;
  fromAddress: string | null;
  fromName: string;
  toAddresses: string[];
  onTradeOpened: boolean;
  onTradeClosed: boolean;
  onOrderRejected: boolean;
  onKillSwitch: boolean;
  source: "database" | "environment" | "none";
  updatedAtUtc: string | null;
  updatedBy: string | null;
  lastAttemptUtc: string | null;
  lastAttemptSucceeded: boolean | null;
  lastError: string | null;
}

interface Form {
  enabled: boolean;
  smtpHost: string;
  smtpPort: number;
  username: string;
  password: string;
  fromAddress: string;
  fromName: string;
  recipients: string;
  onTradeOpened: boolean;
  onTradeClosed: boolean;
  onOrderRejected: boolean;
  onKillSwitch: boolean;
}

const EVENTS: { key: keyof Form; label: string; hint: string }[] = [
  { key: "onTradeOpened", label: "Trade opened", hint: "every order that is filled" },
  { key: "onTradeClosed", label: "Trade closed", hint: "stop loss, take profit or manual close, with the result" },
  { key: "onKillSwitch", label: "Kill switch on / off", hint: "trading stopped or resumed" },
  { key: "onOrderRejected", label: "Order rejected", hint: "a candidate the risk engine or broker refused (can be frequent)" },
];

const toForm = (d: NotificationSettingsData): Form => ({
  enabled: d.enabled,
  smtpHost: d.smtpHost,
  smtpPort: d.smtpPort,
  username: d.username ?? "",
  password: "",
  fromAddress: d.fromAddress ?? "",
  fromName: d.fromName,
  recipients: d.toAddresses.join("\n"),
  onTradeOpened: d.onTradeOpened,
  onTradeClosed: d.onTradeClosed,
  onOrderRejected: d.onOrderRejected,
  onKillSwitch: d.onKillSwitch,
});

export function NotificationSettings() {
  const [data, setData] = useState<NotificationSettingsData | null>(null);
  const [form, setForm] = useState<Form | null>(null);
  const [error, setError] = useState<string | null>(null);
  const [notice, setNotice] = useState<{ ok: boolean; text: string } | null>(null);
  const [busy, setBusy] = useState<"save" | "test" | null>(null);

  const apply = (d: NotificationSettingsData) => {
    setData(d);
    setForm(toForm(d));
  };

  useEffect(() => {
    api.get<NotificationSettingsData>("/api/settings/notifications").then(apply).catch((e: Error) => setError(e.message));
  }, []);

  const set = <K extends keyof Form>(key: K, value: Form[K]) => form && setForm({ ...form, [key]: value });

  const save = async () => {
    if (!form) return;
    setBusy("save");
    setError(null);
    setNotice(null);
    try {
      const saved = await api.put<NotificationSettingsData>("/api/settings/notifications", {
        ...form,
        password: form.password || null,
        toAddresses: form.recipients.split(/[\n,;]/).map((a) => a.trim()).filter(Boolean),
      });
      apply(saved);
      setNotice({ ok: true, text: saved.enabled ? "Saved. The worker applies it within a few seconds." : "Saved. Email notifications are off." });
    } catch (e) {
      setError((e as Error).message);
    } finally {
      setBusy(null);
    }
  };

  const test = async () => {
    setBusy("test");
    setNotice(null);
    try {
      const result = await api.post<{ sent: boolean; message: string }>("/api/settings/notifications/test", {});
      setNotice({ ok: result.sent, text: result.message });
      setData(await api.get<NotificationSettingsData>("/api/settings/notifications"));
    } catch (e) {
      setNotice({ ok: false, text: (e as Error).message });
    } finally {
      setBusy(null);
    }
  };

  if (!form || !data) return <ErrorNote error={error} />;

  return (
    <div className="stack">
      <Card
        title="Email"
        actions={
          <label className="switch">
            <input type="checkbox" checked={form.enabled} onChange={(e) => set("enabled", e.target.checked)} />
            <span>{form.enabled ? "On" : "Off"}</span>
          </label>
        }
      >
        <p className="hint">
          For Gmail keep <code>smtp.gmail.com</code> / <code>587</code>, use your Gmail address as username, and create an{" "}
          <a href="https://myaccount.google.com/apppasswords" target="_blank" rel="noreferrer">App Password</a> (requires 2-Step Verification) — never your
          normal password.
        </p>
        <div className="settings-form">
          <label>
            SMTP server
            <input value={form.smtpHost} onChange={(e) => set("smtpHost", e.target.value)} spellCheck={false} />
          </label>
          <label>
            Port
            <input type="number" value={form.smtpPort} onChange={(e) => set("smtpPort", Number(e.target.value))} />
            <span className="muted small">587 = STARTTLS, 465 = TLS</span>
          </label>
          <label>
            Username
            <input value={form.username} onChange={(e) => set("username", e.target.value)} placeholder="you@gmail.com" autoComplete="off" spellCheck={false} />
          </label>
          <label>
            App password
            <input
              type="password"
              value={form.password}
              onChange={(e) => set("password", e.target.value)}
              placeholder={data.passwordConfigured ? `stored (ends in ${data.passwordHint}) — leave empty to keep` : "16-character app password"}
              autoComplete="new-password"
            />
          </label>
          <label>
            Sender address
            <input value={form.fromAddress} onChange={(e) => set("fromAddress", e.target.value)} placeholder="defaults to the username" spellCheck={false} />
          </label>
          <label>
            Sender name
            <input value={form.fromName} onChange={(e) => set("fromName", e.target.value)} />
          </label>
        </div>
        <label className="field-block">
          Recipients <span className="muted small">(one per line, up to 10)</span>
          <textarea rows={3} value={form.recipients} onChange={(e) => set("recipients", e.target.value)} placeholder="you@gmail.com" spellCheck={false} />
        </label>
      </Card>

      <Card title="Send an email when…">
        <div className="event-list">
          {EVENTS.map((ev) => (
            <label key={ev.key} className="event-option">
              <input type="checkbox" checked={form[ev.key] as boolean} onChange={(e) => set(ev.key, e.target.checked as never)} />
              <span>
                <span className="strong">{ev.label}</span>
                <span className="muted small"> — {ev.hint}</span>
              </span>
            </label>
          ))}
        </div>
        <p className="hint small" style={{ marginTop: 10 }}>
          Each trade and event is emailed once. Emails are sent in the background, so a mail problem never delays or stops trading.
        </p>
      </Card>

      <div className="form-actions">
        <button className="primary" onClick={save} disabled={busy !== null}>{busy === "save" ? "Saving…" : "Save"}</button>
        <button onClick={test} disabled={busy !== null || !data.passwordConfigured}>{busy === "test" ? "Sending…" : "Send test email"}</button>
        {notice && <span className={`small ${notice.ok ? "pos" : "neg"}`}>{notice.text}</span>}
      </div>
      <ErrorNote error={error} />

      <p className="muted small">
        {data.lastAttemptUtc ? (
          <>
            Last email {time(data.lastAttemptUtc)}:{" "}
            {data.lastAttemptSucceeded ? <Badge tone="good">delivered</Badge> : <Badge tone="bad">failed</Badge>}
            {data.lastError && <> {data.lastError}</>}
          </>
        ) : (
          "No email sent yet."
        )}
        {data.source === "environment" && " · Currently using settings from .env; saving here replaces them."}
        {data.updatedAtUtc && ` · Settings changed ${time(data.updatedAtUtc)} by ${data.updatedBy}.`}
      </p>
    </div>
  );
}
