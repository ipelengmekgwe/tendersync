# TenderSync

Monitors South African public-sector tenders from National Treasury's eTenders OCDS API, classifies them against configurable IT and software interest categories, extracts the qualifying criteria from the bid packs, scores whether a company qualifies, and notifies immediately by email, Telegram and Microsoft Teams.

The full plan, architecture and work breakdown is in [PLAN.md](PLAN.md). Conventions for contributors and agents are in [CLAUDE.md](CLAUDE.md).

## Status

WP00 (repository bootstrap) and WP01 (domain model and persistence) are complete: the domain entities, the PostgreSQL schema with its Initial migration, reference-data seeding (interest categories, municipalities, roles, first admin) and the integration test harness exist. There is no ingestion, API surface or UI yet. See PLAN.md §17 for the work packages and their order.

WP02 adds the API host: JWT login with rotating refresh tokens, role policies (Admin ⊃ Reviewer ⊃ Viewer), the MediatR pipeline (logging, authorization, validation, unit of work, audit), problem-details errors, OpenAPI with Scalar at `/scalar`, health endpoints, rate limiting on auth, and the first endpoints (`/api/v1/auth/*`, `/api/v1/tenders`, `/api/v1/admin/audit-logs`). Ingestion is still to come (WP03), so the tender list is empty on a fresh database.

WP05 adds the Worker host: Hangfire on PostgreSQL (schema `hangfire`) with the `notify`, `ingest` and `pipeline` queues, the recurring-job schedule in `JobSchedule`, job-run recording, and the outbox dispatcher that turns persisted domain events into MediatR notifications every minute. The Hangfire dashboard at `http://localhost:5001/hangfire` needs an Admin JWT: open it as `/hangfire?access_token=<token>` once and the token moves into a cookie.

WP03 adds ingestion from the eTenders OCDS API: a resilient client (timeouts, retries, circuit breaker), the OCDS-to-tender mapper, JSON-schema drift detection, and the `IngestRecentReleases` (every 30 minutes), `BackfillReleases` (weekly) and `SourceHealthCheck` (every 15 minutes) jobs. Admins can read `/api/v1/admin/source-health` and `/api/v1/admin/jobs/runs`.

WP04 adds bid-pack documents: `DownloadTenderDocumentsJob` fetches every file of a tender through a throttled client (two concurrent downloads, 500 ms apart, 50 MB cap, audio/video skipped, magic-byte sniffing, sha256) into the document store at `tenders/{ocid}/{documentId}.{ext}`, and `ExtractDocumentTextJob` turns PDF (PdfPig), Word (Open XML), Excel (ClosedXML, ExcelDataReader for `.xls`) and zip archives (two levels deep) into per-page rows in `document_texts`. A PDF averaging fewer than 50 characters per page is flagged `needs_ocr`. Downloads run for matched tenders (WP07) and on request: `POST /api/v1/tenders/{id}/documents/download` (Reviewer) raises `TenderDocumentsRequested`, which the Worker's outbox turns into the job. `GET /api/v1/tenders/{id}/documents`, `.../documents/{docId}/download` (streams the stored copy) and `.../documents/{docId}/text` read the results. The store is the local filesystem by default (`%LOCALAPPDATA%/TenderSync/documents`, shared by the API and Worker) or Azure Blob (`DocumentStore:Provider=AzureBlob`); both are tested with the same round trip, Azure through an Azurite container. The pipeline queue has its own Hangfire server with `Hangfire:PipelineWorkerCount` workers (default 2).

