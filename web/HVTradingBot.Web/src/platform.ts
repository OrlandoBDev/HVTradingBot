/**
 * True when the dashboard runs inside the Android app's WebView (the app marks its user agent). There the engine
 * runs on the phone: API requests are answered in-process, live updates come from a long-poll instead of SignalR,
 * and there is no dashboard login.
 */
export const inApp = /\bHVTradingBotApp\//.test(navigator.userAgent);

/** Android's request interception cannot read request bodies, so the app receives them base64-encoded in this header. */
export const APP_BODY_HEADER = "X-HV-Body";

/** UTF-8 safe base64 for request bodies sent to the app. */
export function toBase64(text: string): string {
  const bytes = new TextEncoder().encode(text);
  let binary = "";
  bytes.forEach((b) => (binary += String.fromCharCode(b)));
  return btoa(binary);
}
