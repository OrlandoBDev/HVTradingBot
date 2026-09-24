# Broker Integration

## Broker Strategy

Use a broker abstraction so trading logic is broker-independent.

## Initial Brokers

### PaperTradingBroker

Required first.

Responsibilities:

- simulate fills
- spread
- slippage
- stop loss
- take profit
- order state
- P&L
- rejection scenarios

### OANDA

Initial Forex broker adapter candidate.

Keep all OANDA-specific SDK/HTTP models in Infrastructure.

## Later Broker

### Interactive Brokers

Planned for broader multi-asset support including:

- Forex
- futures
- options
- VIX-related instruments
- SPX-related products

## Required Broker Behaviors

Implement:

- authentication
- account retrieval
- market data
- orders
- positions
- cancellations
- close-position
- broker-health status

## Idempotency

Every execution must use a unique client-generated idempotency/order reference.

A retry must never create an unintended duplicate position.

## Unknown Execution State

If broker request times out after submission:

Do not assume success.
Do not assume failure.

Instead:

1. query broker by client order ID
2. reconcile order state
3. block conflicting retry
4. raise an execution uncertainty event

## Startup Reconciliation

At startup:

1. connect to broker
2. retrieve account
3. retrieve open positions
4. retrieve pending orders
5. compare against internal state
6. repair/reconcile state
7. restore monitoring
8. only then allow new trades
