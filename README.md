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

WP08 adds requirement extraction (PLAN.md §7.4). `ExtractRequirementsJob` sends each usable document of a tender to the Strong tier with the `extract-requirements/v1` prompt: PDFs up to 32 MB and 600 pages go natively with citations (page numbers come back from the model), larger or scanned ones go as extracted text with `[page N]` markers. The per-document answers (`TenderRequirementsV1`: summary, mandatory items by type, functionality criteria and threshold, evaluation method, required documents and form codes, briefing, submission, pricing, contract, contacts) are merged with the main bid document winning conflicts, then replace `tender_requirements` and fill the tender's summary, briefing and submission columns. Every row carries its source document, page and quote; rows under 0.5 confidence or without a page are flagged for verification. Extraction chains automatically once the last document of a matched tender has text; `POST /api/v1/tenders/{id}/extract` (Reviewer) queues it on demand (and downloads first if needed); `GET /api/v1/tenders/{id}/requirements` returns the grouped result. Enquiry contacts are stored for internal use only and never sent through notification channels. The labelling guide for the 20-tender accuracy set is in `docs/golden/extraction/README.md`.

WP09 adds the company profile and the qualification engine (PLAN.md §7.5, §12). `/api/v1/company-profiles` is the CRUD surface for the profile the engine compares against (registrations, CSD/tax/B-BBEE/CIDB status with expiries, provinces, capabilities: category codes, technologies, certifications, reference projects, key personnel); the tax PIN is `[Sensitive]` so audit rows redact it and responses mask it. `/api/v1/company-profiles/{id}/documents` is the encrypted document library: uploads are encrypted while they stream into the store (AES-256-GCM in authenticated chunks, a random data key per file wrapped by the master key, path `companies/{profileId}/{documentId}.enc`), downloads decrypt on the fly and are written to the audit log, deletes are soft. The master key comes from `Encryption:Keys:{id}` + `Encryption:ActiveKeyId` (or `TENDERSYNC_MASTER_KEY`) locally and from Azure Key Vault (`Encryption:Provider=KeyVault`) on Azure; `POST /api/v1/admin/encryption/rewrap` re-wraps every data key after a rotation (runbook: `docs/runbooks/key-rotation.md`). `QualifyTenderJob` runs once a matched tender's requirements are extracted, or on `POST /api/v1/tenders/{id}/qualify` (Reviewer): deterministic evaluators per requirement type decide every mandatory item from the profile (Pass / Fail / Unknown / Expired), required documents are checked against the library (SBD and MBD forms count as "to complete", never missing), certification wording the rules cannot match goes to the fast model (`fuzzy-match-certification/v1`), and the strong model estimates functionality points per criterion from the profile facts (`estimate-functionality/v1`; key personnel only by role and only when the profile opts in). The verdict follows PLAN.md §7.5: any Fail is NotQualify, any Unknown or Expired or an estimate under the threshold is Partial, otherwise Qualify; the score is a weighted mix (`Qualification:*Weight`). Every Unknown, fuzzy match, expired item and compulsory briefing becomes a question for a human. `GET /api/v1/tenders/{id}/qualification` returns the newest report with findings, missing items, questions and the stored markdown (disclaimer first, every finding linked to its source page).

WP10 adds the notification layer (PLAN.md §7.6, §9). Domain events reach `NotificationDispatcher` through the Worker's outbox: `TenderMatched` → NewMatch (bypassing quiet hours when the tender closes within two days), `QualificationCompleted` → QualificationReady (once per verdict), `TenderChanged` → TenderChanged for new documents or a moved closing date and AwardPublished when awards appear (matched tenders only, one message per distinct change), `TenderStatusChanged` → TenderChanged on cancellation, `PipelineFailure` → one alert per source per day. Each fact becomes one `notifications` row per enabled preference that wants the type, rendered from the Scriban templates under `src/TenderSync.Application/Notifications/Templates/{family}.{channel}.sbn` (first line subject; HTML for email and Telegram, markdown for Teams, plain text for SMS/WhatsApp; every message carries bid number, buyer, closing in SAST, relevance, verdict, the top missing items and the links), deduplicated by (type, tender, profile, channel, bucket) through a unique key, held until the recipient's quiet hours end, and sent by `SendNotificationJob` on the notify queue. Failures retry five times with 1-2-4-8-minute backoff on the row itself; `SweepNotificationsJob` (every minute) delivers held, retried and API-created rows; `DailyDigestJob` (07:00 SAST) sends the opt-in digest. Channels: email over MailKit SMTP (`Notifications:Email:*`), Telegram Bot API with an inline "Open tender" button (`Notifications:Telegram:BotToken`; the preference address is the chat id), Teams incoming webhooks with an Adaptive Card (the address is the webhook URL), SMS through `ISmsSender` (NoOp by default, Clickatell stub with `Notifications:Sms:Provider=Clickatell`), WhatsApp NoOp. Unconfigured channels mark rows Skipped rather than failing. Endpoints: `GET/PUT /api/v1/notification-preferences` (one entry per channel: address, types, enabled, quiet hours), `GET /api/v1/notifications` (mine, recipients masked), `POST /api/v1/notifications/test` (queues a test message the Worker delivers within about a minute).

