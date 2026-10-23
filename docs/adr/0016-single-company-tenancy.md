# ADR-0016: Single company in v1, SaaS-ready schema

**Status:** Accepted (23 Oct 2026)

## Context

Keeps the aggregator ambition from tender-portal-plan.md open without a rewrite.

## Decision

One company profile in v1; matches, reports, notifications and preferences carry CompanyProfileId.

## Consequences

Multi-tenant needs a TenantId and row-level filtering later.
