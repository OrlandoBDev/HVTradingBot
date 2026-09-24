# Architecture Decision Records

Use this file as an index for major architecture decisions.

## ADR-001 — Modular Monolith First

Status: Accepted

Start as a modular monolith with workers rather than microservices.

Reason:

The system requires strong transactional boundaries and rapid iteration. Premature microservices add operational complexity without proven scaling needs.

## ADR-002 — PAPER Is Default Mode

Status: Accepted

The application starts in PAPER mode unless explicitly configured otherwise.

## ADR-003 — AI Cannot Execute Trades

Status: Accepted

AI produces advisory analysis only.

Order execution requires deterministic strategy/risk/execution layers.

## ADR-004 — Same Strategy Code for Backtest and Runtime

Status: Accepted

Backtesting and runtime strategies share interfaces and implementations to reduce model drift.

## ADR-005 — Broker Abstraction

Status: Accepted

The Domain/Application layers must not depend directly on OANDA, IBKR, or other broker SDKs.

## ADR-006 — PostgreSQL as Initial Persistence

Status: Proposed

Use PostgreSQL unless environment constraints require SQL Server.

## ADR-007 — Modular Monolith Before Microservices

Status: Accepted

Split services only when justified by deployment, scaling, ownership, or reliability requirements.

## ADR-008 — Deriv Demo as the First Broker

Status: Accepted

Deriv replaces OANDA as the initial broker. Its API trades Forex and other markets as multiplier contracts
(MULTUP/MULTDOWN) with broker-side stop loss and take profit expressed in money. Positions are sized by the risk
engine, mapped to stake and multiplier, and priced with Deriv's quoted commission before buying. Only demo accounts
are accepted; the account type is checked on the REST account list and on the WebSocket URL.

## ADR-009 — Bounded, Deterministic Learning

Status: Accepted

Adaptation uses resolved outcomes of every setup (virtual trades), shrunk towards zero expectancy, to adjust scores
within fixed bounds or disable a strategy/regime/asset-class combination. It cannot alter position size, risk limits
or the risk engine's authority. Backtests learn inside the replay from a blank slate so results remain reproducible.

## ADR-010 — 5-Minute Evaluation Cadence

Status: Accepted

Strategies evaluate at every 5-minute close. Setups rely on closed 1H/4H/Daily bars and 15-minute timing, so faster
evaluation adds no information while multiplying journal volume and API load. Exits are enforced tick-by-tick by the
broker. Each signal bar can produce at most one executed order (idempotency key).

## ADR-011 — Broker Settings in PostgreSQL

Status: Accepted

Broker credentials, market selection and risk limits are entered on the dashboard and stored in PostgreSQL.
Risk limits are validated against ranges narrower than the configuration allows (they can be tuned, not disabled)
and are applied as an immutable snapshot per decision. The token is encrypted
with ASP.NET Core Data Protection; keys live outside the database. The API only stores settings; the worker is the
only process that connects to the broker.
