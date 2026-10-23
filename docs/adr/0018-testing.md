# ADR-0018: Testing stack

**Status:** Accepted (23 Oct 2026)

## Context

Agents need fast, deterministic feedback; LLM behaviour needs separate, cost-capped evaluation.

## Decision

xUnit, FluentAssertions 7, NSubstitute, Testcontainers (PostgreSQL), Verify snapshots, NetArchTest layer rules, Playwright for dashboard e2e only, opt-in LLM evals with golden sets.

## Consequences

Integration tests need Docker locally and in CI.
