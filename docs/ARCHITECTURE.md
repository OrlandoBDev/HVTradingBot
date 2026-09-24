# Architecture

## Architectural Style

Use a modular monolith for the initial product.

Do not create microservices unless independent scaling, deployment, fault isolation, or team ownership makes them necessary.

## Proposed Solution Structure

```
HVTradingBot.sln

src/
  HVTradingBot.Api/
  HVTradingBot.Worker/
  HVTradingBot.Domain/
  HVTradingBot.Application/
  HVTradingBot.Infrastructure/
  HVTradingBot.Contracts/

web/
  HVTradingBot.Web/

tests/
  HVTradingBot.UnitTests/
  HVTradingBot.IntegrationTests/
  HVTradingBot.ArchitectureTests/

docs/
```

## Domain Modules

```
MarketData
MarketAnalysis
Strategies
TradeScoring
RiskManagement
TradeExecution
BrokerIntegration
Portfolio
Backtesting
Performance
Notifications
Auditing
Configuration
```

## Core Flow

```
Market Data
   ↓
Feature / Indicator Engine
   ↓
Market Regime
   ↓
Strategy Evaluation
   ↓
Candidate Ensemble
   ↓
Trade Scoring
   ↓
Uncertainty / Abstention
   ↓
Risk Validation
   ↓
Approval Gate (when enabled)
   ↓
Final Risk Validation
   ↓
Execution Engine
   ↓
Broker
   ↓
Position Monitoring
   ↓
Journal / Performance
```

## Hard Boundary

Only the Execution module may call the broker order API.

AI, strategy, dashboard, and notification components must not directly execute trades.

## Broker Abstraction

```csharp
public interface IBroker
{
    Task<BrokerAccount> GetAccountAsync(CancellationToken cancellationToken);
    Task<IReadOnlyCollection<BrokerPosition>> GetPositionsAsync(CancellationToken cancellationToken);
    Task<IReadOnlyCollection<BrokerOrder>> GetOrdersAsync(CancellationToken cancellationToken);
    Task<IReadOnlyCollection<Candle>> GetCandlesAsync(
        string instrument,
        TimeFrame timeframe,
        CancellationToken cancellationToken);

    Task<OrderResult> PlaceOrderAsync(
        TradeOrder order,
        CancellationToken cancellationToken);

    Task<OrderResult> CancelOrderAsync(
        string orderId,
        CancellationToken cancellationToken);

    Task<OrderResult> ClosePositionAsync(
        string positionId,
        CancellationToken cancellationToken);
}
```

## Strategy Abstraction

```csharp
public interface ITradingStrategy
{
    string Name { get; }

    Task<StrategyResult> EvaluateAsync(
        MarketContext context,
        CancellationToken cancellationToken);
}
```

## Risk Abstraction

```csharp
public interface IRiskManager
{
    Task<RiskDecision> EvaluateAsync(
        TradeProposal proposal,
        PortfolioState portfolio,
        CancellationToken cancellationToken);
}
```

## Design Rules

- Domain layer has no broker SDK dependency.
- Broker-specific DTOs remain in Infrastructure.
- Use decimal for financial values where appropriate.
- UTC for persistence.
- All workers support graceful cancellation.
- Every execution operation uses an idempotency key.
- Broker state is authoritative in live modes.
- Database state alone must never be assumed to represent real open positions.
