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

### MetaTrader 5 through MetaApi (implemented, demo only, Forex)

Deriv multipliers charge about 0.05% of the position per trade, several times a normal Forex spread, so many Forex
setups fail the fee check. With MT5 switched on, **Forex orders go to an MT5 demo account instead**; prices still come
from Deriv, and non-Forex markets are not traded while MT5 is on.

**Set up (about 10 minutes, no code or config files):**

1. Open an MT5 **demo** account (e.g. Deriv MT5, or any broker's MT5 demo) and note its login, password and server.
2. Create a free account at [metaapi.cloud](https://metaapi.cloud), add the MT5 account there (login, password,
   server), and deploy it. Note its **account id** and **region** (e.g. `new-york`, `london`).
3. In MetaApi, create an **API access token**.
4. In the app: Settings › Broker account › MetaTrader 5 (Forex): paste the token and account id, pick the region, set
   the symbol suffix if your broker uses one (`EURUSD.r` → `.r`; usually empty) and the commission (0.007% ≈ $7 per
   lot; 0 for spread-only accounts), tick **Trade Forex on MT5** and save.
5. The trading engine restarts and the card shows **connected** with the account and balance.

**How it trades** (`Mt5Broker`, `MetaApiClient`): market orders with broker-side stop loss and take profit; the risk
engine's units become lots rounded **down** to the lot step (trades below the minimum lot are refused, never
enlarged). Every order is recorded before sending and carries a short client id derived from the signal, so it is
never sent twice and a lost answer is reconciled by finding the position with that id; until then the outcome is
Unknown and the kill switch stays on. Closes are read from the deal history (profit net of commission and swap; stop
or target from the deal reason). Real-money accounts are refused (MetaApi's trade mode, or the server name).
Position sizing uses the MT5 commission while MT5 is on. MetaApi is a paid service beyond its free allowance.

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
