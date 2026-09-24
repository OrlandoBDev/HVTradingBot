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