WP11 adds status tracking and reminders (PLAN.md §7.7). `RefreshActiveTendersJob` (05:00 SAST) re-fetches every matched, unfinished tender by ocid through the same ingest upsert, so new documents, moved closing dates, awards and cancellations raise the same events as a normal ingest (and so the WP10 notifications); the least recently checked tenders go first, with a polite pause between calls. Cancellation is detected from the wording of a release (`CancellationDetector`: titles and document titles matched loosely, descriptions only on sentence-level wording), awards from the release's award section. `ComputeStatusJob` (hourly) re-derives Open → ClosingSoon → Closed as time passes; every transition lands in `tender_status_history` and, for matched tenders, Closed, Cancelled and Awarded become TenderChanged or AwardPublished messages. `ClosingRemindersJob` and `BriefingRemindersJob` (06:00) send the 7/3/1-day closing reminders for matched, non-dismissed tenders and briefing reminders two days ahead (compulsory briefings also to dismissed matches); `DocumentExpiryCheckJob` (06:30) flags company documents expiring within 30 days. Buckets are counted in South African calendar days and deduplicated on the target date, so a moved date reminds again and a re-run never repeats. `GET /api/v1/tenders/{id}/history` returns the transitions, the releases seen and the notifications sent.

WP12 adds the dashboard shell (PLAN.md §11, ADR-010/011): a Next.js 15 app in `web/` (TypeScript strict, Tailwind 4, shadcn/ui, TanStack Query, dark mode). Sign-in posts to the app's own route handler, which calls `POST /api/v1/auth/login` and keeps the access and refresh tokens in httpOnly cookies; the browser never holds a token. Client components call the API through `/api/proxy/*`, which adds the bearer token, rotates the refresh token once on a 401 and clears the cookies when that fails; server components use the same typed client directly. The client types are generated from the API's OpenAPI document (`npm run generate:api`, committed as `src/lib/api/schema.d.ts`). Pages: `/login`, `/` (overview shell), `/tenders` (search, status/province filters, matched-only and near-miss toggles, sort and paging, all in the URL) and a placeholder `/tenders/[id]`; the inbox, detail cards, company, settings and admin pages follow in WP13 and WP14. `npm run check` runs lint, type-check, Vitest and the production build; `npm run test:e2e` runs the Playwright login-and-list flow against a running stack (`E2E_EMAIL`/`E2E_PASSWORD`). `web/Dockerfile` builds the standalone image; `API_BASE_URL` points it at the API. `web/TenderSync.Web.esproj` only makes the folder visible in Visual Studio's Solution Explorer (JavaScript and TypeScript workload) with `npm run dev` as its start command; npm, Docker and CI never read it, and deleting it plus its line in `TenderSync.slnx` removes the integration.

WP13 adds the daily screens (PLAN.md §11). `/` shows new matches, matched tenders closing within 7 days, pipeline health (newest run per job, failed runs in the last 24 hours, newest probe per source), model cost this month (Admins only) and company documents or registrations expiring within 30 days, all from `GET /api/v1/overview`. `/matches` is the inbox: tabs per status (New, Reviewing, Applying, Applied, Dismissed) with counts, relevance, the newest verdict, a closing countdown and quick actions; dismissing asks for a reason. It reads `GET /api/v1/matches` (filters `Status`, `TenderId`, `CompanyProfileId`, `ClosingWithinDays`, `ActiveOnly`, `Sort`, paging) and changes matches with `PATCH /api/v1/matches/{id}` (Reviewer; status, notes, dismiss reason). These calls only record decisions; nothing is ever submitted (ADR-015). `/tenders` gains a closing-window filter and saved views, which are kept in the browser's localStorage. `/tenders/[id]` has the header facts, the match status and notes, Summary, **Can we bid?** (verdict, score, estimated functionality against the threshold, missing items, checks, questions), Requirements with source badges, Documents and History, plus reviewer actions: download documents, extract, qualify, reclassify. A source badge opens `/tenders/[id]/documents/[docId]?page=N`, a react-pdf viewer that starts at that page and highlights the quoted text. Documents in other formats show their extracted text. Every page carries the disclaimer banner and works at phone width. `npm run test:e2e` now also runs the WP13 flow (new match → open → change status → source opens the PDF at its page), which needs the demo tender from `--seed-demo`.

