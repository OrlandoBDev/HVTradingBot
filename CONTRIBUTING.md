# Contributing

## Workflow

1. Create a feature branch.
2. Keep each PR focused on one logical change.
3. Add or update tests.
4. Update documentation.
5. Ensure build and tests pass.
6. Open a PR into main.

## Branch Naming

- feature/*
- bugfix/*
- docs/*
- chore/*
- refactor/*

## Commit Style

Use concise imperative commit messages.

Examples:

- Add market data abstraction
- Implement ATR indicator
- Add daily loss risk rule
- Fix duplicate paper order execution

## Pull Requests

PRs should include:

- What changed
- Why
- Testing performed
- Risk/safety impact
- Breaking changes
- Follow-up work

## Trading-Specific Review Checklist

- Does this change affect order execution?
- Does it change position sizing?
- Does it change risk rules?
- Can it bypass the kill switch?
- Can it cause duplicate orders?
- Does it use future data accidentally?
- Does it change paper/live behavior?
- Are new assumptions documented?
