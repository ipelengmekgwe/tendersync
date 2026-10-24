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
- Persistence from Application goes through `IAppDbContext` (typed repositories + `SaveChangesAsync`) for writes and `IReadDb` (Dapper, raw SQL against the snake_case schema) for list/search queries. Application never references EF Core. Add a repository method when a handler needs a new write-side query; put read-side SQL in the handler.
- Domain events are raised inside entities (`Raise(...)`) and become `outbox_messages` rows automatically on `SaveChangesAsync`. Never dispatch events in handlers.
- JSON stored in jsonb columns is a `string` property named `*Json` on the entity with a typed accessor; serialise with `DomainJson`. EF cannot translate `Contains` over jsonb, so search JSON through `IReadDb` with `::text` or jsonb operators.
- Migrations: `dotnet dotnet-ef migrations add <Name> --project src/TenderSync.Infrastructure --startup-project src/TenderSync.Api --output-dir Persistence/Migrations`. The design-time factory reads `TENDERSYNC_CONNECTION`. The `tenders.search_vector` column lives only in raw SQL inside the Initial migration; keep it out of the model.
- Province and buyer type come from `ProvinceResolver` + the seeded `municipalities` table (`IMunicipalityLookup`); the feed has neither field.
- Requests are `ICommand<T>` (state change: unit of work saves, audit row written) or `IQuery<T>` (read only). Every request type carries `[Authorize]`, `[Authorize(Roles.X)]` or `[AllowAnonymous]`; a request without one fails closed with 401. Mark secrets on requests with `[Sensitive]` so logs and audit rows redact them.
- Handlers never call `SaveChangesAsync`; the `UnitOfWorkBehaviour` does it for commands. Throw `NotFoundException` / `ForbiddenException` / `ConflictException` / `DomainException`; the API maps them to problem details.
- Read configuration lazily (inside options lambdas or factories), never at registration time: `WebApplicationFactory` adds its overrides after `Program.cs` has registered services, and eager reads make tests hit the Compose database on port 5432.
- Minimal-API query binding treats non-nullable value-type parameters and `[AsParameters]` properties as required. Optional filters must be nullable and defaulted in the mapping.
- Endpoints live in `Api/Endpoints/<Area>Endpoints.cs` as `Map<Area>Endpoints` extension methods; groups set the tag and the authorization policy (`Policies.Viewer/Reviewer/Admin`).
- Background jobs are classes in `Worker/Jobs/` with one `ExecuteAsync(CancellationToken)` method, a `[Queue]` attribute from `JobQueues`, and a body wrapped in `JobRunner.RunAsync` so every run lands in `job_runs`. Register the class as scoped in `AddWorkerServices` and the schedule in `JobSchedule.Register` (cron in SAST). Jobs call Application services (`OutboxProcessor` style); logic never lives in the job class.
- React to domain events with `INotificationHandler<DomainEventNotification<TEvent>>` in `Application/Features/<Area>/EventHandlers/`. The outbox job publishes them; handlers must be idempotent because a failed batch is retried.
- Worker tests set `Hangfire:ServerEnabled=false` and invoke job classes directly from a scope; never rely on the Hangfire server firing in a test. The servers start from `HangfireServersHostedService`, which reads that flag at host start; registering them with `AddHangfireServer` at composition time read the flag eagerly, so a server ran during tests and quietly executed the jobs the tests had just enqueued.
- `NpgsqlDataSource.ConnectionString` strips the password. Anything that needs a raw connection string (Hangfire storage) reads it from `IConfiguration` at resolve time.
- New tender sources implement `ITenderSource` (Application) and map to `TenderSnapshot`; `TenderIngestor` does the upsert, never the source. The eTenders client lives in `Infrastructure/ETenders/` with its resilience pipeline in `AddETenders`; keep `PageSize` at 100. Every page is checked against `ocds-release-package.schema.json` (also in `docs/research/`); extend the schema when the mapper starts relying on a new field.
- Test projects get xunit, FluentAssertions and NSubstitute from `Directory.Build.targets`; adding them to a test csproj again fails the build (NU1504). Tests may only call public members (no `InternalsVisibleTo`).
- Build small PDF/DOCX/XLSX fixtures in code (`PdfDocumentBuilder`, Open XML, ClosedXML; see `tests/TenderSync.Infrastructure.Tests/Documents/SampleFiles.cs`) instead of committing binaries.
- Dapper read queries map onto private `Row` classes with settable properties, not positional records.
- JsonSchema.Net registers a schema's `$id` globally: load each schema once (static) or the second instance throws "Overwriting registered schemas".
- The eTenders API returns HTTP 500 for any page containing certain releases ("poison rows"), and pages shift as new releases arrive. `ETendersClient` bisects a failing page (100 → 10 → 1) and skips only those rows; plain 500 is excluded from retry and circuit-breaker handling for that reason. Skips are reported in `SourceFetchStats` and the ingest job's details.
- Documents: `TenderDocumentDownloader` and `DocumentTextExtraction` (Application) own the rules from PLAN.md §7.3; Infrastructure supplies `IDocumentDownloader` (throttled HttpClient `etenders-download`), `IDocumentStore` (`FileSystemDocumentStore` / `AzureBlobDocumentStore`, chosen by `DocumentStore:Provider`) and `IDocumentTextExtractor` (`CompositeTextExtractor` over one `ITextExtractor` per format, zip recursion inside). The stored extension comes from `DocumentFormats.Sniff` (magic bytes), not from the feed: Office Open XML and zip share the `PK` signature, so the declared format wins only for those. Store paths are `tenders/{ocid}/{documentId}.{ext}`; never build paths from titles.
- Pipeline jobs are enqueued, not recurring: Application event handlers call `IPipelineScheduler` (Hangfire in the Worker; `NoPipelineScheduler` in the API, which never dispatches outbox events). The pipeline queue runs on a second Hangfire server with `Hangfire:PipelineWorkerCount` workers so document and LLM steps keep to the polite concurrency.
- The filesystem document store defaults to `%LOCALAPPDATA%/TenderSync/documents` so the API (which streams downloads) and the Worker (which writes them) see the same files when started from different working directories; a relative `DocumentStore:FileSystem:RootPath` would differ per host.
- LLM calls go through the injected `ILlmClient` (`GuardedLlmClient`: cap → router → `llm_calls` row). Build requests from `IPromptRegistry.Get(purpose)` (`prompt.Body` is the system prompt, `prompt.VersionTag` the version) and a response record type; `LlmJsonSchema.For<T>()` is the schema both adapters send, so response types are records with simple members, `[Description]` on properties, enums as strings, nullable for optional. Adapters throw `LlmCallFailedException` (carries usage for billing) wrapping `LlmRefusalException` / `LlmOutputException`; callers see the inner exception. Never prefill the assistant turn; never read Anthropic SDK type names from memory (the beta namespace uses `BetaUserMessageParam`, `BetaRequestDocumentBlock`, `BetaJsonOutputFormat`; probe with reflection when unsure).
- Tests use `FakeLlmClient` (route the purpose to `fake`, register answers by purpose). Recorded provider responses live in `docs/research/llm-samples/`; the live smoke test and evals are gated by `TENDERSYNC_EVALS=1`.
- Classification: `KeywordPrefilter.Matches` is whole-word and case-insensitive (letters or digits on either side block the match), so keyword lists can hold short tokens like "ups", "api" and "sap" safely. `TenderClassifier` owns both stages and matching; jobs and the reclassify command call it, never the prefilter or the model directly. Thresholds and weights come from `ClassificationOptions` (bound in `AddLlm`). Company profiles with an empty `CategoryCodes` list match every category by design (v1 has one company).
- Requirement extraction: `RequirementExtractor` builds one `LlmDocumentPart` per document (native PDF when within `ExtractionOptions` limits, else `text/plain` with `[page N]` markers) and merges with `RequirementsMerger` (main document first; list items deduped by type and normalised text). New sections in `TenderRequirementsV1` need a `RequirementKind` mapping in `RequirementExtractor.ToRequirements` and a line in `docs/golden/extraction/README.md` if they are scored. Dates in the schema are ISO strings with `+02:00` (structured outputs do not take date-time formats); parse with `RequirementExtractor.ParseDate`.
- Qualification (PLAN.md §7.5): the deterministic evaluators live in Domain (`RequirementEvaluators.For(type)`, `DocumentRequirementMatcher`, `VerdictRules`) and are pure functions over the profile, so new rules get unit tests there, not in Application. `TenderQualifier` (Application) orchestrates: rules, then the fast model for certification wording the rules could not match, then the strong model for the functionality estimate; it never lets the model decide a hard fact. Thresholds come from `RequirementValueParser` reading the extracted `value` text; a value it cannot parse is Unknown, never a guess. Every Unknown, Expired, fuzzy match and compulsory briefing must appear in the report's questions. Key personnel go to the model by role only and only when `CompanyProfile.ShareKeyPersonnelWithModel` is true.
- Company documents: `IEncryptionService` (Application port) returns streams; `AesGcmEnvelopeEncryptionService` writes `TSE1` chunked AES-256-GCM files and wrapped keys as `v1|{keyId}|{base64}`. The master key provider is chosen by `Encryption:Provider` and reads keys on first use, so tests set `Encryption:Keys:test` + `Encryption:ActiveKeyId`. Downloads are commands (`DownloadCompanyDocumentCommand`), not queries, so the audit behaviour records them. `RequestRedactor` walks nested request objects: a `[Sensitive]` property on a body record is still redacted, and `Stream` properties are never serialised (mark them `[Sensitive]` anyway).
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
