using FluentValidation;
using MediatR;
using TenderSync.Application.Abstractions;
using TenderSync.Application.Abstractions.Persistence;
using TenderSync.Application.Common.Exceptions;
using TenderSync.Application.Common.Messaging;
using TenderSync.Application.Common.Models;
using TenderSync.Application.Security;
using TenderSync.Domain.Enums;

namespace TenderSync.Application.Features.Notifications.Queries.ListMyNotifications;

/// <summary>GET /notifications: messages addressed to any of the caller's preference addresses, newest first.</summary>
[Authorize(Roles.Viewer)]
public sealed record ListMyNotificationsQuery(NotificationStatus? Status = null, NotificationType? Type = null, int Page = 1, int PageSize = Paging.DefaultPageSize) : IQuery<PagedResult<NotificationDto>>;

public sealed class ListMyNotificationsQueryValidator : AbstractValidator<ListMyNotificationsQuery>
{
    public ListMyNotificationsQueryValidator()
    {
        RuleFor(q => q.Page).GreaterThanOrEqualTo(1);
        RuleFor(q => q.PageSize).InclusiveBetween(1, Paging.MaxPageSize);
    }
}

public sealed class ListMyNotificationsHandler(IReadDb db, ICurrentUser currentUser) : IRequestHandler<ListMyNotificationsQuery, PagedResult<NotificationDto>>
{
    private const string Sql = """
        select n.id, n.type, n.channel, n.recipient, n.subject, n.body, n.status, n.attempts, n.sent_at, n.not_before, n.error, n.tender_id, n.created_at,
               count(*) over() as total_count
        from tendersync.notifications n
        where lower(n.recipient) in (select lower(p.address) from tendersync.notification_preferences p where p.user_id = @userId)
          and (@status::int is null or n.status = @status)
          and (@type::int is null or n.type = @type)
        order by n.created_at desc, n.id desc
        limit @pageSize offset @offset
        """;

    public async Task<PagedResult<NotificationDto>> Handle(ListMyNotificationsQuery request, CancellationToken cancellationToken)
    {
        var userId = currentUser.UserId ?? throw new UnauthorizedException();
        var rows = await db.QueryAsync<Row>(Sql, new
        {
            userId,
            status = (int?)request.Status,
            type = (int?)request.Type,
            pageSize = request.PageSize,
            offset = Paging.Offset(request.Page, request.PageSize),
        }, cancellationToken);

        if (rows.Count == 0)
        {
            return PagedResult.Empty<NotificationDto>(request.Page, request.PageSize);
        }

        var items = rows.Select(r => new NotificationDto(
            r.Id, (NotificationType)r.Type, (NotificationChannel)r.Channel, NotificationSender.Mask(r.Recipient), r.Subject, r.Body, (NotificationStatus)r.Status,
            r.Attempts, r.SentAt, r.NotBefore, r.Error, r.TenderId, r.CreatedAt)).ToList();
        return new PagedResult<NotificationDto>(items, request.Page, request.PageSize, (int)rows[0].TotalCount);
    }

    private sealed class Row
    {
        public Guid Id { get; set; }

        public int Type { get; set; }

        public int Channel { get; set; }

        public string Recipient { get; set; } = string.Empty;

        public string Subject { get; set; } = string.Empty;

        public string Body { get; set; } = string.Empty;

        public int Status { get; set; }

        public int Attempts { get; set; }

        public DateTimeOffset? SentAt { get; set; }

        public DateTimeOffset? NotBefore { get; set; }

        public string? Error { get; set; }

        public Guid? TenderId { get; set; }

        public DateTimeOffset CreatedAt { get; set; }

        public long TotalCount { get; set; }
    }
}
