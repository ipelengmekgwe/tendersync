# ADR-0003: PostgreSQL as the database

**Status:** Accepted (23 Oct 2026)

## Context

Raw OCDS JSON fits jsonb, full-text search is built in, free hosting exists (Neon, Supabase, Azure Flexible Server), Testcontainers support is mature. Plan.txt allowed SQL Server or PostgreSQL.

## Decision

PostgreSQL 17 via Npgsql and EF Core 10; Dapper for read-heavy queries.

## Consequences

No provider-specific SQL in Application so SQL Server stays possible by swapping the provider.
