# ADR-0004: Hangfire for scheduling

**Status:** Accepted (23 Oct 2026)

## Context

Recurring jobs, retries and a dashboard with no extra infrastructure.

## Decision

Hangfire 1.8 with Hangfire.PostgreSql, hosted in a separate Worker process.

## Consequences

Quartz is the fallback if the PostgreSQL storage proves troublesome.
