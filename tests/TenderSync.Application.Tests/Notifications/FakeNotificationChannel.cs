using TenderSync.Application.Abstractions.Notifications;
using TenderSync.Domain.Enums;

namespace TenderSync.Application.Tests.Notifications;

/// <summary>Records what a channel was asked to send and answers as scripted.</summary>
public sealed class FakeNotificationChannel(NotificationChannel channel) : INotificationChannel
{
    public NotificationChannel Channel { get; } = channel;

    public List<NotificationMessage> Sent { get; } = [];

    public Func<NotificationMessage, NotificationSendResult> Respond { get; set; } = _ => NotificationSendResult.Ok("fake-1");

    public Task<NotificationSendResult> SendAsync(NotificationMessage message, CancellationToken cancellationToken = default)
    {
        Sent.Add(message);
        return Task.FromResult(Respond(message));
    }
}
