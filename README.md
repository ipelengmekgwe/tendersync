# TenderSync

Monitors South African public-sector tenders from National Treasury's eTenders OCDS API, classifies them against configurable IT and software interest categories, extracts the qualifying criteria from the bid packs, scores whether a company qualifies, and notifies immediately by email, Telegram and Microsoft Teams.

The full plan, architecture and work breakdown is in [PLAN.md](PLAN.md). Conventions for contributors and agents are in [CLAUDE.md](CLAUDE.md).

## Status

WP00 (repository bootstrap) and WP01 (domain model and persistence) are complete: the domain entities, the PostgreSQL schema with its Initial migration, reference-data seeding (interest categories, municipalities, roles, first admin) and the integration test harness exist. There is no ingestion, API surface or UI yet. See PLAN.md §17 for the work packages and their order.

WP02 adds the API host: JWT login with rotating refresh tokens, role policies (Admin ⊃ Reviewer ⊃ Viewer), the MediatR pipeline (logging, authorization, validation, unit of work, audit), problem-details errors, OpenAPI with Scalar at `/scalar`, health endpoints, rate limiting on auth, and the first endpoints (`/api/v1/auth/*`, `/api/v1/tenders`, `/api/v1/admin/audit-logs`). Ingestion is still to come (WP03), so the tender list is empty on a fresh database.

WP05 adds the Worker host: Hangfire on PostgreSQL (schema `hangfire`) with the `notify`, `ingest` and `pipeline` queues, the recurring-job schedule in `JobSchedule`, job-run recording, and the outbox dispatcher that turns persisted domain events into MediatR notifications every minute. The Hangfire dashboard at `http://localhost:5001/hangfire` needs an Admin JWT: open it as `/hangfire?access_token=<token>` once and the token moves into a cookie.

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
