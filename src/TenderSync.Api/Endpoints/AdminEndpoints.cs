using MediatR;
using TenderSync.Api.Security;
using TenderSync.Application.Common.Models;
using TenderSync.Application.Features.Admin.Queries.ListAuditLogs;
using TenderSync.Application.Features.Admin.Queries.ListJobRuns;
using TenderSync.Application.Features.Admin.Queries.ListSourceHealth;

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

        return app;
    }
}
