using FluentValidation;
using MediatR;
using TenderSync.Application.Abstractions.Persistence;
using TenderSync.Application.Common.Messaging;
using TenderSync.Application.Common.Models;
using TenderSync.Application.Security;

namespace TenderSync.Application.Features.Admin.Queries.ListAuditLogs;

public sealed record AuditLogDto(Guid Id, Guid? ActorUserId, string ActorName, string Action, string EntityType, string? EntityId, string? AfterJson, string? Ip, DateTimeOffset At);

[Authorize(Roles.Admin)]
public sealed record ListAuditLogsQuery(string? Action = null, string? EntityType = null, int Page = 1, int PageSize = Paging.DefaultPageSize) : IQuery<PagedResult<AuditLogDto>>;

public sealed class ListAuditLogsQueryValidator : AbstractValidator<ListAuditLogsQuery>
{
    public ListAuditLogsQueryValidator()
    {
        RuleFor(q => q.Page).GreaterThanOrEqualTo(1);
        RuleFor(q => q.PageSize).InclusiveBetween(1, Paging.MaxPageSize);
    }
}

public sealed class ListAuditLogsQueryHandler(IReadDb db) : IRequestHandler<ListAuditLogsQuery, PagedResult<AuditLogDto>>
{
    private const string Sql = """
        select id, actor_user_id, actor_name, action, entity_type, entity_id, after_json::text as after_json, ip, at, count(*) over() as total_count
        from tendersync.audit_logs
        where (@action::text is null or action = @action)
          and (@entityType::text is null or entity_type = @entityType)
        order by at desc, id
        limit @pageSize offset @offset
        """;

    public async Task<PagedResult<AuditLogDto>> Handle(ListAuditLogsQuery request, CancellationToken cancellationToken)
    {
        var rows = await db.QueryAsync<Row>(Sql, new
        {
            action = request.Action,
            entityType = request.EntityType,
            pageSize = request.PageSize,
            offset = Paging.Offset(request.Page, request.PageSize),
        }, cancellationToken);

        if (rows.Count == 0)
        {
            return PagedResult.Empty<AuditLogDto>(request.Page, request.PageSize);
        }

        var items = rows.Select(r => new AuditLogDto(r.Id, r.ActorUserId, r.ActorName, r.Action, r.EntityType, r.EntityId, r.AfterJson, r.Ip, r.At)).ToList();
        return new PagedResult<AuditLogDto>(items, request.Page, request.PageSize, (int)rows[0].TotalCount);
    }

    private sealed class Row
    {
        public Guid Id { get; init; }

        public Guid? ActorUserId { get; init; }

        public string ActorName { get; init; } = string.Empty;

        public string Action { get; init; } = string.Empty;

        public string EntityType { get; init; } = string.Empty;

        public string? EntityId { get; init; }

        public string? AfterJson { get; init; }

        public string? Ip { get; init; }

        public DateTimeOffset At { get; init; }

        public long TotalCount { get; init; }
    }
}
