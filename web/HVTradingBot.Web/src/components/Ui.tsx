import type { ReactNode } from "react";

export function Card({ title, children, actions }: { title?: ReactNode; children: ReactNode; actions?: ReactNode }) {
  return (
    <section className="card">
      {(title || actions) && (
        <header className="card-head">
          {title && (typeof title === "string" ? <h2>{title}</h2> : title)}
          {actions}
        </header>
      )}
      {children}
    </section>
  );
}

export function Stat({ label, value, tone, sub }: { label: string; value: ReactNode; tone?: string; sub?: string }) {
  return (
    <div className="stat">
      <div className="stat-label">{label}</div>
      <div className={`stat-value ${tone ?? ""}`}>{value}</div>
      {sub && <div className="stat-sub">{sub}</div>}
    </div>
  );
}

/** Small segmented control for switching views inside a page. */
export function Segmented<T extends string>({ value, options, onChange }: { value: T; options: { value: T; label: string }[]; onChange: (v: T) => void }) {
  return (
    <div className="segmented">
      {options.map((o) => (
        <button key={o.value} className={o.value === value ? "active" : ""} onClick={() => onChange(o.value)}>{o.label}</button>
      ))}
    </div>
  );
}

export function Badge({ children, tone = "neutral" }: { children: ReactNode; tone?: "good" | "bad" | "warn" | "neutral" | "info" }) {
  return <span className={`badge badge-${tone}`}>{children}</span>;
}

export function ErrorNote({ error }: { error: string | null }) {
  return error ? <p className="error">Could not load: {error}</p> : null;
}

export function Empty({ children }: { children: ReactNode }) {
  return <p className="empty">{children}</p>;
}

export const stateTone = (state: string | null): "good" | "bad" | "warn" | "neutral" | "info" => {
  switch (state) {
    case "Executed":
      return "good";
    case "RejectedByRisk":
      return "bad";
    case "Candidate":
    case "Observe":
      return "warn";
    case "NoTrade":
      return "neutral";
    default:
      return "info";
  }
};

/** Market regime badge: bullish green ▲, bearish red ▼, ranging blue, volatility amber, uncertain grey. */
export function RegimeBadge({ regime }: { regime: string | null }) {
  if (!regime) return <span className="muted" title="Evaluated at the next 5-minute close">pending</span>;
  const [tone, label]: ["good" | "bad" | "warn" | "neutral" | "info", string] =
    regime === "TrendingBullish" ? ["good", "▲ Trending bullish"]
    : regime === "TrendingBearish" ? ["bad", "▼ Trending bearish"]
    : regime === "Ranging" ? ["info", "◆ Ranging"]
    : regime === "LowVolatility" ? ["info", "◇ Low volatility"]
    : regime === "HighVolatility" ? ["warn", "⚡ High volatility"]
    : regime === "ExtremeVolatility" ? ["warn", "⚠ Extreme volatility"]
    : regime === "NewsEvent" ? ["warn", "News event"]
    : ["neutral", "? Uncertain"];
  return <Badge tone={tone}>{label}</Badge>;
}
