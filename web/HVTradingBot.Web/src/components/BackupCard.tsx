import { useEffect, useState } from "react";
import { api } from "../api";
import { time } from "../format";
import { Badge, Card, ErrorNote } from "./Ui";

interface BackupInfo {
  createdAtUtc: string;
  databaseBytes: number;
  closedTrades: number;
  decisions: number;
  signals: number;
}

interface BackupStatus {
  lastExportUtc: string | null;
  lastExport: BackupInfo | null;
  databaseBytes: number;
}

type Progress = { state: "working" | "exported" | "restored" | "failed" | "cancelled"; message: string };

const STALE_DAYS = 7;
const mb = (bytes: number) => `${(bytes / 1_048_576).toFixed(1)} MB`;

/**
 * Android app only: all data lives on this phone, so it can be saved to a file kept elsewhere (Google Drive,
 * Downloads) and restored from one. The app shows the file pickers; this card asks for them and shows the outcome.
 */
export function BackupCard() {
  const [status, setStatus] = useState<BackupStatus | null>(null);
  const [progress, setProgress] = useState<Progress | null>(null);
  const [error, setError] = useState<string | null>(null);

  const load = () => api.get<BackupStatus>("/api/app/backup").then(setStatus).catch((e: Error) => setError(e.message));

  useEffect(() => {
    void load();
    const onResult = (e: Event) => {
      const detail = (e as CustomEvent<Progress>).detail;
      setProgress(detail.state === "cancelled" ? null : detail);
      if (detail.state === "exported") void load();
    };
    window.addEventListener("hv:backup", onResult);
    return () => window.removeEventListener("hv:backup", onResult);
  }, []);

  // The app handles these pages itself (a file picker); the dashboard stays where it is.
  const ask = (action: "backup-export" | "backup-restore") => {
    setProgress(null);
    window.location.assign(`/app-action/${action}`);
  };

  const days = status?.lastExportUtc ? (Date.now() - Date.parse(status.lastExportUtc)) / 86_400_000 : null;
  const tone = days == null ? "bad" : days > STALE_DAYS ? "warn" : "good";

  return (
    <Card title="Backup" actions={status && <Badge tone={tone}>{days == null ? "never backed up" : days < 1 ? "backed up today" : `${Math.floor(days)} day(s) ago`}</Badge>}>
      <p className="hint">
        Trades, decisions, learning and settings live only on this phone. Save a backup to Google Drive or Downloads regularly; losing
        the phone or uninstalling the app would otherwise lose them. The backup does not contain your Deriv token or email password:
        after restoring on another phone, enter them again.
      </p>
      {status && (
        <p className="muted small">
          Data on this phone: {mb(status.databaseBytes)}.{" "}
          {status.lastExport
            ? `Last backup ${time(status.lastExport.createdAtUtc)}: ${status.lastExport.closedTrades} closed trades, ${status.lastExport.decisions} decisions.`
            : "No backup yet."}
        </p>
      )}
      <div className="form-actions">
        <button className="primary" onClick={() => ask("backup-export")} disabled={progress?.state === "working"}>Save backup…</button>
        <button onClick={() => ask("backup-restore")} disabled={progress?.state === "working"}>Restore from backup…</button>
      </div>
      {progress && (
        <p className={progress.state === "failed" ? "neg" : progress.state === "working" ? "muted" : "pos"}>{progress.message}</p>
      )}
      <p className="muted small">
        Restoring replaces this phone's data; the current data is saved inside the app first, so nothing is lost.
      </p>
      <ErrorNote error={error} />
    </Card>
  );
}
