import { useEffect, useState } from "react";
import { HubConnectionBuilder, HubConnectionState, LogLevel } from "@microsoft/signalr";
import { api } from "./api";
import type { SystemStatus } from "./types";

/** Live system status pushed over SignalR, with an initial REST fetch. */
export function useStatus() {
  const [status, setStatus] = useState<SystemStatus | null>(null);
  const [connected, setConnected] = useState(false);

  useEffect(() => {
    api.get<SystemStatus>("/api/status").then(setStatus).catch(() => undefined);

    const connection = new HubConnectionBuilder()
      .withUrl("/hubs/dashboard")
      .withAutomaticReconnect()
      .configureLogging(LogLevel.Warning)
      .build();
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
