/**
 * True when the dashboard runs inside the Android app's WebView (the app marks its user agent). There the engine
 * runs on the phone: API requests are answered in-process, live updates come from a long-poll instead of SignalR,
 * and there is no dashboard login.
 */
export const inApp = /\bHVTradingBotApp\//.test(navigator.userAgent);

/**
 * Android's request interception cannot read request bodies, so the app receives them base64-encoded in this query
 * parameter (the URL is always visible to the app).
 */
export const APP_BODY_PARAMETER = "_body";

/** The request URL for the app: the JSON body moves into the query string. */
export function withAppBody(path: string, body: string): string {
  return `${path}${path.includes("?") ? "&" : "?"}${APP_BODY_PARAMETER}=${encodeURIComponent(toBase64(body))}`;
}

/** UTF-8 safe base64 for request bodies sent to the app. */
export function toBase64(text: string): string {
  const bytes = new TextEncoder().encode(text);
  let binary = "";
  bytes.forEach((b) => (binary += String.fromCharCode(b)));
  return btoa(binary);
}
