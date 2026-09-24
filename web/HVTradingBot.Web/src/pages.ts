export type PageId = "overview" | "markets" | "trades" | "decisions" | "learning" | "performance" | "backtest" | "risk" | "audit" | "settings";

export interface PageInfo {
  id: PageId;
  label: string;
  icon: string;
  group: "Monitor" | "Trading" | "Analysis" | "Control";
  summary: string;
  details: string[];
}

/** Every page, with the one-line summary and the "What this page does" notes shown in its header. */
export const PAGES: PageInfo[] = [
  {
    id: "overview",
    label: "Overview",
    icon: "◉",
    group: "Monitor",
    summary: "Your account, system health and the markets at a glance.",
    details: [
      "Equity, balance and profit/loss for today, this week and since the start.",
      "Whether the trading worker is running and market data is fresh — trading pauses automatically if either fails.",
      "A compact view of the selected markets and any open positions.",
    ],
  },
  {
    id: "markets",
    label: "Markets",
    icon: "≋",
    group: "Monitor",
    summary: "Live prices, market regime and the latest decision for every selected market.",
    details: [
      "Prices update every few seconds from Deriv; analysis runs at every 5-minute close.",
      "Regime: ▲ trending bullish, ▼ trending bearish, ◆ ranging, ⚡ high volatility, ? uncertain — strategies only trade in regimes they suit.",
      "Choose which markets appear here under Settings › Markets.",
    ],
  },
  {
    id: "trades",
    label: "Trades",
    icon: "⇅",
    group: "Trading",
    summary: "Open positions and the history of closed trades.",
    details: [
      "Open: every position with its entry, broker-side stop loss and take profit, and live unrealized P&L.",
      "History: how each trade ended (stop, target or manual), its result in money and in R (multiples of the amount risked).",
      "MAE / MFE show how far a trade went against and in favour of you while it was open.",
    ],
  },
  {
    id: "decisions",
    label: "Decisions",
    icon: "✓",
    group: "Trading",
    summary: "The journal of every evaluation and why the engine did or did not trade.",
    details: [
      "Each market is evaluated at every 5-minute close. Most evaluations end in NO_TRADE — that is the intended default.",
      "Candidates show their score (0–100); only 75+ can trade, and every risk rule must pass.",
      "Click a row for the full audit record: indicators, every strategy's verdict, each risk check and the order result.",
    ],
  },
  {
    id: "learning",
    label: "Learning",
    icon: "✦",
    group: "Analysis",
    summary: "What the engine has learned about each strategy in each market condition.",
    details: [
      "Every setup — traded or not — is followed as a virtual trade until its stop, target or expiry.",
      "Good results raise future scores (up to +8), poor ones lower them (down to −15); persistent losers are switched off.",
      "Learning never changes position size or risk limits, and needs many samples before it has much effect.",
    ],
  },
  {
    id: "performance",
    label: "Performance",
    icon: "↗",
    group: "Analysis",
    summary: "Results of closed trades: win rate, profit factor, drawdown — overall, per strategy and per market.",
    details: [
      "Profit factor = gross profit ÷ gross loss; expectancy = average result per trade.",
      "Max drawdown is the largest fall from a previous equity peak.",
      "Small samples are unreliable — judge results over many trades.",
    ],
  },
  {
    id: "backtest",
    label: "Backtest",
    icon: "⟲",
    group: "Analysis",
    summary: "Replay history through the same engine to see how the rules would have performed.",
    details: [
      "Uses the exact strategy, scoring, risk and fill code as live trading, including spread, slippage and commission.",
      "Simulated data is reproducible by seed; stored data replays real Deriv candles collected by the worker.",
      "A good backtest is not proof of a future edge.",
    ],
  },
  {
    id: "risk",
    label: "Risk",
    icon: "◈",
    group: "Control",
    summary: "Whether new trades are currently allowed, and how close you are to each limit.",
    details: [
      "Risk rules have final authority over every trade and are checked twice: when proposed and just before execution.",
      "Loss limits, open-position limits, cooldowns and stale data block new trades automatically.",
      "Change the limits under Settings › Risk limits.",
    ],
  },
  {
    id: "audit",
    label: "Audit log",
    icon: "☰",
    group: "Control",
    summary: "A record of every important system and user action.",
    details: [
      "Startups, reconnections, kill-switch changes, settings changes, closed positions and execution problems.",
      "Secrets are never written here.",
    ],
  },
  {
    id: "settings",
    label: "Settings",
    icon: "⚙",
    group: "Control",
    summary: "Connect your Deriv account, choose markets, set risk limits and notifications.",
    details: [
      "Broker account: your Deriv App ID and token (stored encrypted) — only demo accounts are accepted.",
      "Markets: what the engine analyses and trades. Saving restarts the worker.",
      "Risk limits: applied within seconds, within safe ranges.",
      "Notifications: your SMTP (e.g. Gmail) settings and recipients; an email for every trade opened and closed.",
    ],
  },
];

export const GROUPS = ["Monitor", "Trading", "Analysis", "Control"] as const;

export function pageInfo(id: PageId): PageInfo {
  return PAGES.find((p) => p.id === id)!;
}
