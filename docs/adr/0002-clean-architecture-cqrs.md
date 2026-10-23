# ADR-0002: Clean Architecture with CQRS

**Status:** Accepted (23 Oct 2026)

## Context

Testability and clear ownership so agents can work per layer without stepping on each other.

## Decision

Four layers (Domain, Application, Infrastructure, hosts) with MediatR 12.5.0 requests and handlers; feature folders inside Application.

## Consequences

Architecture tests enforce references. Some ceremony per feature; accepted for consistency.
