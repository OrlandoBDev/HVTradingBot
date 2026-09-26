import { useEffect, useState } from "react";
import { HubConnectionState } from "@microsoft/signalr";
import { api } from "./api";
import { createDashboardConnection } from "./hub";
import type { SystemStatus } from "./types";

/** Live system status pushed over SignalR, with an initial REST fetch. */
export function useStatus() {
  const [status, setStatus] = useState<SystemStatus | null>(null);
  const [connected, setConnected] = useState(false);

  useEffect(() => {
    api.get<SystemStatus>("/api/status").then(setStatus).catch(() => undefined);

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
  }, []);

  return { status, connected, setStatus };
}
