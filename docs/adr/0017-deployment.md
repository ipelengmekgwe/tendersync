# ADR-0017: Docker Compose first, Azure as the option

**Status:** Accepted (23 Oct 2026)

## Context

Cheap to start; Azure matches the owner's day-job.

## Decision

Docker Compose (api, worker, web, postgres, seq) for local and VPS; Azure Container Apps, PostgreSQL Flexible Server, Blob Storage and Key Vault via Bicep as the cloud option.

## Consequences

Two deployment targets to keep in sync; the Compose path is the reference.
