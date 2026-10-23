# ADR-0014: No browser automation in v1

**Status:** Accepted (23 Oct 2026)

## Context

Verified on 23 Oct 2026: the eTenders OCDS API needs no browser and bid documents download over plain HTTP.

## Decision

No Playwright in v1. An ITenderSource abstraction allows Playwright-based sources later. Drift detection validates API responses against a JSON schema and checks download content types.

## Consequences

Municipal and SOE portal scrapers arrive in WP23 with their own robots.txt and terms checks.
