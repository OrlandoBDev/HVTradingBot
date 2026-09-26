# Risk Management

## Principle

Risk controls are deterministic and have final authority.

No AI, strategy, admin dashboard widget, or notification action may bypass them.

## Initial Defaults

Defaults are placeholders and must be configurable.

- Max risk per trade: 0.5%
- Max daily loss: 2%
- Max weekly loss: 5%
- Max open positions: 3
- Min R:R: 1:2
- Max consecutive losses: 3

## Required Rules

### Per-Trade Risk

Calculate position size from:

- equity
- risk %
- entry
- stop distance
- pip/tick value
- contract size
- currency conversion when needed

### Portfolio Exposure

Track:

- gross exposure
- net exposure
- exposure by currency
- correlated positions
- same-direction concentration

### Daily / Weekly Loss

Block new trades once thresholds are breached.

### Spread

Reject a trade when spread exceeds the configured threshold or deviates excessively from recent normal conditions.

### Slippage

Estimate expected slippage and reject if execution quality becomes unacceptable.

### Minimum R:R

Reject setups below the configured minimum.

### Consecutive Losses

After N losses:

- pause strategies
- activate cooldown
- optionally require manual review

### Market Data Freshness

If data is stale:

- block new trades
- raise health alert
- optionally trigger kill switch

### News Events

No new trade on a market from 30 minutes before to 30 minutes after a high-impact release for its currencies; other
medium- or high-impact releases within two hours, and headlines clearly against the trade, shrink the position. News
can only tighten risk. See [NEWS_AND_TRENDS.md](NEWS_AND_TRENDS.md).

### Kill Switch

Triggers may include:

- manual activation
- daily loss breach
- repeated broker failures
- uncertain broker order state
- stale market data
- internal reconciliation mismatch
- invalid account state

The kill switch blocks all new orders.

## Final Risk Check

Risk must be checked twice:

1. when proposal is created
2. immediately before execution

This protects against price movement or account changes between proposal and execution.
