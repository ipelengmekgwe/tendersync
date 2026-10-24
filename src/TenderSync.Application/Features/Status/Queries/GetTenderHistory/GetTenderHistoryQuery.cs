using MediatR;
using TenderSync.Application.Abstractions.Persistence;
using TenderSync.Application.Common.Exceptions;
using TenderSync.Application.Common.Messaging;
using TenderSync.Application.Features.Notifications;
using TenderSync.Application.Features.Tenders;
using TenderSync.Application.Security;
using TenderSync.Domain.Enums;
using TenderSync.Domain.Tenders;

namespace TenderSync.Application.Features.Status.Queries.GetTenderHistory;

public sealed record TenderReleaseDto(Guid Id, string ReleaseId, DateTimeOffset ReleaseDate, string Tag, DateTimeOffset SeenAt);

public sealed record TenderNotificationDto(Guid Id, NotificationType Type, NotificationChannel Channel, string Recipient, NotificationStatus Status, DateTimeOffset? SentAt, DateTimeOffset CreatedAt);

/// <summary>Everything that happened to a tender (PLAN.md §10 detail page, History): status transitions, releases seen, notifications sent.</summary>
public sealed record TenderHistoryDto(
    Guid TenderId,
    TenderStatus Status,
    string? StatusReason,
    DateTimeOffset FirstSeenAt,
    DateTimeOffset LastSeenAt,
    DateTimeOffset? LastCheckedAt,
    IReadOnlyList<TenderStatusHistoryDto> StatusHistory,
    IReadOnlyList<TenderReleaseDto> Releases,
    IReadOnlyList<TenderNotificationDto> Notifications);

[Authorize(Roles.Viewer)]
public sealed record GetTenderHistoryQuery(Guid TenderId) : IQuery<TenderHistoryDto>;

public sealed class GetTenderHistoryHandler(IAppDbContext db, IReadDb readDb) : IRequestHandler<GetTenderHistoryQuery, TenderHistoryDto>
{
    private const string NotificationsSql = """
        select id, type, channel, recipient, status, sent_at, created_at
        from tendersync.notifications
        where tender_id = @tenderId
        order by created_at desc
        limit 200
        """;

    public async Task<TenderHistoryDto> Handle(GetTenderHistoryQuery request, CancellationToken cancellationToken)
    {
        var tender = await db.Tenders.GetWithDetailsAsync(request.TenderId, cancellationToken)
            ?? throw new NotFoundException(nameof(Tender), request.TenderId);
        var notifications = await readDb.QueryAsync<NotificationRow>(NotificationsSql, new { tenderId = tender.Id }, cancellationToken);

        return new TenderHistoryDto(
            tender.Id,
            tender.Status,
            tender.StatusReason,
            tender.FirstSeenAt,
            tender.LastSeenAt,
            tender.LastCheckedAt,
            tender.StatusHistory.OrderBy(h => h.At).Select(h => new TenderStatusHistoryDto(h.FromStatus, h.ToStatus, h.Reason, h.At)).ToList(),
            tender.Releases.OrderBy(r => r.ReleaseDate).Select(r => new TenderReleaseDto(r.Id, r.ReleaseId, r.ReleaseDate, r.Tag, r.CreatedAt)).ToList(),
            notifications.Select(n => new TenderNotificationDto(n.Id, (NotificationType)n.Type, (NotificationChannel)n.Channel, NotificationSender.Mask(n.Recipient), (NotificationStatus)n.Status, n.SentAt, n.CreatedAt)).ToList());
    }

    private sealed class NotificationRow
    {
        public Guid Id { get; set; }

        public int Type { get; set; }

        public int Channel { get; set; }

        public string Recipient { get; set; } = string.Empty;

        public int Status { get; set; }

        public DateTimeOffset? SentAt { get; set; }

        public DateTimeOffset CreatedAt { get; set; }
    }
}
