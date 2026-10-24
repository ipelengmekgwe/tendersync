using MediatR;
using TenderSync.Api.Security;
using TenderSync.Application.Common.Models;
using TenderSync.Application.Features.Admin.Queries.GetLlmCallsSummary;
using TenderSync.Application.Features.Admin.Queries.ListAuditLogs;
using TenderSync.Application.Features.Admin.Queries.ListJobRuns;
using TenderSync.Application.Features.Admin.Queries.ListSourceHealth;
using TenderSync.Application.Features.Companies.Commands.RewrapCompanyDocuments;

namespace TenderSync.Api.Endpoints;

public static class AdminEndpoints
{
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

        group.MapPost("/encryption/rewrap", async (ISender sender, CancellationToken ct) =>
                TypedResults.Ok(await sender.Send(new RewrapCompanyDocumentsCommand(), ct)))
            .WithSummary("Master-key rotation: re-wraps every company document's data key with the active key (docs/runbooks/key-rotation.md).")
            .Produces<RewrapResult>();

        return app;
    }
}
