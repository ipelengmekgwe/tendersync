using MediatR;
using TenderSync.Api.Security;
using TenderSync.Application.Abstractions.Identity;
using TenderSync.Application.Abstractions.Jobs;
using TenderSync.Application.Common.Models;
using TenderSync.Application.Features.Admin.Commands.RunJobNow;
using TenderSync.Application.Features.Admin.Queries.ListRecurringJobs;
using TenderSync.Application.Features.Users.Commands.CreateUser;
using TenderSync.Application.Features.Users.Commands.UpdateUser;
using TenderSync.Application.Features.Users.Queries.ListUsers;
using TenderSync.Application.Features.Admin.Queries.GetLlmCallsSummary;
using TenderSync.Application.Features.Admin.Queries.ListAuditLogs;
using TenderSync.Application.Features.Admin.Queries.ListJobRuns;
using TenderSync.Application.Features.Admin.Queries.ListSourceHealth;
using TenderSync.Application.Features.Companies.Commands.RewrapCompanyDocuments;

namespace TenderSync.Api.Endpoints;

public static class AdminEndpoints
{
    /// <summary>PUT /admin/users/{id} body. NewPassword is optional; leave it empty to keep the password.</summary>
    public sealed record UpdateUserRequest(string DisplayName, string Role, bool Disabled, string? NewPassword);

    public static IEndpointRouteBuilder MapAdminEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/v1/admin")
            .WithTags("Admin")
            .RequireAuthorization(Policies.Admin);

        group.MapGet("/audit-logs", async (string? action, string? entityType, int? page, int? pageSize, ISender sender, CancellationToken ct) =>
                TypedResults.Ok(await sender.Send(new ListAuditLogsQuery(action, entityType, page ?? 1, pageSize ?? Paging.DefaultPageSize), ct)))
            .WithSummary("Audit log of every command, newest first.")
            .Produces<PagedResult<AuditLogDto>>();

        group.MapGet("/source-health", async (string? source, int? take, ISender sender, CancellationToken ct) =>
                TypedResults.Ok(await sender.Send(new ListSourceHealthQuery(source, take ?? 50), ct)))
            .WithSummary("Latest reachability, latency and schema-drift probes of the tender sources.")
            .Produces<IReadOnlyList<SourceHealthDto>>();

        group.MapGet("/jobs/runs", async (string? jobName, int? page, int? pageSize, ISender sender, CancellationToken ct) =>
                TypedResults.Ok(await sender.Send(new ListJobRunsQuery(jobName, page ?? 1, pageSize ?? Paging.DefaultPageSize), ct)))
            .WithSummary("Background job runs, newest first, with per-run details such as ingest summaries.")
            .Produces<PagedResult<JobRunDto>>();

        group.MapGet("/llm-calls", async (DateTimeOffset? from, DateTimeOffset? to, string? purpose, int? recent, ISender sender, CancellationToken ct) =>
                TypedResults.Ok(await sender.Send(new GetLlmCallsSummaryQuery(from, to, purpose, recent ?? 50), ct)))
            .WithSummary("Model spend: totals by day, purpose and model for a window (default 30 days), today's spend against the cap, and the latest calls.")
            .Produces<LlmCallsSummaryDto>()
            .ProducesValidationProblem();

        group.MapGet("/jobs", async (ISender sender, CancellationToken ct) =>
                TypedResults.Ok(await sender.Send(new ListRecurringJobsQuery(), ct)))
            .WithSummary("Recurring jobs registered by the Worker: cron (SAST), queue, next and last execution, state of the last run.")
            .Produces<IReadOnlyList<RecurringJobInfo>>();

        group.MapPost("/jobs/{id}/run", async (string id, ISender sender, CancellationToken ct) =>
                TypedResults.Accepted((string?)null, await sender.Send(new RunJobNowCommand(id), ct)))
            .WithSummary("Asks the Worker to run a recurring job now (through the outbox; it starts within about a minute).")
            .Produces<RunJobNowResponse>(StatusCodes.Status202Accepted)
            .ProducesProblem(StatusCodes.Status404NotFound);

        group.MapGet("/users", async (ISender sender, CancellationToken ct) =>
                TypedResults.Ok(await sender.Send(new ListUsersQuery(), ct)))
            .WithSummary("Every account with its role, disabled flag, lockout and last sign-in.")
            .Produces<IReadOnlyList<UserAccount>>();

        group.MapPost("/users", async (CreateUserCommand command, ISender sender, CancellationToken ct) =>
            {
                var created = await sender.Send(command, ct);
                return TypedResults.Created($"/api/v1/admin/users/{created.Id}", created);
            })
            .WithSummary("Creates an account with one role (Admin, Reviewer or Viewer).")
            .Produces<UserAccount>(StatusCodes.Status201Created)
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status409Conflict);

        group.MapPut("/users/{id:guid}", async (Guid id, UpdateUserRequest request, ISender sender, CancellationToken ct) =>
                TypedResults.Ok(await sender.Send(new UpdateUserCommand(id, request.DisplayName, request.Role, request.Disabled, request.NewPassword), ct)))
            .WithSummary("Changes display name and role, disables or re-enables (revoking sessions), optionally sets a new password. The last enabled admin cannot be demoted or disabled.")
            .Produces<UserAccount>()
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict);

        group.MapPost("/encryption/rewrap", async (ISender sender, CancellationToken ct) =>
                TypedResults.Ok(await sender.Send(new RewrapCompanyDocumentsCommand(), ct)))
            .WithSummary("Master-key rotation: re-wraps every company document's data key with the active key (docs/runbooks/key-rotation.md).")
            .Produces<RewrapResult>();

        return app;
    }
}
