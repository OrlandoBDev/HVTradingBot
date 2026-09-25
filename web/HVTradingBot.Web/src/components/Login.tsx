import { useState, type FormEvent } from "react";
import { api } from "../api";
import type { AuthStatus } from "../types";
import { ErrorNote } from "./Ui";

/** Sign-in page, or first-time creation of the owner login (needs the setup code from the API log). */
export function Login({ setupRequired, onSignedIn }: { setupRequired: boolean; onSignedIn: (status: AuthStatus) => void }) {
  const [username, setUsername] = useState("");
  const [password, setPassword] = useState("");
  const [confirm, setConfirm] = useState("");
  const [setupCode, setSetupCode] = useState("");
  const [rememberMe, setRememberMe] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const [busy, setBusy] = useState(false);

  const submit = async (e: FormEvent) => {
    e.preventDefault();
    if (setupRequired && password !== confirm) {
      setError("The two passwords do not match.");
      return;
    }
    setBusy(true);
    setError(null);
    try {
      const status = setupRequired
        ? await api.post<AuthStatus>("/api/auth/setup", { setupCode, username, password })
        : await api.post<AuthStatus>("/api/auth/login", { username, password, rememberMe });
      onSignedIn(status);
    } catch (err) {
      setError((err as Error).message);
    } finally {
      setBusy(false);
    }
  };

  return (
    <div className="login-page">
      <form className="login-card" onSubmit={submit}>
        <div className="brand">
          <span className="logo">HV</span>
          <span className="brand-name">HVTradingBot</span>
        </div>
        <h1>{setupRequired ? "Create your login" : "Sign in"}</h1>
        {setupRequired && (
          <p className="muted small">
            First start: choose the username and password for this dashboard. The setup code is printed in the API log
            (<code>.run/api.log</code>, or <code>./run.sh setup-code</code>), so only whoever runs the server can do this.
          </p>
        )}
        {setupRequired && (
          <label>
            Setup code
            <input value={setupCode} onChange={(e) => setSetupCode(e.target.value)} placeholder="ABCD-1234" autoComplete="one-time-code" required />
          </label>
        )}
        <label>
          Username
          <input value={username} onChange={(e) => setUsername(e.target.value)} autoComplete="username" autoFocus={!setupRequired} required />
        </label>
        <label>
          Password
          <input type="password" value={password} onChange={(e) => setPassword(e.target.value)}
            autoComplete={setupRequired ? "new-password" : "current-password"} minLength={setupRequired ? 10 : undefined} required />
        </label>
        {setupRequired && (
          <label>
            Confirm password
            <input type="password" value={confirm} onChange={(e) => setConfirm(e.target.value)} autoComplete="new-password" required />
            <span className="muted small">At least 10 characters.</span>
          </label>
        )}
        {!setupRequired && (
          <label className="check">
            <input type="checkbox" checked={rememberMe} onChange={(e) => setRememberMe(e.target.checked)} />
            Keep me signed in for 30 days
          </label>
        )}
        <ErrorNote error={error} />
        <button className="primary" type="submit" disabled={busy}>
          {busy ? "Please wait…" : setupRequired ? "Create login" : "Sign in"}
        </button>
      </form>
    </div>
  );
}
