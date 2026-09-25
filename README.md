# HVTradingBot

HVTradingBot is a research-driven, risk-first automated trading platform designed to analyze financial markets, identify high-quality trade opportunities, and support paper, approval-based, and eventually guarded automated execution.

## Core Principle

The system does **not** assume trades are certain.

The default decision is **NO_TRADE** unless multiple independent signals, market-regime checks, risk rules, and execution-quality checks agree.

AI/LLM components are advisory only and must never bypass deterministic risk controls.

## Quick Start (macOS)

Requirements: Docker Desktop, .NET 10 SDK, Node.js 22 (`brew install --cask dotnet-sdk docker && brew install node@22`).

```bash
./run.sh          # PostgreSQL in Docker, API + worker natively -> http://localhost:5080
./run.sh docker   # everything in Docker
./run.sh test     # all automated tests (integration tests need Docker)
./run.sh stop     # stop PostgreSQL
./run.sh reset    # delete the local database (all trading history)
./run.sh setup-code   # show the one-time code for creating the dashboard login
./run.sh reset-login  # forgotten password: delete the login and print a new setup code
```

**Dashboard login.** On first start the dashboard asks you to create a username and password. This needs a
one-time setup code that is printed only in the API log (and by `./run.sh`), so nobody who merely finds the
dashboard can claim it. After that every page, API call and live update requires the login; sessions last
12 hours (or 30 days with "Keep me signed in"), five wrong passwords lock the login for 15 minutes, and changing
the password (**Settings → Login & security**) signs out every other device. Health checks (`/health/*`) stay
public for hosting probes.

Then open the dashboard, go to **Settings** and enter your Deriv App ID and Personal Access Token
(from [developers.deriv.com](https://developers.deriv.com)). Only **demo** accounts are accepted.
Choose markets under **Settings → Markets**.

Set `BROKER_PROVIDER=Paper` in `.env` to trade on real Deriv prices with local simulated fills, and additionally
`MARKET_DATA_PROVIDER=Simulated` to run fully offline.

## What Is Implemented

- **Market data:** real prices from Deriv's public API (5-minute candles, live ticks); offline synthetic feed.
- **Markets:** Deriv catalog — Forex majors, Derived/synthetic indices, metals, crypto (tradable via multiplier
  contracts) and stock indices (analysis only; no multiplier contracts via the API).
- **Analysis:** EMA 9/20/50/200, RSI, MACD, ATR, ADX, Bollinger, ROC; market structure; regimes.
- **Strategies:** Trend Following, Breakout + Retest, Momentum, Mean Reversion, Volatility Expansion.
- **Evaluation:** every market at every 5-minute close; setups from closed 1H/4H/Daily bars; 15-minute timing.
- **Scoring:** 0–100 per `docs/TRADING_ENGINE.md`, abstention rules, configurable thresholds.
- **Adaptive learning:** every setup is tracked as a virtual trade; bounded score adjustments and disabling of
  persistently losing strategy/regime/asset-class combinations (never changes size or risk limits).
- **Risk engine:** all rules in `docs/RISK_MANAGEMENT.md`, checked at proposal and again before execution, kill switch.
  Defaults are a small-account profile (1% per trade, 3% daily, 8% weekly, 2 positions, broker fee ≤ 30% of risk);
  limits are editable under **Settings → Risk limits** within safe ranges and apply without a restart.
- **Execution:** Deriv demo account (multiplier contracts with broker-side stop loss / take profit, idempotent
  submission, unknown-state reconciliation) or local paper broker.
- **Journal:** every decision incl. NO_TRADE, orders, positions (R, MAE, MFE), audit log.
- **Email notifications (optional):** an email for every trade opened and closed (with the result), kill-switch
  changes and, optionally, rejected orders. Configure SMTP (e.g. Gmail with an App Password), recipients and events
  under **Settings → Notifications**, with a "Send test email" button; sent in the background so they never delay
  trading. `Notifications__Email__*` variables in `.env` are a fallback.
- **Backtesting:** same engine code, spread/slippage/commission, reproducible synthetic or stored real data.
- **Dashboard:** status, markets, decisions, positions, history, risk, performance, learning, backtest, audit,
  settings; live updates over SignalR.

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
- [macOS App](docs/MACOS_APP.md)
- [Agent Build Guide](docs/AGENTS.md)
- [ADRs](docs/ADR.md)

## Non-Negotiable Safety Rules

- PAPER mode is always the default.
- No AI component can place an order directly.
- Every trade must pass deterministic risk validation.
- Broker credentials must never be committed (Deriv token is stored encrypted in PostgreSQL via the Settings page).
- Backtests must include spread, fees, and slippage assumptions.
- Live broker state is authoritative for open positions.
- Duplicate execution must be prevented with idempotency.
- A stale market feed blocks new trading.
- Kill switch blocks new trading immediately.
- Rejected opportunities are stored for later analysis.

## Status

MVP running against a Deriv **demo** account (see ADR-008). Real-money trading, approval mode, authentication and
Interactive Brokers remain on the roadmap.

Known gaps:

- No user authentication yet (docs/SECURITY.md); the API listens on localhost only.
- Redis and RabbitMQ are not used yet; the modular monolith does not need them at this stage.
- Learning starts empty and needs weeks of setups before its adjustments carry weight.
