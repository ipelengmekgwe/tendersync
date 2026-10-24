using MediatR;
using TenderSync.Application.Abstractions;
using TenderSync.Application.Abstractions.Persistence;
using TenderSync.Application.Common.Exceptions;
using TenderSync.Application.Common.Messaging;
using TenderSync.Application.Security;

namespace TenderSync.Application.Features.Notifications.Queries.GetNotificationPreferences;

/// <summary>GET /notification-preferences: the caller's subscriptions.</summary>
[Authorize(Roles.Viewer)]
public sealed record GetNotificationPreferencesQuery : IQuery<IReadOnlyList<NotificationPreferenceDto>>;

public sealed class GetNotificationPreferencesHandler(IAppDbContext db, ICurrentUser currentUser) : IRequestHandler<GetNotificationPreferencesQuery, IReadOnlyList<NotificationPreferenceDto>>
{
    public async Task<IReadOnlyList<NotificationPreferenceDto>> Handle(GetNotificationPreferencesQuery request, CancellationToken cancellationToken)
    {
        var userId = currentUser.UserId ?? throw new UnauthorizedException();
        var preferences = await db.NotificationPreferences.ListByUserAsync(userId, cancellationToken);
        return preferences.OrderBy(p => p.Channel).Select(NotificationPreferenceDto.From).ToList();
    }
}