WP14 makes everything configurable from the dashboard (PLAN.md §11). `/company/profile` edits the company profile: details, registrations with expiry badges, CIDB, provinces, capabilities, certifications, key personnel and references. Admins edit and Reviewers read. The tax PIN is never shown; leaving it empty keeps the stored PIN and "Clear it" removes it. `/company/documents` uploads documents (encrypted with AES-256-GCM in the API), shows expiry badges and offers edit, download and, for Admins, delete. Uploading needs a master key on the API (`Encryption:Keys:{id}` plus `Encryption:ActiveKeyId`, or `TENDERSYNC_MASTER_KEY`); see `docs/runbooks/key-rotation.md`. `/settings/notifications` holds each person's channels, message types and quiet hours, with a test send. `/settings/categories` (Admin) edits interest categories through the new `GET/POST/PUT/DELETE /api/v1/interest-categories`. It can test the keyword stage against a stored tender and reclassify that tender on the spot. `/admin/pipeline` lists the Worker's scheduled jobs with "run now" (`GET /api/v1/admin/jobs`, `POST /api/v1/admin/jobs/{id}/run`; the request goes through the outbox, so the Worker starts the job within about a minute). The page also shows job runs, source health with schema drift and a model-cost-per-day chart. `/admin/audit` browses the audit log and `/admin/users` manages accounts (`GET/POST /api/v1/admin/users`, `PUT /api/v1/admin/users/{id}`). Disabling an account revokes its sessions, and the last enabled admin cannot be demoted or disabled. `/applications` is the Phase 2 placeholder over `GET /api/v1/applications`. Admin pages, category settings and company pages answer 404 to roles that cannot use them, and the API enforces the same roles.

The Playwright global setup now runs `--seed-demo` before every run (`E2E_SKIP_SEED=true` skips it). The category spec (edit a category, reclassify, see the match) needs the model stage to answer without a key, so it runs only with `E2E_FAKE_CLASSIFY=true` against an API started like this:

```bash
Llm__Routing__classify=fake Llm__FakeAnswers__classify='{"categories":[{"code":"APP_SUPPORT","confidence":0.9}],"relevance":85,"isItServices":true,"rationale":"Fake answer for the e2e run.","redFlags":[]}' dotnet run --project src/TenderSync.Api
```

`Llm:FakeAnswers:{purpose}` preloads a canned JSON answer into the fake provider. Use it only for local and e2e runs: every call returns the same answer.

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

```bash
dotnet run --project src/TenderSync.Worker -- --extract ocds-9t57fa-175186 --extraction-template docs/golden/extraction
```

```bash
dotnet run --project src/TenderSync.Worker -- --migrate-seed
```

```bash
dotnet run --project src/TenderSync.Worker -- --qualify ocds-9t57fa-175186,ocds-9t57fa-175322 --qualify-report docs/golden/qualification
```

```bash
dotnet run --project src/TenderSync.Worker -- --refresh-active --compute-status --reminders
```

```bash
dotnet run --project src/TenderSync.Worker -- --seed-demo
```

`--refresh-active`, `--compute-status` and `--reminders` run the WP11 jobs once (refresh matched tenders from the portal, re-derive statuses, send the closing, briefing and document-expiry reminders). `--extract` runs requirement extraction for the listed tenders on the Strong tier (tens of cents per tender) and, with `--extraction-template`, writes the review templates. `--qualify` runs the qualification engine for the listed tenders (every matched profile, or every active one when nothing matched) and, with `--qualify-report`, writes each report's markdown for the five-tender acceptance review. `--migrate-seed` is the production migration job: it applies migrations and refreshes reference data. `--seed-demo` (Development only) writes a demo tender, `DEMO/E2E/0001`, with a three-page PDF bid pack, requirements sourced to pages 2 and 3, a qualification report and a New match. It uses the active company profile and creates "Demo Technologies (Pty) Ltd" when there is none. Rerunning it resets the match to New, and no events reach the pipeline or notifications.

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

To see what is set for a project, list its secrets (values are shown in full, so do this in a private terminal):

```bash
dotnet user-secrets list --project src/TenderSync.Api
```

```bash
dotnet user-secrets list --project src/TenderSync.Worker
```

The stores are plain JSON files at `%APPDATA%\Microsoft\UserSecrets\tendersync-api\secrets.json` and `...\tendersync-worker\secrets.json` (the ids come from `UserSecretsId` in each csproj); in Visual Studio, right-click the project and choose **Manage User Secrets** to open the same file. `dotnet user-secrets remove "Key"` and `dotnet user-secrets clear` undo entries.

As an alternative for local testing, each host also loads an optional, git-ignored `appsettings.Development.local.json` next to its `appsettings.json` (any `appsettings.*.local.json` is ignored). It is read last, so its values win over user-secrets and environment variables; include only the keys you mean to override, because an empty string there blanks a secret. The file in each project folder holds commented examples.

While the API runs in the Development environment, `DevAuth:AllowAnyCredentials` (on in `appsettings.Development.json`) lets any email and password sign in: an unknown email gets an Admin account created on the spot and the password is never checked. `Program.cs` forces the flag off in every other environment and logs a warning when it is on. Set it to false (user-secrets or `appsettings.Development.local.json`) to test real sign-in, including lockout.

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
| API | http://localhost:5000 (Scalar at `/scalar`; the Visual Studio profile opens it on start) |
| Worker | http://localhost:5001 (Hangfire at `/hangfire` from WP05) |
| Dashboard | http://localhost:3000 (`cd web && npm install --legacy-peer-deps && npm run dev`) |
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
