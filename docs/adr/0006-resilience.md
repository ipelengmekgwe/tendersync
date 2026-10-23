# ADR-0006: Resilience with Polly

**Status:** Accepted (23 Oct 2026)

## Context

The eTenders API returns 500 for page sizes over 100 and occasionally hangs.

## Decision

Microsoft.Extensions.Http.Resilience pipelines per external dependency: timeout, retry with jitter, circuit breaker.

## Consequences

Every outbound HttpClient is registered with a named pipeline; page size is fixed at 100.
