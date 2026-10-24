using TenderSync.Domain.Common;
using TenderSync.Domain.Enums;
using TenderSync.Domain.Notifications;

namespace TenderSync.Domain.Tests.Notifications;

public class NotificationTests
{
    private static readonly DateTimeOffset Now = TestSnapshots.Now;

    [Fact]
    public void Dedupe_key_covers_type_channel_recipient_tender_profile_and_bucket_and_normalises_the_recipient()
    {
        var tender = Guid.CreateVersion7();
        var profile = Guid.CreateVersion7();

        var key = Notification.BuildDedupeKey(NotificationType.ClosingSoon3d, NotificationChannel.Email, " Jane@Example.com ", tender, profile, "2026-11-17");

        key.Should().Be($"closingsoon3d:email:jane@example.com:{tender:N}:{profile:N}:2026-11-17");
        Notification.BuildDedupeKey(NotificationType.PipelineFailure, NotificationChannel.Telegram, "123", null, null).Should().Be("pipelinefailure:telegram:123:-:-:-");
    }

    [Fact]
    public void Failed_attempts_back_off_and_the_fifth_failure_is_final()
    {
        var notification = Notification.Create(NotificationType.NewMatch, NotificationChannel.Email, "a@b.c", "Subject", "Body", null, null);
        notification.IsDue(Now).Should().BeTrue();

        notification.MarkFailed("smtp down", Now).Should().BeTrue();
        notification.Status.Should().Be(NotificationStatus.Pending);
        notification.NotBefore.Should().Be(Now.AddMinutes(1));
        notification.IsDue(Now).Should().BeFalse();
        notification.IsDue(Now.AddMinutes(1)).Should().BeTrue();

        notification.MarkFailed("smtp down", Now).Should().BeTrue();
        notification.NotBefore.Should().Be(Now.AddMinutes(2));
        notification.MarkFailed("smtp down", Now);
        notification.NotBefore.Should().Be(Now.AddMinutes(4));
        notification.MarkFailed("smtp down", Now);
        notification.MarkFailed("smtp down", Now).Should().BeFalse("the fifth attempt is the last");
        notification.Status.Should().Be(NotificationStatus.Failed);
        notification.Attempts.Should().Be(Notification.MaxAttempts);
        notification.NotBefore.Should().BeNull();
    }

    [Fact]
    public void Sending_clears_the_hold_and_skipping_keeps_the_reason()
    {
        var notification = Notification.Create(NotificationType.Test, NotificationChannel.Sms, "+27821234567", "S", "B", null, null, notBefore: Now.AddHours(2));
        notification.IsDue(Now).Should().BeFalse("held by quiet hours");

        notification.MarkSent(Now.AddHours(2));
        notification.Status.Should().Be(NotificationStatus.Sent);
        notification.NotBefore.Should().BeNull();

        var skipped = Notification.Create(NotificationType.Test, NotificationChannel.WhatsApp, "+27821234567", "S", "B", null, null);
        skipped.Skip("no-op");
        skipped.Status.Should().Be(NotificationStatus.Skipped);
        skipped.Error.Should().Be("no-op");
    }

    [Fact]
    public void Preferences_store_quiet_hours_as_normalised_json_and_reject_bad_times()
    {
        var preference = NotificationPreference.Create(Guid.CreateVersion7(), NotificationChannel.Telegram, "123", [NotificationType.NewMatch]);
        preference.Update("123", [NotificationType.NewMatch, NotificationType.NewMatch], true, "{\"start\":\"22:00\",\"end\":\"06:00\"}");

        preference.Types.Should().Equal(NotificationType.NewMatch);
        preference.QuietHours.Should().Be(new QuietHours("22:00", "06:00", null));
        preference.QuietHoursJson.Should().Be("{\"start\":\"22:00\",\"end\":\"06:00\"}");
        preference.Wants(NotificationType.NewMatch).Should().BeTrue();
        preference.Wants(NotificationType.DailyDigest).Should().BeFalse();

        FluentActions.Invoking(() => preference.Update("123", [], true, "{\"start\":\"ten\",\"end\":\"06:00\"}")).Should().Throw<DomainException>();
    }
}
