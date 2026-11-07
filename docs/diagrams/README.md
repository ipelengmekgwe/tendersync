# Diagrams

Mermaid sources that GitHub renders in place. They follow the code; when the code changes a flow, update the diagram in
the same pull request.

| Page | Contents |
|---|---|
| [sequences.md](sequences.md) | Ingest, match to notification, qualification, reminders and status |
| [domain.md](domain.md) | Class diagrams per bounded area: tenders and sources, classification and matching, requirements and qualification, company, notifications, operations and security |
| [deployment.md](deployment.md) | Docker Compose, Azure Container Apps, and the release flow from pull request to Azure |

To check that a diagram parses before pushing, render it with the Mermaid CLI:

```bash
docker run --rm -v "$PWD/docs/diagrams:/data" minlag/mermaid-cli:11.12.0 -i /data/example.mmd -o /data/example.svg
```

`example.mmd` holds the text between the ` ```mermaid ` fences. PLAN.md §4 has the original architecture sketch; where
it differs from these pages, these pages and the code are current.
