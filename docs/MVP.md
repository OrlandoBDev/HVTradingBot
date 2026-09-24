# MVP Definition

## Goal

Deliver a working Forex paper-trading platform that can continuously observe markets, generate trade candidates, reject weak or unsafe setups, simulate accepted trades, and measure results.

The MVP is successful when it can run unattended in PAPER mode and produce reproducible, auditable trading decisions.

## MVP Scope

### 1. Platform Foundation

- .NET 10 solution
- ASP.NET Core API
- Worker service
- PostgreSQL persistence
- EF Core migrations
- Redis integration
- Serilog
- OpenTelemetry
- Health checks
- Docker Compose
- Configuration validation

### 2. Market Data

Support:

- EUR/USD
- GBP/USD
- USD/JPY
- AUD/USD
- USD/CAD

Timeframes:

- 5m
- 15m
- 1H
- 4H
- Daily

Capture:

- OHLC
- Bid
- Ask
- Spread
- Timestamp
- Data-source status

### 3. Indicators

Implement:

- EMA 9
- EMA 20
- EMA 50
- EMA 200
- RSI
- MACD
- ATR
- ADX
- Bollinger Bands

### 4. Market Structure

Detect:

- Higher highs
- Higher lows
- Lower highs
- Lower lows
- Trend
- Range
- Breakout
- Retest
- Support
- Resistance
- Volatility expansion

### 5. Market Regimes

Initial regimes:

- TRENDING_BULLISH
- TRENDING_BEARISH
- RANGING
- HIGH_VOLATILITY
- LOW_VOLATILITY
- UNCERTAIN

### 6. Strategies

Implement:

- Trend Following
- Breakout + Retest
- Momentum
- Mean Reversion
- Volatility Expansion

Each strategy returns either a structured candidate or NO_TRADE.

### 7. Trade Scoring

Score candidates from 0-100.

Initial components:

- Trend
- Momentum
- Structure
- Higher-timeframe confirmation
- Volatility
- Risk/reward
- Spread penalty

Suggested policy:

- < 60 reject
- 60-74 observe
- 75-84 candidate
- 85+ high-quality candidate

Thresholds must remain configurable.

### 8. Risk Engine

Minimum controls:

- Max risk per trade
- Max daily loss
- Max open positions
- Minimum R:R
- Maximum spread
- Maximum correlated currency exposure
- Maximum consecutive losses
- Cooldown period
- Market-data freshness check
- Kill switch

### 9. Paper Trading

Support:

- Market orders
- Stop loss
- Take profit
- Spread
- Slippage
- Simulated fills
- P&L tracking
- Order rejection
- Position close

### 10. Trade Journal

Persist:

- Candidate
- Score
- Regime
- Indicators
- Strategy
- Risk evaluation
- Rejection reason
- Simulated order
- Outcome
- P&L
- MAE
- MFE

Store rejected candidates too.

### 11. Backtesting

Use the same strategy interfaces as the live/paper engine.

Minimum outputs:

- Total trades
- Win rate
- Profit factor
- Expectancy
- Maximum drawdown
- Average R
- Longest losing streak

### 12. API / Dashboard

MVP screens:

- System status
- Trading mode
- Market overview
- Trade candidates
- Open paper positions
- Trade history
- Risk status
- Strategy performance
- Kill switch

## Explicitly Out of MVP

Do not block MVP delivery on:

- Live trading
- Auto trading
- VIX
- Options
- Machine learning
- LLM-driven strategy generation
- Mobile app
- Social sentiment
- News NLP
- Portfolio optimization
- Reinforcement learning

## MVP Acceptance Criteria

The system must:

1. Start in PAPER mode.
2. Ingest valid market data.
3. Detect stale market data.
4. Calculate indicators deterministically.
5. Generate NO_TRADE when no setup is valid.
6. Generate structured candidates for valid setups.
7. Risk-check every candidate.
8. Reject invalid candidates with explicit reasons.
9. Simulate accepted trades.
10. Prevent duplicate execution.
11. Persist every decision.
12. Recover paper positions after restart.
13. Expose current system/risk status.
14. Pass automated risk and strategy tests.
15. Produce repeatable backtest results using the same strategy code.
