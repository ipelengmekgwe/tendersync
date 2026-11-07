# Architecture decision records

Each record states one decision, why it was made, and what follows from it. PLAN.md §3 lists the same decisions as
ADR-001 to ADR-018; the files here carry the full text. Decisions change only through a new record that supersedes the
old one (CLAUDE.md: "Do not change decisions in PLAN.md").

| ADR | Decision | Status | Where it shows up in the code |
|---|---|---|---|
| [0001](0001-backend-platform.md) | .NET 10 LTS, C# latest | Accepted | `global.json`, `Directory.Build.props` (nullable, warnings as errors, analyzers) |
| [0002](0002-clean-architecture-cqrs.md) | Clean Architecture with MediatR commands and queries, feature folders | Accepted | `src/TenderSync.Application/Features`, layer rules in `tests/TenderSync.Architecture.Tests` |
| [0003](0003-postgresql.md) | PostgreSQL 17 with EF Core for writes, Dapper for reads | Accepted | `IAppDbContext` (writes), `IReadDb` (list and search SQL), `Persistence/Migrations` |
| [0004](0004-hangfire.md) | Hangfire on PostgreSQL in a separate Worker | Accepted | `src/TenderSync.Worker/Jobs`, `JobSchedule`, `HangfireServersHostedService` |
| [0005](0005-no-message-broker.md) | In-process MediatR notifications plus an outbox, no broker | Accepted | `outbox_messages`, `OutboxProcessor`, `ProcessOutboxJob`, `Features/*/EventHandlers` |
| [0006](0006-resilience.md) | Resilience pipelines per external dependency | Accepted | `AddETenders` (poison-row bisect, 500 excluded from retry), `etenders-download`, LLM adapters |
| [0007](0007-openapi-scalar.md) | OpenAPI document rendered with Scalar | Accepted | `/openapi/v1.json`, `/scalar`; the dashboard client is generated from it |
| [0008](0008-logging.md) | Serilog with Seq locally, Application Insights on Azure | Accepted, partly built | Serilog everywhere; Azure Monitor OpenTelemetry when `APPLICATIONINSIGHTS_CONNECTION_STRING` is set (WP17). Trace context does not yet flow from API to Worker through the outbox |
| [0009](0009-mapping-validation.md) | Mapster and FluentValidation in a pipeline behaviour | Accepted | `*Validator.cs` next to each command, `ValidationBehaviour` |
| [0010](0010-nextjs-dashboard.md) | Next.js 15 dashboard with an OpenAPI-generated client | Accepted | `web/` (`npm run generate:api`) |
| [0011](0011-auth.md) | Identity, JWT with refresh rotation, roles Admin/Reviewer/Viewer, httpOnly cookies | Accepted | `IdentityService`, `[Authorize]` on every request type, `web/src/app/api/auth` |
| [0012](0012-llm-provider.md) | `ILlmClient` with Anthropic and OpenAI-compatible adapters | Accepted | `GuardedLlmClient` (cost cap → router → `llm_calls`), `Prompts/<purpose>/v<N>.md` |
| [0013](0013-document-storage-encryption.md) | Filesystem or Blob store; company documents in AES-256-GCM envelopes | Accepted | `IDocumentStore`, `AesGcmEnvelopeEncryptionService`, `docs/runbooks/key-rotation.md` |
| [0014](0014-no-playwright-v1.md) | No browser automation for sources in v1; schema drift detection instead | Accepted | `ITenderSource`, `ocds-release-package.schema.json` validation |
| [0015](0015-no-automated-submission.md) | The system never submits a bid | Accepted | No submission code exists; reports end with instructions for a human |
| [0016](0016-single-company-tenancy.md) | One company profile, with `CompanyProfileId` on every per-company row | Accepted | Matches, reports, notifications and preferences |
| [0017](0017-deployment.md) | Docker Compose first, Azure Container Apps as the option | Accepted | `deploy/docker` (WP15), `deploy/azure` and `deploy-azure.yml` (WP17) |
| [0018](0018-testing.md) | xUnit, FluentAssertions 7, NSubstitute, Testcontainers, Verify, NetArchTest, Playwright, opt-in evals | Accepted | `tests/`, `web/e2e`, `evals.yml` |
| [0019](0019-package-versions.md) | MediatR 12.5.0 and FluentAssertions 7.x pinned for licence reasons | Accepted | `Directory.Packages.props`, Dependabot ignores their majors |

## Writing a new record

Copy the shape of an existing file: **Status**, **Context**, **Decision**, **Consequences**. Number it with the next
free number, add a row here, and link it from the pull request that needs it. To replace a decision, set the old
record's status to "Superseded by ADR-NNNN" rather than editing its text.
