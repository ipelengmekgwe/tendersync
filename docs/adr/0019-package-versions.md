# ADR-0019: Deliberate package pins

**Status:** Accepted (23 Oct 2026)

## Context

TenderSync may become a commercial product; licence-clean dependencies avoid a future rewrite.

## Decision

MediatR stays at 12.5.0 (last Apache-2.0 release before the commercial licence from v13). FluentAssertions stays at 7.x (v8 moved to a commercial licence for non-open-source use). All other packages track their latest stable release as looked up on nuget.org on 23 Oct 2026.

## Consequences

Dependabot must be configured to ignore major versions of these two packages. Review this ADR if either project changes licence again.
