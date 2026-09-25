export interface Account {
  currency: string;
  balance: number;
  startingBalance: number;
  equity: number;
  unrealizedPnl: number;
}

export interface Broker {
  name: string;
  accountId: string | null;
  isDemo: boolean;
}

export interface SystemStatus {
  mode: string;
  broker: Broker;
  killSwitchActive: boolean;
  killSwitchReason: string | null;
  killSwitchChangedUtc: string | null;
  workerHeartbeatUtc: string | null;
  workerHealthy: boolean;
  marketData: { lastBarTimeUtc: string | null; lastDataReceivedUtc: string | null; isStale: boolean };
  account: Account;
  openPositions: number;
  dailyRealizedPnl: number;
  weeklyRealizedPnl: number;
  consecutiveLosses: number;
  cooldownUntilUtc: string | null;
  serverTimeUtc: string;
}

export interface IndicatorSnapshot {
  close: number;
  ema20: number | null;
  ema50: number | null;
  ema200: number | null;
  rsi: number | null;
  adx: number | null;
  atr: number | null;
  atrPercentRank: number | null;
  macdHistogram?: number | null;
}

export interface Market {
  instrument: string;
  displayName: string;
  assetClass: string;
  isTradable: boolean;
  priceDecimals: number;
  isOpen: boolean;
  isPaused: boolean;
  isLoading: boolean;
  marketTimeUtc: string;
  bid: number;
  ask: number;
  spreadPips: number;
  regime: string | null;
  lastDecision: string | null;
  lastDecisionTimeUtc: string | null;
  indicators: IndicatorSnapshot | null;
}

export interface Decision {
  id: string;
  marketTimeUtc: string;
  instrument: string;
  state: string;
  regime: string;
  strategy: string | null;
  direction: string | null;
  score: number | null;
  entry: number | null;
  stopLoss: number | null;
  takeProfit: number | null;
  rewardToRisk: number | null;
  reasons: string;
  clientOrderId: string | null;
  correlationId: string;
}

export interface Position {
  id: string;
  clientOrderId: string;
  instrument: string;
  direction: string;
  units: number;
  entryPrice: number;
  stopLoss: number;
  takeProfit: number;
  initialRiskAmount: number;
  openedAtUtc: string;
  strategy: string;
  score: number;
  currentPrice: number | null;
  unrealizedPnl: number | null;
  closedAtUtc: string | null;
  exitPrice: number | null;
  exitReason: string | null;
  realizedPnl: number | null;
  rMultiple: number | null;
  maePips: number | null;
  mfePips: number | null;
  closeStatus: "Pending" | "Closing" | "Failed" | null;
  closeMessage: string | null;
  /** What the broker charged for the trade; already included in the P&L. */
  commission: number | null;
}

export interface RiskStatus {
  newTradesAllowed: boolean;
  blockingReasons: string[];
  limits: { name: string; current: string; limit: string; breached: boolean }[];
  currencyExposure: Record<string, number>;
}

export interface Metrics {
  totalTrades: number;
  wins: number;
  losses: number;
  winRate: number;
  netPnl: number;
  grossProfit: number;
  grossLoss: number;
  profitFactor: number | null;
  expectancy: number;
  averageR: number;
  maxDrawdown: number;
  maxDrawdownPercent: number;
  longestLosingStreak: number;
  averageMaePips: number;
  averageMfePips: number;
}

export interface Performance {
  overall: Metrics;
  byStrategy: Record<string, Metrics>;
  byInstrument: Record<string, Metrics>;
  decisionCounts: Record<string, number>;
}

export interface AuditEntry {
  id: number;
  timestampUtc: string;
  actor: string;
  action: string;
  details: string;
  correlationId: string | null;
}

export interface BacktestRun {
  id: string;
  createdAtUtc: string;
  source: string;
  totalTrades: number;
  netPnl: number;
  parameters: { source: string; days: number; seed?: number; endDate?: string; instruments: string[] };
  summary: {
    fromUtc: string;
    toUtc: string;
    barsProcessed: number;
    startingBalance: number;
    endingBalance: number;
    metrics: Metrics;
    byStrategy: Record<string, Metrics>;
    decisionCounts: Record<string, number>;
  };
}

export interface DerivAccount {
  accountId: string;
  accountType: string;
  currency: string;
}

export interface DerivSettings {
  brokerProvider: string;
  accountType: string;
  appId: string | null;
  tokenConfigured: boolean;
  tokenHint: string | null;
  accountId: string | null;
  source: "database" | "environment";
  updatedAtUtc: string | null;
  updatedBy: string | null;
  connection: {
    state: "NotConfigured" | "Connected" | "Failed";
    message: string | null;
    isCurrent: boolean;
    connectedAccountId: string | null;
    accounts: DerivAccount[];
    checkedAtUtc: string;
  } | null;
}

export interface MarketCatalogItem {
  symbol: string;
  brokerSymbol: string;
  name: string;
  market: string;
  submarket: string;
  assetClass: "Forex" | "Commodity" | "Crypto" | "SyntheticIndex" | "StockIndex";
  isTradable: boolean;
  isOpen: boolean;
  multipliers: number[];
}

export interface MarketSettings {
  selected: string[];
  derivedOnlyWhenForexClosed: boolean;
  isDefaultSelection: boolean;
  version: number;
  appliedVersion: number;
  updatedAtUtc: string | null;
  updatedBy: string | null;
  maxSelected: number;
  catalog: MarketCatalogItem[];
}

export interface AuthStatus {
  authenticated: boolean;
  username: string | null;
  setupRequired: boolean;
}

/** A stored bar from GET /api/candles (oldest first). */
export interface CandleBar {
  openTimeUtc: string;
  open: number;
  high: number;
  low: number;
  close: number;
  spread: number;
  volume: number;
}

export interface CandleSeries {
  instrument: string;
  timeFrame: string;
  candles: CandleBar[];
}

/** A newly closed candle pushed over the dashboard hub's "bar" message. */
export interface LiveBar {
  instrument: string;
  timeFrame: string;
  openTimeUtc: string;
  open: number;
  high: number;
  low: number;
  close: number;
  volume: number;
}
