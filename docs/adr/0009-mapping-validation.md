# ADR-0009: Mapster and FluentValidation

**Status:** Accepted (23 Oct 2026)

## Context

Plan.txt mandate; keeps handlers free of validation and mapping noise.

## Decision

Mapster (runtime configuration, no code generation) for DTO mapping; FluentValidation executed by a MediatR pipeline behaviour.

## Consequences

Validators live beside their requests; mapping configs are registered per feature.
