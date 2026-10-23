# TenderSync

Monitors South African public-sector tenders from National Treasury's eTenders OCDS API, classifies them against configurable IT and software interest categories, extracts the qualifying criteria from the bid packs, scores whether a company qualifies, and notifies immediately by email, Telegram and Microsoft Teams.

The full plan, architecture and work breakdown is in [PLAN.md](PLAN.md). Conventions for contributors and agents are in [CLAUDE.md](CLAUDE.md).

## Status

WP00 (repository bootstrap) is complete. The solution builds and tests pass, but there is no business logic yet. See PLAN.md §17 for the work packages and their order.

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
