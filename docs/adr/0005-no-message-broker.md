# ADR-0005: No message broker in v1

**Status:** Accepted (23 Oct 2026)

## Context

One worker and one database; a broker adds operations cost without a consumer.

## Decision

In-process MediatR notifications plus an outbox table drained by a Hangfire job; no MassTransit.

## Consequences

Add MassTransit when a second worker type or an external consumer appears.
