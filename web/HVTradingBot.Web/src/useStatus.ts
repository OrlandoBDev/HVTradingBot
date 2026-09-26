import { useEffect, useState } from "react";
import { HubConnectionState } from "@microsoft/signalr";
import { api } from "./api";
import { inApp } from "./platform";
import { createDashboardConnection, reloadCharts } from "./hub";
import type { SystemStatus } from "./types";

/** Changes whenever the learning model gains or resolves a setup (Android app only; the web refreshes per bar). */
export interface LearningPulse {
  resolved: number;
  open: number;
  lastResolvedUtc: string | null;
  version: string;
}

/**
 * Live system status: pushed over SignalR in the browser, long-polled from the engine in the Android app (which also
 * pushes learning changes). Starts with a REST fetch either way.
 */
export function useStatus() {
  const [status, setStatus] = useState<SystemStatus | null>(null);
  const [learning, setLearning] = useState<LearningPulse | null>(null);
  const [connected, setConnected] = useState(false);

  useEffect(() => {
    api.get<SystemStatus>("/api/status").then(setStatus).catch(() => undefined);
    return inApp ? pollAppEvents(setStatus, setLearning, setConnected) : connectSignalR(setStatus, setConnected);
  }, []);

  return { status, learning, connected, setStatus };
}

function connectSignalR(setStatus: (s: SystemStatus) => void, setConnected: (c: boolean) => void) {
  // The one dashboard connection; it also carries live bars for the price charts (see hub.ts).
  const connection = createDashboardConnection();
  connection.on("status", (s: SystemStatus) => setStatus(s));
  connection.onreconnecting(() => setConnected(false));
  connection.onreconnected(() => setConnected(true));
  connection.onclose(() => setConnected(false));
  connection
    .start()
    .then(() => setConnected(connection.state === HubConnectionState.Connected))
    .catch(() => setConnected(false));

  return () => {
    void connection.stop();
  };
}

interface AppEvents {
  sequence: number;
  events: { name: string; data: unknown }[];
}

/** Each request returns what is newer than `after` at once, or waits (up to 20 s) for the next update. */
function pollAppEvents(
  setStatus: (s: SystemStatus) => void,
  setLearning: (l: LearningPulse) => void,
  setConnected: (c: boolean) => void,
) {
  let stopped = false;
  let after = 0;
  let lastBar: string | null | undefined;
  void (async () => {
    while (!stopped) {
      try {
        const batch = await api.get<AppEvents>(`/api/app/events?after=${after}`);
        after = batch.sequence;
        for (const e of batch.events) {
          if (e.name === "status") {
            const status = e.data as SystemStatus;
            setStatus(status);
            // A new bar was processed: charts reload their candles (the browser gets bars over SignalR instead).
            if (lastBar !== undefined && status.marketData.lastBarTimeUtc !== lastBar) reloadCharts();
            lastBar = status.marketData.lastBarTimeUtc;
          }
          if (e.name === "learning") setLearning(e.data as LearningPulse);
        }
        setConnected(true);
      } catch {
        setConnected(false);
        await new Promise((resolve) => setTimeout(resolve, 2000));
      }
    }
  })();
  return () => {
    stopped = true;
  };
}
