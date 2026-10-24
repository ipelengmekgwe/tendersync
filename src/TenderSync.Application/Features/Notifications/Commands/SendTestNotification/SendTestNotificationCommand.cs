using System.Globalization;
using MediatR;
using Microsoft.Extensions.Options;
using TenderSync.Application.Abstractions;
using TenderSync.Application.Abstractions.Persistence;
using TenderSync.Application.Common.Exceptions;
using TenderSync.Application.Common.Messaging;
using TenderSync.Application.Security;
using TenderSync.Domain.Common;
using TenderSync.Domain.Enums;
using TenderSync.Domain.Notifications;

namespace TenderSync.Application.Features.Notifications.Commands.SendTestNotification;

/// <summary>
/// POST /notifications/test: queues a Test message to the caller's address on the given channel. The row is picked up by
/// the Worker's sweeper within about a minute (the API does not run Hangfire), so the response reports the queued row.
/// </summary>
[Authorize(Roles.Viewer)]
public sealed record SendTestNotificationCommand(NotificationChannel Channel) : ICommand<TestNotificationResponse>;

public sealed record TestNotificationResponse(Guid NotificationId, NotificationChannel Channel, string Recipient, string Subject);

public sealed class SendTestNotificationHandler(IAppDbContext db, ICurrentUser currentUser, NotificationTemplates templates, IOptions<NotificationOptions> options, IDateTimeProvider clock)
    : IRequestHandler<SendTestNotificationCommand, TestNotificationResponse>
{
    public async Task<TestNotificationResponse> Handle(SendTestNotificationCommand request, CancellationToken cancellationToken)
    {
        var userId = currentUser.UserId ?? throw new UnauthorizedException();
        var preference = (await db.NotificationPreferences.ListByUserAsync(userId, cancellationToken)).FirstOrDefault(p => p.Channel == request.Channel)
            ?? throw new NotFoundException($"You have no {request.Channel} preference; save one first (PUT /api/v1/notification-preferences).");
        if (!preference.Enabled)
        {
            throw new ConflictException($"Your {request.Channel} preference is disabled.");
        }

        var now = clock.UtcNow;
        var model = new NotificationModel
        {
            Type = NotificationType.Test.ToString(),
            Date = NotificationModel.Sast(now),
            Company = currentUser.DisplayName,
            Message = $"Test message requested by {currentUser.DisplayName} on {NotificationModel.Sast(now)}.",
            DashboardUrl = options.Value.DashboardBaseUrl,
        };
        var rendered = templates.Render(NotificationType.Test, request.Channel, model);
        var payload = DomainJson.Serialize(new { actionUrl = options.Value.DashboardBaseUrl, actionLabel = "Open TenderSync" });
        var notification = Notification.Create(NotificationType.Test, request.Channel, preference.Address, rendered.Subject, rendered.Body, null, null, now.ToUnixTimeMilliseconds().ToString(CultureInfo.InvariantCulture), payload);
        db.Notifications.Add(notification);
        return new TestNotificationResponse(notification.Id, request.Channel, NotificationSender.Mask(preference.Address), rendered.Subject);
    }
}
