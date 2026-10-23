# ADR-0013: Document storage and encryption

**Status:** Accepted (23 Oct 2026)

## Context

Plan.txt requires encrypted sensitive documents; tender documents are public data.

## Decision

IDocumentStore with filesystem and Azure Blob implementations. Company documents are encrypted with AES-256-GCM using a per-file data key wrapped by a master key (Key Vault on Azure, environment locally). Public tender documents are stored unencrypted.

## Consequences

Key rotation needs a runbook and a re-wrap job.
