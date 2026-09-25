# Product Roadmap

## Backlog

Ideas agreed for later, not yet scheduled into a phase.

### MetaTrader 5 as a second broker (Forex through Deriv MT5)

**Why:** Deriv multiplier contracts charge a commission of about 0.05% of the position value. At small-account
risk (≈1% per trade) that is 31–36% of the amount at risk on Forex, above the 30% fee cap, so every Forex
candidate is currently rejected. Deriv MT5 accounts are typically priced through the spread instead of a
commission, which may make Forex viable.

**First step (decides whether to build it):** verify Deriv MT5's actual Forex costs (spread and any commission)
on the demo account for the traded pairs and compare with the multiplier commission.

**Integration options** (MetaTrader has no official public trading API):

| Option | Notes |
|---|---|
| MetaApi (metaapi.cloud) | Cloud-hosted MT5 terminal with REST/WebSocket API; works from macOS/Linux hosting; paid beyond its trial/free allowance. Preferred. |
| MQL5 Expert Advisor bridge | EA in a running MT5 terminal talks to the app over a local socket; free but needs a Windows host (or Wine) running 24/7. |
| MetaTrader5 Python package | Windows only; also needs the terminal running. |

**Design:** add MT5 as a second `IExecutionBroker` next to Deriv; route Forex to MT5 and keep Derived markets on
Deriv multipliers. Risk engine, learning, dashboard and emails stay unchanged. Keep the demo-only guard
(refuse real accounts), idempotent client order ids and kill-switch-on-unknown-state behaviour.

## Phase 0 — Foundation

- Documentation
- Repository structure
- Solution skeleton
- CI
- Docker
- Database
- Logging
- Health checks

## Phase 1 — Forex Market Intelligence

- Market-data adapters
- Candle persistence
- Indicators
- Market structure
- Multi-timeframe analysis
- Regime detection

## Phase 2 — Strategy Engine

- Trend Following
- Breakout + Retest
- Momentum
- Mean Reversion
- Volatility Expansion
- Trade scoring
- Abstention

## Phase 3 — Risk Engine

- Position sizing
- Exposure
- Loss limits
- Correlation controls
- Spread/slippage controls
- Kill switch
- Risk test suite

## Phase 4 — Paper Trading MVP

- Paper broker
- execution
- position monitoring
- journaling
- dashboard
- SignalR

## Phase 5 — Backtesting

- historical replay
- out-of-sample
- walk-forward
- Monte Carlo
- performance analytics

## Phase 6 — Shadow Mode

- observe live markets
- record predictions
- no simulated or real orders
- probability calibration

## Phase 7 — OANDA Approval Mode

- live account read
- trade proposals
- human approval
- final risk check
- broker execution

## Phase 8 — Guarded Auto Trading

- strict eligibility
- small-capital deployment
- enhanced reconciliation
- operational alerts

## Phase 9 — Ensemble / Adaptive Intelligence

- strategy voting
- meta-strategy
- regime performance weighting
- probability calibration
- uncertainty modeling
- AI advisory analysis

## Phase 10 — Interactive Brokers

- multi-asset broker adapter
- futures/options support

## Phase 11 — VIX Intelligence

- VIX spot
- VX futures
- term structure
- contango/backwardation
- SPX relationship
- volatility-regime strategies

## Phase 12 — Advanced Research

- portfolio optimization
- feature selection
- ML models
- anomaly detection
- automated research pipelines

Production strategies remain gated by deterministic validation and risk controls.
