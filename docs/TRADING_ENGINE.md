# Trading Engine

## Design Objective

The trading engine is intentionally selective.

The desired behavior is not:

> Find a reason to trade.

It is:

> Reject everything that does not meet a sufficiently strong and validated edge.

## Decision States

Every evaluation ends as one of:

- NO_TRADE
- OBSERVE
- CANDIDATE
- REJECTED_BY_RISK
- APPROVAL_REQUIRED
- APPROVED
- EXECUTED
- EXPIRED

## Multi-Timeframe Model

Suggested hierarchy:

- Daily: broad regime
- 4H: structural direction
- 1H: primary setup
- 15m: confirmation
- 5m: execution refinement

A lower-timeframe signal must not automatically override a conflicting higher-timeframe state.

## Initial Indicators

- EMA 9 / 20 / 50 / 200
- RSI
- MACD
- ATR
- ADX
- Bollinger Bands
- Rate of Change
- Momentum
- Volatility

## Market Structure

Detect:

- HH
- HL
- LH
- LL
- trend
- range
- breakout
- retest
- false-breakout candidate
- support
- resistance
- volatility expansion
- volatility contraction

## Regimes

- TRENDING_BULLISH
- TRENDING_BEARISH
- RANGING
- LOW_VOLATILITY
- HIGH_VOLATILITY
- EXTREME_VOLATILITY
- NEWS_EVENT
- UNCERTAIN

## Strategy Set

### Trend Following

Requires trend strength and higher-timeframe alignment.

### Breakout + Retest

Requires established level, valid break, retest, and confirmation.

### Momentum

Requires directional acceleration with regime compatibility.

### Mean Reversion

Only enabled in appropriate ranging conditions.

### Volatility Expansion

Targets transitions from contraction to expansion.

## Ensemble Logic

Each strategy evaluates independently.

Do not average blindly.

The ensemble should consider:

- strategy validity
- regime compatibility
- historical strategy performance in that regime
- signal agreement
- correlation between strategies
- uncertainty

## Trade Score

Suggested components:

| Component | Max |
|---|---:|
| Trend | 20 |
| Momentum | 15 |
| Structure | 20 |
| Higher timeframe | 15 |
| Volatility | 10 |
| Regime fit | 10 |
| Risk/reward | 10 |

Penalties may apply for:

- spread
- conflicting signals
- deteriorating liquidity
- news risk
- uncertainty

Bounded adjustments are added on top: learned strategy performance, headline sentiment, the cross-market currency
trend and learned news/trend conditions. See [NEWS_AND_TRENDS.md](NEWS_AND_TRENDS.md).

## Abstention

The system must explicitly support abstention.

Even a high raw score can become NO_TRADE when:

- data is stale
- regime is uncertain
- major news is imminent
- spread is abnormal
- strategies disagree materially
- probability calibration is weak
- historical sample size is too small
- execution conditions are poor
