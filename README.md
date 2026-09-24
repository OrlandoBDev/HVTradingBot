# HVTradingBot

HVTradingBot is a research-driven, risk-first automated trading platform designed to analyze financial markets, identify high-quality trade opportunities, and support paper, approval-based, and eventually guarded automated execution.

## Core Principle

The system does **not** assume trades are certain.

The default decision is **NO_TRADE** unless multiple independent signals, market-regime checks, risk rules, and execution-quality checks agree.

AI/LLM components are advisory only and must never bypass deterministic risk controls.

## Initial Scope

The MVP focuses on:

- Forex market data and analysis
- Multi-timeframe technical analysis
- Strategy evaluation
- Market-regime detection
- Trade scoring
- Deterministic risk management
- Paper trading
- Trade journaling
- Backtesting
- Dashboard and observability

Later phases add:

- Broker-connected approval mode
- Guarded auto trading
- Interactive Brokers support
- VIX futures/options analysis
- Ensemble/meta-strategy selection
- Probability calibration
- Cross-market intelligence
- Strategy learning and optimization

## Technology

- .NET 10
- ASP.NET Core Web API
- .NET Worker Services
- EF Core
- PostgreSQL
- Redis
- RabbitMQ
- React + TypeScript
- SignalR
- Serilog
- OpenTelemetry
- Docker / Docker Compose

## Trading Modes

1. PAPER — default; all trades simulated
2. APPROVAL — proposals require human approval
3. AUTO — guarded automated execution; must be explicitly enabled

## Project Documentation

See the `docs/` folder:

- [Architecture](docs/ARCHITECTURE.md)
- [MVP](docs/MVP.md)
- [Features](docs/FEATURES.md)
- [Trading Engine](docs/TRADING_ENGINE.md)
- [Risk Management](docs/RISK_MANAGEMENT.md)
- [Broker Integration](docs/BROKER_INTEGRATION.md)
- [Backtesting](docs/BACKTESTING.md)
- [Security](docs/SECURITY.md)
- [Roadmap](docs/ROADMAP.md)
- [Agent Build Guide](docs/AGENTS.md)
- [ADRs](docs/ADR.md)

## Non-Negotiable Safety Rules

- PAPER mode is always the default.
- No AI component can place an order directly.
- Every trade must pass deterministic risk validation.
- Broker credentials must never be committed.
- Backtests must include spread, fees, and slippage assumptions.
- Live broker state is authoritative for open positions.
- Duplicate execution must be prevented with idempotency.
- A stale market feed blocks new trading.
- Kill switch blocks new trading immediately.
- Rejected opportunities are stored for later analysis.

## Status

Project foundation / pre-MVP.

The first milestone is a complete Forex paper-trading loop with market scanning, signal generation, risk validation, simulated execution, journaling, and performance analysis.
