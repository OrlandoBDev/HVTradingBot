import { HubConnectionBuilder, LogLevel, type HubConnection } from "@microsoft/signalr";
import type { LiveBar } from "./types";

type BarListener = (bar: LiveBar) => void;
const barListeners = new Set<BarListener>();
const reconnectListeners = new Set<() => void>();

/**
 * Builds the dashboard's SignalR connection (useStatus owns and starts it). Live bars and reconnects are fanned out to
 * components through onBar / onReconnected, so the page needs only one connection.
 */
export function createDashboardConnection(): HubConnection {
  const connection = new HubConnectionBuilder()
    .withUrl("/hubs/dashboard")
    .withAutomaticReconnect()
    .configureLogging(LogLevel.Warning)
    .build();
  connection.on("bar", (bar: LiveBar) => barListeners.forEach((l) => l(bar)));
  connection.onreconnected(() => reconnectListeners.forEach((l) => l()));
  return connection;
}

/** Calls `listener` for every newly closed bar the API pushes. Returns the unsubscribe function. */
export function onBar(listener: BarListener) {
  barListeners.add(listener);
  return () => void barListeners.delete(listener);
}

/** Calls `listener` after the connection comes back, when bars may have been missed. Returns the unsubscribe function. */
export function onReconnected(listener: () => void) {
  reconnectListeners.add(listener);
  return () => void reconnectListeners.delete(listener);
}
