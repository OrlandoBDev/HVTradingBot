export const money = (value: number | null | undefined, currency = "USD") =>
  value == null ? "—" : value.toLocaleString(undefined, { style: "currency", currency, maximumFractionDigits: 2 });

export const num = (value: number | null | undefined, digits = 2) =>
  value == null ? "—" : value.toLocaleString(undefined, { minimumFractionDigits: digits, maximumFractionDigits: digits });

/** Prices: 3 decimals for JPY pairs, 5 for other currency pairs, otherwise up to 5 significant decimals. */
export const price = (value: number | null | undefined, instrument?: string, decimals?: number) => {
  if (value == null) return "—";
  if (decimals != null) return value.toFixed(decimals);
  if (instrument?.includes("/")) return value.toFixed(instrument.includes("JPY") ? 3 : 5);
  return value.toLocaleString(undefined, { maximumFractionDigits: 5 });
};

const pad = (n: number) => String(n).padStart(2, "0");

/** A timestamp in the phone's / computer's local time, e.g. "2026-09-26 14:05". The API sends UTC. */
export const time = (iso: string | null | undefined) => {
  if (!iso) return "—";
  const d = new Date(iso);
  return `${d.getFullYear()}-${pad(d.getMonth() + 1)}-${pad(d.getDate())} ${pad(d.getHours())}:${pad(d.getMinutes())}`;
};

/** The local time zone, e.g. "America/New_York"; the API uses it for Today / This week / This month. */
export const localTimeZone = () => Intl.DateTimeFormat().resolvedOptions().timeZone || "UTC";

export const ago = (iso: string | null | undefined, now: string) => {
  if (!iso) return "never";
  const seconds = Math.max(0, Math.round((Date.parse(now) - Date.parse(iso)) / 1000));
  return seconds < 90 ? `${seconds}s ago` : `${Math.round(seconds / 60)}m ago`;
};

export const signClass = (value: number | null | undefined) => (value == null || value === 0 ? "" : value > 0 ? "pos" : "neg");
