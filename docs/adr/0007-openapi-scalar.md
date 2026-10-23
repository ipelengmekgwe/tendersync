# ADR-0007: OpenAPI with Scalar

**Status:** Accepted (23 Oct 2026)

## Context

Plan.txt asks for Scalar; the dashboard client is generated from the OpenAPI document.

## Decision

Microsoft.AspNetCore.OpenApi generates the document; Scalar.AspNetCore renders it at /scalar.

## Consequences

The OpenAPI document is snapshot-tested so contract changes are visible in PRs.
