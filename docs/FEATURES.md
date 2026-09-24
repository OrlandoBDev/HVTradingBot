# Feature Catalogue

## Foundation

- Modular monolith architecture
- API
- Worker processes
- EF Core
- PostgreSQL
- Redis
- RabbitMQ
- Docker
- Configuration validation
- Feature flags
- Health checks
- Audit logging

## Market Data

- Multiple providers
- Streaming or polling adapters
- OHLC candles
- Bid/ask
- Spread
- Tick volume where available
- Market-data normalization
- Timeframe aggregation
- Data freshness detection
- Historical import

## Analysis

- Technical indicators
- Market structure
- Support/resistance
- Volatility analysis
- Session analysis
- Multi-timeframe confirmation
- Market-regime classification
- Cross-market confirmation

## Strategies

- Trend Following
- Breakout/Retest
- Momentum
- Mean Reversion
- Volatility Expansion
- Carry / rate-differential research
- Session-based strategies
- VIX term-structure strategies
- Strategy voting
- Ensemble strategy engine
- Meta-strategy selection

## Trade Intelligence

- Trade score
- Confidence estimation
- Probability calibration
- Uncertainty score
- Abstention gate
- Candidate expiration
- Setup quality tiers
- Trade invalidation logic

## Risk

- Risk-per-trade limit
- Daily/weekly loss limits
- Drawdown controls
- Correlation controls
- Currency exposure controls
- Max open positions
- Max trades/day
- R:R enforcement
- Spread/slippage limits
- Consecutive-loss halt
- Session restrictions
- News blackout
- Stale-data halt
- Kill switch

## Trading Modes

- Shadow mode
- Paper mode
- Approval mode
- Guarded auto mode

## Broker Support

Initial:

- PaperTradingBroker
- OANDA

Later:

- Interactive Brokers

## Execution

- Market orders
- Limit orders
- Stop orders
- Stop loss
- Take profit
- Trailing stops
- Break-even rules
- Idempotency
- Order reconciliation
- Partial-fill handling
- Lost-response recovery
- Broker-state reconciliation

## VIX / Volatility

- VIX spot monitoring
- VIX9D
- VIX3M
- VX futures
- Front-month/second-month spread
- Contango
- Backwardation
- Term-structure slope
- Curve curvature
- SPX/VIX relationship
- Volatility regimes
- VIX futures strategy module
- VIX options strategy module

## Backtesting & Research

- Historical replay
- Same strategy code as live
- Spread/slippage/fees
- Walk-forward analysis
- Out-of-sample validation
- Monte Carlo simulation
- Parameter stability tests
- Look-ahead-bias checks
- Survivorship-bias safeguards where relevant
- Reproducible test configurations

## AI / ML

AI is advisory only.

Potential features:

- Market-context summary
- Independent proposal review
- Strategy performance analysis
- Anomaly detection
- Feature importance
- Model-assisted regime classification
- Probability calibration
- Strategy weighting recommendation

AI must not directly place trades.

## Dashboard

- Overview
- Markets
- Trade candidates
- Open positions
- Orders
- Trade history
- Strategies
- Backtests
- Performance
- Risk
- Broker status
- System health
- Audit logs
- Configuration

## Notifications

- Dashboard
- Email
- Push
- Telegram
- SMS

Events:

- Candidate generated
- Approval requested
- Trade executed
- Stop hit
- Target hit
- Kill switch
- Broker disconnected
- Market data stale
- Loss threshold reached
- System failure
