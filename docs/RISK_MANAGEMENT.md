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

### Position Slots and High-Score Extras

- **Normal slots** (`MaxOpenPositions`, default 3) are shared by all markets. While Forex trades, Derived markets
  take at most `MaxDerivedOpenPositions` (1) of them so they cannot crowd Forex out; while Forex is closed
  (weekends, the daily break) Derived may use every slot. Derived trades already open when Forex reopens run to
  their stop or target; Forex takes slots as they free up.
- **High-score extras** (`MaxExtraDerivedPositions`, default 2, shared by every market type): a candidate scoring at
  least `HighScoreOverrideMinScore` (90) may open when the slots are full, on a market with no open position (a
  Derived market may also add to one it already trades; Forex never does). At most normal + extras (5) are open at
  once. An extra only opens if every open trade and the new one could hit their stops without going over today's
  remaining daily loss limit (Derived: the Derived daily limit too). Every other rule still applies.
- **Derived risk budget:** every Derived trade after the first must fit in today's remaining Derived loss budget
  if all open Derived trades and the new one hit their stops.

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

### Signal Trades

Signals are setups the engine sends to the user instead of trading them (markets set to "Signals", and optional near
misses scoring just below the automatic threshold). A signal is traded only when the user accepts it, and it never
touches the bot's own limits:

- Its own slots (default 2), risk per trade (default 0.5%) and daily loss budget (default 2%); trades are tagged by the
  `SIG-` client order id. The bot's slot and loss rules ignore signal trades; currency exposure counts every position.
- Accepting re-prices the entry at the live quote and refuses the signal once the price has covered a set share
  (default 1/3) of the way to its stop or target, or after it expires (default 10 minutes).
- The same rules run again at placement. A failed rule can be accepted by the user, except hard rules, which always
  block: trading mode, kill switch, stale data, market not tradable, sizing, already traded. Loss limits can be accepted
  only when "Allow trading past a loss limit" is on, with a typed `ACCEPT`.
- Accepted rules are recorded on the risk check ("Accepted by you: ..."), the decision journal and the audit log
  (`SignalTradedWithOverrides`); results of overridden trades are reported separately on the Signals page.

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
