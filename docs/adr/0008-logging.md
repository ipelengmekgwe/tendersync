# ADR-0008: Structured logging and tracing

**Status:** Accepted (23 Oct 2026)

## Context

Pipeline failures must be traceable from a notification back to the job run and the LLM call.

## Decision

Serilog to console and Seq locally, Application Insights on Azure; OpenTelemetry traces with a correlation id across API and Worker.

## Consequences

Log calls use message templates; no string interpolation.
