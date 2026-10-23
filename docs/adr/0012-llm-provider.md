# ADR-0012: LLM provider abstraction

**Status:** Accepted (23 Oct 2026)

## Context

Anthropic offers native PDF input, structured outputs and page-level citations, which match the requirement to show the source page of every extracted claim. Plan.txt asks for OpenAI-compatible APIs, which the second adapter satisfies.

## Decision

ILlmClient with two adapters: the official Anthropic SDK (primary: Claude Opus 5.5 for extraction and qualification, Claude Haiku 5.5 for classification) and an OpenAI-compatible chat-completions adapter.

## Consequences

Routing per purpose is configuration. Prompts are provider-neutral markdown. Every call is cost-logged.
