import { useEffect, useState } from "react";
import { api } from "../api";
import { time } from "../format";
import { Badge, Card, Empty, ErrorNote } from "./Ui";
import { BackupCard } from "./BackupCard";

interface EngineInfo {
  state: "Stopped" | "Starting" | "Running" | "Restarting";
  lastError: string | null;
  restarts: number;
}

interface LogEntry {
  timestampUtc: string;
  level: string;
  category: string;
  message: string;
}

const tone = (state: EngineInfo["state"]) => (state === "Running" ? "good" : state === "Stopped" ? "bad" : "warn");

/** Android app only: the engine running on this phone and its recent log (there is no terminal on a phone). */
export function AppEngineSettings() {
  const [engine, setEngine] = useState<EngineInfo | null>(null);
  const [log, setLog] = useState<LogEntry[]>([]);
  const [error, setError] = useState<string | null>(null);

  useEffect(() => {
    let cancelled = false;
    const load = () =>
      Promise.all([api.get<EngineInfo>("/api/app/engine"), api.get<LogEntry[]>("/api/app/logs?count=100")])
        .then(([e, l]) => {
          if (!cancelled) {
            setEngine(e);
            setLog(l.reverse());
            setError(null);
          }
        })
        .catch((e: Error) => !cancelled && setError(e.message));
    void load();
    const timer = setInterval(load, 5000);
    return () => {
      cancelled = true;
      clearInterval(timer);
    };
  }, []);

  return (
    <>
      <ErrorNote error={error} />
      <Card title="Trading engine on this phone">
        {engine && (
          <div className="chips">
            <Badge tone={tone(engine.state)}>{engine.state.toLowerCase()}</Badge>
            <span className="chip">{engine.restarts} restart{engine.restarts === 1 ? "" : "s"} since the app started</span>
            {engine.lastError && <span className="chip">last error: {engine.lastError}</span>}
          </div>
        )}
        <p className="muted">
          The engine runs in the background while the "HVTradingBot is trading" notification is shown, also with the
          screen off. It restarts on its own after errors and when you change the markets. Open positions always keep
          their broker-side stop loss and take profit.
        </p>
      </Card>
      <BackupCard />
      <Card title="Recent log">
        {log.length === 0 ? (
          <Empty>No log lines yet.</Empty>
        ) : (
          <div className="table-wrap">
            <table>
              <thead>
                <tr><th>Time</th><th>Level</th><th>Source</th><th>Message</th></tr>
              </thead>
              <tbody>
                {log.map((l, i) => (
                  <tr key={i}>
                    <td>{time(l.timestampUtc)}</td>
                    <td><Badge tone={l.level === "Error" || l.level === "Critical" ? "bad" : l.level === "Warning" ? "warn" : "neutral"}>{l.level}</Badge></td>
                    <td>{l.category}</td>
                    <td>{l.message}</td>
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
