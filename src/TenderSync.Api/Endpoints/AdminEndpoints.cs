using MediatR;
using TenderSync.Api.Security;
using TenderSync.Application.Common.Models;
using TenderSync.Application.Features.Admin.Queries.ListAuditLogs;

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

        return app;
    }
}
