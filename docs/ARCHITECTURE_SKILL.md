# HVTradingBot .NET 10 Architecture Skill

## Role

When working in this repository, act as a senior .NET 10 architect and quantitative trading systems engineer.

## Required Stack

- .NET 10
- C# 14
- ASP.NET Core 10 Web API
- .NET 10 Worker Service
- EF Core 10
- PostgreSQL
- Redis
- RabbitMQ
- React + TypeScript
- SignalR
- Serilog
- OpenTelemetry
- Docker / Docker Compose
- xUnit

## Architecture

Use a modular monolith with Clean Architecture boundaries.

Projects:

```
HVTradingBot.Domain
HVTradingBot.Application
HVTradingBot.Infrastructure
HVTradingBot.Api
HVTradingBot.Worker
HVTradingBot.Contracts
```

Dependencies:

```
Domain <- Application <- Api
                    <- Worker
Application <- Infrastructure
Api/Worker compose Infrastructure at startup
```

Domain must not reference Infrastructure, EF Core, broker SDKs, HTTP clients, AI SDKs, Redis, RabbitMQ, or ASP.NET Core.

## Modules

- MarketData
- MarketAnalysis
- Strategies
- TradeScoring
- RiskManagement
- TradeExecution
- BrokerIntegration
- Portfolio
- Backtesting
- Performance
- Auditing
- Notifications
- Configuration

Keep module ownership explicit even while deployed as one application.

## Domain Rules

Prefer rich domain models over database-shaped objects.

Use:

- records/value objects for immutable concepts
- enums for bounded states
- decimal for prices, money, P&L, quantities and percentages where appropriate
- DateTimeOffset/UTC for timestamps
- Guid or strongly typed identifiers
- explicit result types for expected business rejection

Do not use exceptions for ordinary NO_TRADE or risk rejection outcomes.

## Trading Safety Boundary

No strategy, AI component, controller, worker or dashboard handler may call a live broker order endpoint directly.

Required flow:

```
Market Data
 -> Analysis
 -> Strategy
 -> Trade Proposal
 -> Scoring
 -> Risk Evaluation
 -> Approval Gate when required
 -> Final Risk Evaluation
 -> Execution Service
 -> IBroker
```

Only TradeExecution may invoke broker mutation operations.

PAPER is the default mode.

## Application Layer

Contains use cases/orchestration and interfaces required by the application.

Examples:

- IMarketDataProvider
- ITradingStrategy
- ITradeScoringService
- IRiskManager
- ITradeExecutionService
- IBroker
- ITradeJournal
- IClock

Application must not know OANDA/IBKR DTOs.

## Infrastructure

Contains implementations for:

- EF Core/PostgreSQL
- Redis
- RabbitMQ
- broker adapters
- HTTP clients
- telemetry exporters
- external notification providers

Broker DTOs are mapped into internal contracts at the boundary.

## API

Use minimal APIs or controllers consistently.

Requirements:

- ProblemDetails
- request validation
- authorization
- rate limiting
- health endpoints
- OpenAPI
- correlation/trace IDs
- no broker secrets returned by API

## Worker

Use BackgroundService with CancellationToken.

Workers must be idempotent and restart-safe.

Initial workers:

- MarketDataWorker
- MarketScannerWorker
- PositionMonitoringWorker
- PerformanceWorker

Do not implement tight polling loops. Configure cadence and use streaming APIs where appropriate.

## Persistence

EF Core 10 + PostgreSQL.

Initial aggregates/entities:

- Instrument
- MarketCandle
- MarketSnapshot
- StrategySignal
- TradeProposal
- RiskEvaluation
- PaperOrder
- Position
- Trade
- TradeDecision
- DailyPerformance
- AuditLog
- SystemEvent

Indexes must reflect time-series access patterns.

Do not store secrets.

## Configuration

Use strongly typed Options with ValidateOnStart.

Initial sections:

- TradingOptions
- RiskOptions
- MarketDataOptions
- PaperTradingOptions
- BrokerOptions

Default TradingOptions.Mode = PAPER.

## Resilience

External HTTP calls use HttpClientFactory and resilience policies.

Use:

- timeout
- bounded retries with jitter
- circuit breaker where appropriate

Never blindly retry order placement. Reconcile uncertain order state using client order/idempotency IDs.

## Observability

Every major trading lifecycle operation must be traceable.

Use:

- Serilog structured logs
- OpenTelemetry traces
- OpenTelemetry metrics
- health checks

Never log API keys or credentials.

Important dimensions:

- instrument
- timeframe
- strategy
- regime
- proposal ID
- order ID
- trading mode

## Testing

Use xUnit.

Test pyramid:

1. Domain/unit tests
2. Application tests
3. Infrastructure integration tests
4. Architecture tests
5. End-to-end paper-trading tests

Mandatory architecture tests:

- Domain cannot depend on Infrastructure
- AI cannot depend on broker execution implementation
- strategy cannot directly depend on broker implementation
- live broker execution cannot be invoked outside execution boundary

Mandatory risk tests:

- max trade risk
- daily loss
- open-position limit
- R:R
- spread
- stale market data
- kill switch
- duplicate order
- expired approval

## Agent Workflow

For each feature:

1. Read README, MVP, ARCHITECTURE, ARCHITECTURE_SKILL and relevant feature docs.
2. Create/confirm a focused feature branch.
3. Implement the smallest vertical slice.
4. Add tests.
5. Build.
6. Run tests.
7. Update docs.
8. Report assumptions and remaining work.

Do not invent broker behavior. Document assumptions and isolate them behind interfaces.

## Definition of Done

A change is done only when:

- solution builds
- tests pass
- architecture boundaries remain valid
- risk behavior is covered when relevant
- configuration is validated
- telemetry exists where operationally important
- docs reflect the implementation
