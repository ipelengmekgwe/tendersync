# TenderSync — conventions for Claude Code agents

Read `PLAN.md` first. It holds the product scope, the verified eTenders facts, the architecture decisions (ADR-001 to ADR-018) and the work package (WP) you are implementing. **Do not change decisions in PLAN.md.** If a decision needs to change, add a proposal under `docs/adr/` and say so in your PR.

## Build, test, run

```bash
dotnet build TenderSync.slnx
dotnet test TenderSync.slnx
docker compose -f deploy/docker/docker-compose.yml up -d     # PostgreSQL 17 + Seq
dotnet run --project src/TenderSync.Api                        # http://localhost:5000
dotnet run --project src/TenderSync.Worker                     # http://localhost:5001
```

Secrets go in `dotnet user-secrets` (API and Worker have `UserSecretsId`) or `.env` (never committed). `.env.example` lists every variable.

## Layering (enforced by `tests/TenderSync.Architecture.Tests`)

| Project | May reference | Must not reference |
|---|---|---|
| `TenderSync.Domain` | nothing | any other project, MediatR, EF Core, HttpClient |
| `TenderSync.Application` | Domain, MediatR, FluentValidation, Mapster, Microsoft.Extensions.* abstractions, Scriban | Infrastructure, hosts, EF Core, Npgsql, Dapper, Hangfire, Anthropic, HttpClient, Azure SDKs |
| `TenderSync.Infrastructure` | Application, Domain, every provider package | Api, Worker, Contracts |
| `TenderSync.Api` / `TenderSync.Worker` | Application, Infrastructure (composition root only) | each other |
| `TenderSync.Contracts` | nothing | everything |

If the architecture tests fail, fix the dependency, not the test.

## Code conventions

- C# latest, nullable enabled, warnings are errors, analyzers at `latest-recommended`. Fix warnings; suppress only with a justification comment.
- File-scoped namespaces. One public type per file. `sealed` by default for classes that are not designed for inheritance.
- Application is organised by feature: `Features/<Area>/<Commands|Queries>/<Name>/` holding `<Name>Command.cs` (or `Query`), `<Name>Handler.cs`, `<Name>Validator.cs`, and the response DTO. One MediatR handler per file.
- Ports (interfaces Infrastructure implements) live in `Application/Abstractions/`. Infrastructure never exposes types to Application beyond those interfaces.
- Domain entities have private setters and behaviour methods (`Tender.ApplyRelease(...)`), never public property bags. Domain events are plain records in `Domain/Events/`.
- IDs are `Guid` v7 generated in Domain. Timestamps are `DateTimeOffset` in UTC. Display in SAST only at the edge.
- Prompts are versioned markdown under `Application/Prompts/<purpose>/v<N>.md`; a change to a prompt bumps the version and must pass its eval.
- Configuration uses typed options classes validated on start (`ValidateDataAnnotations().ValidateOnStart()`).
- Logging: Serilog structured logging with message templates, never string interpolation in log calls.

## Tests

- xUnit, FluentAssertions 7 (pinned, Apache 2.0), NSubstitute, Testcontainers for PostgreSQL, Verify for snapshots.
- Test names read as sentences: `Ingest_is_idempotent_when_the_same_window_is_replayed`.
- Integration tests that need Docker are tagged with `[Trait("Category", "Integration")]`.
- LLM evals live in `tests/TenderSync.Evals` and are skipped unless `TENDERSYNC_EVALS=1` and a provider key are set.
- Recorded API samples live in `docs/research/etenders-samples/`. Add new fixtures there, never inline large JSON in tests.

## Working on a work package

1. Branch `wp/NN-short-name` from `main`.
2. Read the WP section in PLAN.md §17.3 and the design sections it names. Implement only that scope.
3. Keep `dotnet build` and `dotnet test` green. Run `docker compose` for integration tests.
4. Open a PR whose description maps every acceptance criterion of the WP to the evidence (test name, screenshot, command output).
5. Update `README.md` and `docs/` for anything new you introduced (settings, endpoints, jobs).

## Things agents get wrong here

- `MediatR` is pinned to 12.5.0 (last Apache-2.0 release). Do not upgrade it.
- `FluentAssertions` is pinned to 7.x for the same reason.
- The eTenders OCDS feed: `tender.title` is the **bid number**, `tender.description` is the human title. `PageSize` must stay at 100 (200 returns HTTP 500). See PLAN.md §2.
- Never auto-submit a bid (ADR-015). Never send bid-pack contact details through notification channels (POPIA).
- Company documents are encrypted at rest; never write them to disk unencrypted outside the document store.
