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
