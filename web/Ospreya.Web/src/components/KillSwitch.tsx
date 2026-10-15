import { useState } from "react";
import { api } from "../api";
import type { SystemStatus } from "../types";

export function KillSwitch({ status, onChanged }: { status: SystemStatus; onChanged: () => void }) {
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState<string | null>(null);

  const toggle = async () => {
    const activate = !status.killSwitchActive;
    const reason = window.prompt(
      activate
        ? "Activate the kill switch? All new orders will be blocked immediately. Reason (optional):"
        : "Deactivate the kill switch and allow new paper trades? A reason is required:",
      activate ? "Manual stop" : "",
    );
    if (reason === null) return;
    setBusy(true);
    setError(null);
    try {
      await api.post("/api/kill-switch", { active: activate, reason });
      onChanged();
    } catch (e) {
      setError((e as Error).message);
    } finally {
      setBusy(false);
    }
  };

  return (
    <div className="kill-switch">
      <button className={status.killSwitchActive ? "danger-on" : "danger"} onClick={toggle} disabled={busy} title={status.killSwitchReason ?? ""}>
        {status.killSwitchActive ? "Kill switch ON · resume" : "Kill switch"}
      </button>
      {error && <span className="error small">{error}</span>}
    </div>
  );
}
