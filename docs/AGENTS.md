# Agent Development Guide

This file is intended for Codex and other coding agents working in this repository.

## Mission

Build HVTradingBot as a production-quality trading research and automation platform.

Do not optimize for fastest code generation.

Optimize for:

- correctness
- auditability
- safety
- reproducibility
- testability
- modularity

## Required Rules

1. Never enable live trading by default.
2. Never allow an AI component to call broker order methods.
3. Never bypass the risk engine.
4. Never commit secrets.
5. Never use double for monetary calculations where decimal is appropriate.
6. Store timestamps in UTC.
7. Use CancellationToken.
8. Do not swallow exceptions.
9. Use structured logs.
10. Create tests with every critical trading rule.
11. Reuse strategy code between backtest and runtime.
12. Treat NO_TRADE as a valid output.
13. Store rejected candidates.
14. Ensure order submission is idempotent.
15. Broker state is authoritative for live positions.

## Build Order

Agents should work in this sequence:

1. Foundation
2. Domain model
3. Market data
4. Indicators
5. Market analysis
6. Strategies
7. Trade scoring
8. Risk
9. Paper broker
10. Execution
11. Journaling
12. Backtesting
13. API/dashboard
14. Broker integrations
15. AI advisory

## Branching

Use one feature branch per meaningful change.

Examples:

- feature/project-foundation
- feature/market-data
- feature/risk-engine
- feature/paper-broker
- feature/backtesting

Avoid mixing unrelated work in one PR.

## Definition of Done

A feature is not complete until:

- code compiles
- automated tests pass
- docs are updated
- failure scenarios are handled
- observability exists where needed
- no secrets are introduced
- public contracts are documented
