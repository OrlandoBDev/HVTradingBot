import { useState, type FormEvent } from "react";
import { api } from "../api";
import { Card, ErrorNote } from "./Ui";

/** Change the dashboard password. Other browsers signed in with the old password are signed out. */
export function SecuritySettings() {
  const [currentPassword, setCurrentPassword] = useState("");
  const [newPassword, setNewPassword] = useState("");
  const [confirm, setConfirm] = useState("");
  const [error, setError] = useState<string | null>(null);
  const [saved, setSaved] = useState(false);
  const [busy, setBusy] = useState(false);

  const submit = async (e: FormEvent) => {
    e.preventDefault();
    setSaved(false);
    if (newPassword !== confirm) {
      setError("The two new passwords do not match.");
      return;
    }
    setBusy(true);
    setError(null);
    try {
      await api.post("/api/auth/password", { currentPassword, newPassword });
      setSaved(true);
      setCurrentPassword("");
      setNewPassword("");
      setConfirm("");
    } catch (err) {
      setError((err as Error).message);
    } finally {
      setBusy(false);
    }
  };

  return (
    <Card title="Change password">
      <form className="settings-form" onSubmit={submit}>
        <label>
          Current password
          <input type="password" value={currentPassword} onChange={(e) => setCurrentPassword(e.target.value)} autoComplete="current-password" required />
        </label>
        <label>
          New password
          <input type="password" value={newPassword} onChange={(e) => setNewPassword(e.target.value)} autoComplete="new-password" minLength={10} required />
          <span className="muted small">At least 10 characters. Other devices will be signed out.</span>
        </label>
        <label>
          Confirm new password
          <input type="password" value={confirm} onChange={(e) => setConfirm(e.target.value)} autoComplete="new-password" required />
        </label>
        <ErrorNote error={error} />
        {saved && <p className="ok-text small">Password changed. Other signed-in devices were signed out.</p>}
        <div>
          <button className="primary" type="submit" disabled={busy}>{busy ? "Saving…" : "Change password"}</button>
        </div>
      </form>
      <p className="muted small">
        Forgot the password? On the server run <code>./run.sh reset-login</code>, then create a new login with the setup code it prints.
      </p>
    </Card>
  );
}
