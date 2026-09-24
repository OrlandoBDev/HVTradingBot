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

### Deriv (implemented, demo only)

See ADR-008. Implementation: `src/HVTradingBot.Infrastructure/Brokers/Deriv`.

- Authentication: Personal Access Token + App ID (`Authorization: Bearer`, `Deriv-App-ID`) against
  `https://api.derivws.com`; `GET /trading/v1/options/accounts`, then `POST .../accounts/{id}/otp` for a single-use
  WebSocket URL (`wss://api.derivws.com/trading/v1/options/ws/demo`).
- Market data: public WebSocket (`.../ws/public`, no auth): `active_symbols`, `contracts_for`, `ticks_history`
  (5-minute candles), `ticks` (bid/ask).
- Orders: `proposal` without limits (commission quote) → stop/target amounts → `proposal` with `limit_order` →
  broker stop level checked against the strategy stop → `buy`.
- Reconciliation: `portfolio` + `proposal_open_contract` for closed contracts; contracts opened outside the app count
  toward exposure limits.
- Credentials are entered on the dashboard Settings page (encrypted in PostgreSQL); environment variables are a fallback.

### OANDA

Former initial candidate; superseded by Deriv (ADR-008). Keep any OANDA-specific models in Infrastructure.

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
