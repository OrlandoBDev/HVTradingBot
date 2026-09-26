# Backtesting and Validation

## Principle

A profitable backtest is not proof of a profitable live strategy.

Validation must actively search for false confidence.

## Requirements

The backtesting engine must reuse production strategy logic.

Do not maintain a separate set of simplified backtest strategies.

## Simulation

Account for:

- bid/ask spread
- slippage
- fees/commission
- stop-loss behavior
- take-profit behavior
- trade timing
- position sizing

## Validation Pipeline

```
Research
  ↓
Historical Backtest
  ↓
Validation Set
  ↓
Out-of-Sample Test
  ↓
Walk-Forward Test
  ↓
Parameter Stability
  ↓
Monte Carlo
  ↓
Shadow Mode
  ↓
Paper Trading
  ↓
Approval Mode
  ↓
Small-Capital Live
```

## Is It More Than Luck?

Every backtest result includes a robustness check (`RobustnessAnalysis`), shown on the Backtest page:

- **Walk forward, period by period.** The run is split into four consecutive periods. The engine learns only from trades
  already closed, so each period is traded with what was learned before it. A real edge shows up in most periods, not
  in one lucky stretch.
- **Monte Carlo.** The trades' R results are drawn at random with replacement 2,000 times (fixed seed, reproducible):
  the typical and bad-case (5th percentile) total, the drawdown to expect, the chance of ending with a loss, and the
  average trade's 95% lower bound.
- **Verdict.** Fewer than 30 trades: too few to judge. Average R at or below 0: no edge. Otherwise the edge "holds" only
  when the lower bound is above 0 and at least three of four periods made money; anything else is "not proven".

Simulated prices have no real patterns; only runs on stored Deriv candles say something about real markets.

## Metrics

- net return
- win rate
- loss rate
- profit factor
- expectancy
- average R
- maximum drawdown
- longest losing streak
- Sharpe ratio where appropriate
- Sortino ratio where appropriate
- MAE
- MFE

## Segment Analysis

Evaluate performance by:

- strategy
- instrument
- hour
- session
- weekday
- volatility
- regime
- long/short
- trade-score band
- spread band

## Anti-Overfitting Controls

- chronological splits
- no random shuffling of time series
- no future-data leakage
- look-ahead-bias tests
- parameter perturbation
- minimum sample sizes
- out-of-sample confirmation
- walk-forward confirmation

Any strategy with implausibly high accuracy must be treated as suspicious until leakage and overfitting have been ruled out.
