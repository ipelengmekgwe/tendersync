using TenderSync.Application.Abstractions.Notifications;
using TenderSync.Domain.Enums;

namespace TenderSync.Worker.Tests;

/// <summary>Replaces every real channel in the test host and records what was sent.</summary>
public sealed class FakeNotificationChannel(NotificationChannel channel) : INotificationChannel
{
    private readonly List<NotificationMessage> _sent = [];

    public NotificationChannel Channel { get; } = channel;

    public Func<NotificationMessage, NotificationSendResult> Respond { get; set; } = _ => NotificationSendResult.Ok("fake");

    public IReadOnlyList<NotificationMessage> Sent
    {
        get
        {
            lock (_sent)
            {
                return [.. _sent];
            }
        }
    }

    public Task<NotificationSendResult> SendAsync(NotificationMessage message, CancellationToken cancellationToken = default)
    {
        lock (_sent)
        {
            _sent.Add(message);
        }

        return Task.FromResult(Respond(message));
    }
}