WP06 adds the AI layer (PLAN.md §8): one `ILlmClient` that every feature injects, routed per purpose (`Llm:Routing:{purpose}` = `anthropic`, `openai` or `fake`, default `Llm:DefaultProvider`). The Anthropic adapter uses the official SDK with structured outputs (the JSON schema is generated from the C# response type), native PDF input with page citations, adaptive thinking at the request's effort, server-side refusal fallbacks and a cached system prompt; the OpenAI-compatible adapter speaks chat-completions with `response_format: json_schema` and sends documents as extracted text (Azure OpenAI, OpenRouter, Ollama). Every call writes an `llm_calls` row with tokens and USD (prices from `Llm:Pricing`, Anthropic list prices built in); `Llm:DailyCostCapUsd` (default 10) stops calls for the rest of the SAST day and raises one `PipelineFailure`. Prompts are versioned files under `src/TenderSync.Application/Prompts/{purpose}/v{n}.md` loaded by `PromptRegistry`. `GET /api/v1/admin/llm-calls` summarises spend by day, purpose and model; `LlmCostRollupJob` records the daily total at 23:55. Set `Llm:Anthropic:ApiKey` in user-secrets; without any key, route to `fake` (`FakeLlmClient` answers from canned JSON). The live smoke test in `tests/TenderSync.Evals` runs only with `TENDERSYNC_EVALS=1` and a key.

WP07 adds classification and matching (PLAN.md §7.2). Stage 1 is `KeywordPrefilter`: per enabled interest category, whole-word keyword hits weighted by field (title ×3, description ×1, document titles ×2) minus negative and global-negative hits ×2; a category at score 1 or more, or a services/unspecified tender procured by RFP or RFI, goes to stage 2. Stage 2 sends the tender text and the category list to the model with the `classify/v1` prompt and gets `{categories, relevance 0–100, isItServices, rationale, redFlags}` back as structured output. The result lands in `tender_classifications`; relevance at or above `Classification:MatchThreshold` (60) creates a `TenderMatch` for every active company profile whose capability categories overlap (an empty capability list matches everything), which raises `TenderMatched` for notifications and document download; 40–59 is a near miss (`GET /api/v1/tenders?nearMisses=true`). `ClassifyTenderJob` runs for every new tender and again when its text changes; `POST /api/v1/tenders/{id}/reclassify` (Reviewer) runs it on demand and `POST /api/v1/interest-categories/test` (Admin) dry-runs stage 1 on a tender or free text. The review file for the eval lives at `docs/golden/classification.jsonl` (set `isItMatch` per row); `tests/TenderSync.Evals` then reports stage-1 recall always and stage-2 precision/recall with `TENDERSYNC_EVALS=1` and a key.

One-shot operator modes on the Worker (no Hangfire server is started):

```bash
dotnet run --project src/TenderSync.Worker -- --ingest-days 7
```

```bash
dotnet run --project src/TenderSync.Worker -- --backfill-days 90
```

```bash
dotnet run --project src/TenderSync.Worker -- --probe
```

```bash
dotnet run --project src/TenderSync.Worker -- --download-documents ocds-9t57fa-175186,ocds-9t57fa-175322
```

The last one downloads and extracts the bid packs of the listed tenders inline and logs one line per document, which is how the WP04 acceptance rate was measured.

```bash
dotnet run --project src/TenderSync.Worker -- --classify-days 7 --prefilter-only
```

```bash
dotnet run --project src/TenderSync.Worker -- --classify ocds-9t57fa-175186,ocds-9t57fa-175322
```

```bash
dotnet run --project src/TenderSync.Worker -- --build-golden-set docs/golden/classification.jsonl --golden-days 30
```

`--classify-days` without `--prefilter-only` calls the model for every stage-1 candidate (about a quarter of releases, under a cent each on Haiku). The golden-set builder keeps existing labels when the file is rebuilt.

Integration tests start a PostgreSQL 17 container through Testcontainers, so Docker must be running for `dotnet test`.

### First run

The API refuses to start without a JWT signing key, and creates no user unless seed credentials are set. Put both in user-secrets (never in appsettings):

```bash
dotnet user-secrets set "Jwt:SigningKey" "<at least 32 random characters>" --project src/TenderSync.Api
```

```bash
dotnet user-secrets set "Seed:AdminEmail" "you@example.com" --project src/TenderSync.Api
```

```bash
dotnet user-secrets set "Seed:AdminPassword" "<at least 12 characters>" --project src/TenderSync.Api
```

The Worker needs the same `Jwt:SigningKey` (its own user-secrets id is `tendersync-worker`) so dashboard tokens issued by the API validate there.

In Development the API applies migrations and seeds on start (`Database:MigrateOnStartup` / `Database:SeedOnStartup` in `appsettings.Development.json`); the Worker never migrates, so start the API once before the Worker on a fresh database. Then sign in at `/scalar` with `POST /api/v1/auth/login` and paste the access token into Authorize.

## Prerequisites

- .NET SDK 10.0.x (see `global.json`)
- Docker Desktop (PostgreSQL 17 and Seq run in containers; integration tests use Testcontainers)
- Node.js 22 (from WP12 onwards, for the dashboard)

## Quick start

```bash
docker compose -f deploy/docker/docker-compose.yml up -d
dotnet build TenderSync.slnx
dotnet test TenderSync.slnx
dotnet run --project src/TenderSync.Api
```

| Service | URL |
|---|---|
| API | http://localhost:5000 (Scalar at `/scalar` from WP02) |
| Worker | http://localhost:5001 (Hangfire at `/hangfire` from WP05) |
| Seq logs | http://localhost:5341 |
| PostgreSQL | `localhost:5432`, database `tendersync` |

Copy `.env.example` to `.env` to change ports or credentials for Compose. Application secrets go in `dotnet user-secrets`.

## Repository layout

```
src/        Domain, Application, Infrastructure, Contracts, Api, Worker
tests/      unit, integration, API, architecture tests and LLM evals
docs/       ADRs, diagrams, research samples, golden sets, runbooks
deploy/     Docker Compose and Azure Bicep
```

## Data source and licence

Tender data comes from the National Treasury eTenders OCDS API, published under the Open Data Commons PDDL. The publisher states the data is in public beta and must not be used for critical decisions. TenderSync always links to the original tender and bid documents, and every extracted claim shows its source page. Verify in the original bid document before bidding.
