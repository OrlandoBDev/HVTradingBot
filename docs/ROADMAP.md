# Product Roadmap

## Current: macOS app MVP (Docker-based)

A native macOS app (.NET MAUI) that starts PostgreSQL, the API and the worker in Docker and shows the existing
dashboard in a WebView; it replaces `./run.sh` for everyday use until the bot moves to an external server.
Design and implementation tasks: [MACOS_APP.md](MACOS_APP.md).

Status: implemented on `feature/macos-app` (shared encryption keys, single-worker database lock, `run.sh` guards and
`./run.sh dev`, `HVTradingBot.App.Core` with tests, the Mac Catalyst app, CI). Remaining: the manual acceptance test
on a Mac.

## Next: learn Kubernetes with this application

After the macOS MVP. A step-by-step guide (`docs/KUBERNETES_GUIDE.md`, written when this phase starts) that moves
the same Docker stack onto Kubernetes, one concept at a time:

1. Local cluster: Docker Desktop's built-in Kubernetes (kind as an alternative); `kubectl` basics.
2. Images: build the `api` and `worker` images locally and use them from the cluster.
3. Namespace `hvtradingbot`; a Secret created from `.env`; a ConfigMap for non-secret settings.
4. PostgreSQL as a StatefulSet with a PersistentVolumeClaim and a headless Service; move the data over with
   `pg_dump`/`pg_restore`.
5. Data Protection keys on a PersistentVolumeClaim mounted by the API and worker (single node; multi-node needs a
   ReadWriteMany volume or a different key store).
6. API Deployment + Service with liveness (`/health/live`) and readiness probes. `/health/ready` includes the worker's
   health, so the API's readiness probe needs its own check.
7. Worker Deployment: `replicas: 1`, `strategy: Recreate`, backed by the worker's database lock, so a rollout
   never runs two workers.
8. Access: `kubectl port-forward` first, then an Ingress (ingress-nginx) on `hvtradingbot.localhost`.
9. Kustomize base and overlays (local vs server); optionally Helm.
10. Operating it: logs, events, rollouts and rollbacks, resource requests/limits.
11. When a domain and server exist: k3s on a VPS or a managed cluster, TLS with cert-manager.

## Later: mobile and Windows apps

The MAUI project from the macOS MVP, targeting iOS, Android and Windows, pointed at the external server once one
exists (needs remote access over HTTPS; Tailscale is an option before a domain is bought).

## Backlog

Ideas agreed for later, not yet scheduled into a phase.

### MetaTrader 5 as a second broker (Forex through Deriv MT5)

**Why:** Deriv multiplier contracts charge a commission of about 0.05% of the position value. At small-account
risk (≈1% per trade) that is 31–36% of the amount at risk on Forex, above the 30% fee cap, so every Forex
candidate is currently rejected. Deriv MT5 accounts are typically priced through the spread instead of a
commission, which may make Forex viable.

**First step (decides whether to build it):** verify Deriv MT5's actual Forex costs (spread and any commission)
on the demo account for the traded pairs and compare with the multiplier commission.

**Integration options** (MetaTrader has no official public trading API):

| Option | Notes |
|---|---|
| MetaApi (metaapi.cloud) | Cloud-hosted MT5 terminal with REST/WebSocket API; works from macOS/Linux hosting; paid beyond its trial/free allowance. Preferred. |
| MQL5 Expert Advisor bridge | EA in a running MT5 terminal talks to the app over a local socket; free but needs a Windows host (or Wine) running 24/7. |
| MetaTrader5 Python package | Windows only; also needs the terminal running. |

**Design:** add MT5 as a second `IExecutionBroker` next to Deriv; route Forex to MT5 and keep Derived markets on
Deriv multipliers. Risk engine, learning, dashboard and emails stay unchanged. Keep the demo-only guard
(refuse real accounts), idempotent client order ids and kill-switch-on-unknown-state behaviour.

## Phase 0 — Foundation

- Documentation
- Repository structure
- Solution skeleton
- CI
- Docker
- Database
- Logging
- Health checks

## Phase 1 — Forex Market Intelligence

- Market-data adapters
- Candle persistence
- Indicators
- Market structure
- Multi-timeframe analysis
- Regime detection

## Phase 2 — Strategy Engine

- Trend Following
- Breakout + Retest
- Momentum
- Mean Reversion
- Volatility Expansion
- Trade scoring
- Abstention

## Phase 3 — Risk Engine

- Position sizing
- Exposure
- Loss limits
- Correlation controls
- Spread/slippage controls
- Kill switch
- Risk test suite

## Phase 4 — Paper Trading MVP

- Paper broker
- execution
- position monitoring
- journaling
- dashboard
- SignalR

## Phase 5 — Backtesting

- historical replay
- out-of-sample
- walk-forward
- Monte Carlo
- performance analytics

## Phase 6 — Shadow Mode

- observe live markets
- record predictions
- no simulated or real orders
- probability calibration

## Phase 7 — OANDA Approval Mode

- live account read
- trade proposals
- human approval
- final risk check
- broker execution

## Phase 8 — Guarded Auto Trading

- strict eligibility
- small-capital deployment
- enhanced reconciliation
- operational alerts

## Phase 9 — Ensemble / Adaptive Intelligence

- strategy voting
- meta-strategy
- regime performance weighting
- probability calibration
- uncertainty modeling
- AI advisory analysis

## Phase 10 — Interactive Brokers

- multi-asset broker adapter
- futures/options support

## Phase 11 — VIX Intelligence

- VIX spot
- VX futures
- term structure
- contango/backwardation
- SPX relationship
- volatility-regime strategies

## Phase 12 — Advanced Research

- portfolio optimization
- feature selection
- ML models
- anomaly detection
- automated research pipelines

Production strategies remain gated by deterministic validation and risk controls.
