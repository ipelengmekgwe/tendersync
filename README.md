# TenderSync

[![CI](https://github.com/ipelengmekgwe/tendersync/actions/workflows/ci.yml/badge.svg)](https://github.com/ipelengmekgwe/tendersync/actions/workflows/ci.yml) [![Docker images](https://github.com/ipelengmekgwe/tendersync/actions/workflows/docker.yml/badge.svg)](https://github.com/ipelengmekgwe/tendersync/actions/workflows/docker.yml) [![Evals](https://github.com/ipelengmekgwe/tendersync/actions/workflows/evals.yml/badge.svg)](https://github.com/ipelengmekgwe/tendersync/actions/workflows/evals.yml)

Monitors South African public-sector tenders from National Treasury's eTenders OCDS API, classifies them against configurable IT and software interest categories, extracts the qualifying criteria from the bid packs, scores whether a company qualifies, and notifies immediately by email, Telegram and Microsoft Teams.

## How it works

1. **Ingest.** Every 30 minutes the Worker reads the last three days of eTenders releases, 100 per page. It skips the rare
   rows that make the API fail, validates every page against the OCDS schema, and upserts tenders with their buyer,
   province, documents and status history. A weekly backfill covers 90 days.
2. **Classify.** A whole-word keyword prefilter checks each new or changed tender against your interest categories.
   Candidates go to a fast model, which assigns categories and a relevance score. Relevant tenders become *matches* for
   the company profile.
3. **Read the bid pack.** Matched tenders have their documents downloaded (throttled, sniffed, hashed) and their text
   extracted from PDF, Word, Excel and zip files.
4. **Extract requirements.** A strong model reads the bid documents and returns the mandatory criteria, required
   documents, functionality criteria, briefing and submission rules. Every item cites its document, page and quote.
5. **Qualify.** Deterministic rules check each mandatory requirement against the company profile and its encrypted
   document library: CSD, tax, B-BBEE, CIDB, turnover, years and references. A model helps only with certification
   wording and the functionality estimate. The verdict is Qualify, Partial or Not qualify, with a score, the missing
   items and the questions a person must answer.
6. **Notify.** Email, Telegram and Teams messages go out for new matches, finished reports, changes, cancellations,
   awards, closing and briefing reminders, and expiring company documents. Each fact is sent once, and quiet hours are
   respected.
7. **Review.** The dashboard has an overview, a matches inbox, tender search with near misses, and tender detail with
   the report and a PDF viewer that opens at the cited page. It also covers the company profile and documents,
   categories, notification settings and admin pages.

TenderSync never submits a bid (ADR-0015). It stops at a report a person acts on. The diagrams in
[docs/diagrams](docs/diagrams/README.md) show each flow as the code runs it.

## Documentation

| Topic | Where |
|---|---|
| Scope, eTenders facts, architecture, work packages | [PLAN.md](PLAN.md) |
| Conventions for contributors and coding agents | [CLAUDE.md](CLAUDE.md) |
| Sequence, class and deployment diagrams | [docs/diagrams](docs/diagrams/README.md) |
| Architecture decisions | [docs/adr](docs/adr/README.md) |
| Licence, publication policy, POPIA and model data | [docs/compliance.md](docs/compliance.md) |
| Security review and triage | [docs/security-review.md](docs/security-review.md) |
| 90-day backfill timing and source limits | [docs/load-sanity.md](docs/load-sanity.md) |
| Running locally and with Docker Compose | [docs/runbooks/local.md](docs/runbooks/local.md) |
| Worker one-shot commands (backfill, re-run a stage, demo data) | [docs/runbooks/worker-cli.md](docs/runbooks/worker-cli.md) |
| CI, images, evals and branch protection | [docs/runbooks/github.md](docs/runbooks/github.md) |
| Azure deployment, costs and rollback | [docs/runbooks/azure.md](docs/runbooks/azure.md) |
| Rotating the document master key | [docs/runbooks/key-rotation.md](docs/runbooks/key-rotation.md) |
| Golden sets for the model evals | [docs/golden](docs/golden) |
| What each work package added | [docs/history.md](docs/history.md) |

## Status

Phase 1 (WP00 to WP18 in PLAN.md §17.3) is built. Some things need your own credentials before they can be checked end
to end:

- An Anthropic key: the live classification, extraction and qualification runs and the nightly evals.
- SMTP and Telegram credentials: real message delivery.
- An Azure subscription: the first `dev` deployment.

Phase 2 (proposal generation, review checklists and submission packages) is outlined in PLAN.md §16 and not started.

## Prerequisites

- .NET SDK 10.0.x (see `global.json`)
- Docker Desktop (PostgreSQL 17 and Seq run in containers; integration tests use Testcontainers)
- Node.js 22 (for the dashboard)

## Quick start

Everything in Docker (PostgreSQL, Seq, migrations, API, Worker, dashboard), then open http://localhost:3000:

```powershell
./deploy/docker/tendersync.ps1 up
```

`./deploy/docker/tendersync.sh up` does the same in bash. Or develop on the host with only the database and Seq in Docker:

```bash
docker compose -f deploy/docker/docker-compose.yml up -d postgres seq
```

```bash
dotnet build TenderSync.slnx
```

```bash
dotnet test TenderSync.slnx
```

Integration tests start PostgreSQL 17 through Testcontainers, so Docker must be running for `dotnet test`.

```bash
dotnet run --project src/TenderSync.Api
```

`docs/runbooks/local.md` covers both ways, the helper commands and the production-like stack.

| Service | URL |
|---|---|
| API | http://localhost:5000 (Scalar at `/scalar`; the Visual Studio profile opens it on start) |
| Worker | http://localhost:5001 (Hangfire at `/hangfire`, Admin only) |
| Dashboard | http://localhost:3000 (`cd web && npm install --legacy-peer-deps && npm run dev`) |
| Seq logs | http://localhost:5341 |
| PostgreSQL | `localhost:5432`, database `tendersync` |

Compose reads `deploy/docker/.env` (`tendersync.ps1 init` writes one with random secrets; `deploy/docker/.env.example` lists every variable). For `dotnet run`, application secrets go in `dotnet user-secrets`.

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

## Repository layout

```
src/        Domain, Application, Infrastructure, Contracts, Api, Worker
tests/      unit, integration, API, architecture tests and LLM evals
web/        Next.js dashboard (Vitest unit tests, Playwright e2e)
docs/       ADRs, diagrams, research samples, golden sets, runbooks
deploy/     Docker Compose and Azure Bicep
```

## Data source and licence

Tender data comes from the National Treasury eTenders OCDS API, published under the Open Data Commons PDDL. The publisher states the data is in public beta and must not be used for critical decisions. TenderSync always links to the original tender and bid documents, and every extracted claim shows its source page. Verify in the original bid document before bidding. [docs/compliance.md](docs/compliance.md) records how the licence, the publication policy and POPIA are handled.
