using TenderSync.Domain.Enums;
using TenderSync.Domain.Notifications;

namespace TenderSync.Domain.Tests.Notifications;

public class NotificationTests
{
    [Fact]
    public void Dedupe_key_is_deterministic_and_case_insensitive_on_recipient()
    {
        var tenderId = Guid.CreateVersion7();

        var a = Notification.BuildDedupeKey(NotificationType.ClosingSoon3d, NotificationChannel.Email, "Ipeleng@Example.com", tenderId, null, "3d");
        var b = Notification.BuildDedupeKey(NotificationType.ClosingSoon3d, NotificationChannel.Email, "ipeleng@example.com ", tenderId, null, "3d");

        a.Should().Be(b);
        a.Should().StartWith("closingsoon3d:email:ipeleng@example.com:");
    }

    [Fact]
    public void Failed_attempts_retry_until_the_limit()
    {
        var notification = Notification.Create(NotificationType.NewMatch, NotificationChannel.Telegram, "12345", "New match", "Body", null, null);

        for (var attempt = 1; attempt < Notification.MaxAttempts; attempt++)
        {
            notification.MarkFailed("timeout", TestSnapshots.Now).Should().BeTrue();
            notification.Status.Should().Be(NotificationStatus.Pending);
        }

        notification.MarkFailed("timeout", TestSnapshots.Now).Should().BeFalse();
        notification.Status.Should().Be(NotificationStatus.Failed);
        notification.Attempts.Should().Be(Notification.MaxAttempts);
    }

    [Fact]
    public void Outbox_message_serialises_the_event_with_its_type_name()
    {
        var evt = new Events.TenderFirstSeen(Guid.CreateVersion7(), "ocds-9t57fa-1", TestSnapshots.Now);

        var message = OutboxMessage.FromEvent(evt);

        message.Type.Should().Be("TenderSync.Domain.Events.TenderFirstSeen");
        message.PayloadJson.Should().Contain("\"ocid\":\"ocds-9t57fa-1\"");
        message.OccurredAt.Should().Be(TestSnapshots.Now);
    }
}
