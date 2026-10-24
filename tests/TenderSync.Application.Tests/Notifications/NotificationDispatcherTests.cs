using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using TenderSync.Application.Abstractions;
using TenderSync.Application.Abstractions.Jobs;
using TenderSync.Application.Abstractions.Persistence;
using TenderSync.Application.Features.Notifications;
using TenderSync.Domain.Enums;
using TenderSync.Domain.Notifications;

namespace TenderSync.Application.Tests.Notifications;

/// <summary>Recipient selection, dedupe (same event twice → one row), quiet hours and the urgent bypass.</summary>
public class NotificationDispatcherTests
{
    private static readonly DateTimeOffset Noon = new(2026, 10, 23, 10, 0, 0, TimeSpan.Zero); // 12:00 SAST
    private static readonly DateTimeOffset Night = new(2026, 10, 23, 21, 0, 0, TimeSpan.Zero); // 23:00 SAST

    private readonly IAppDbContext _db = Substitute.For<IAppDbContext>();
    private readonly IPipelineScheduler _scheduler = Substitute.For<IPipelineScheduler>();
    private readonly IDateTimeProvider _clock = Substitute.For<IDateTimeProvider>();
    private readonly List<NotificationPreference> _preferences = [];
    private readonly List<Notification> _stored = [];

    public NotificationDispatcherTests()
    {
        _db.NotificationPreferences.ListEnabledAsync(Arg.Any<CancellationToken>()).Returns(_ => _preferences.Where(p => p.Enabled).ToList());
        _db.Notifications.ExistsAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(call => _stored.Any(n => n.DedupeKey == call.Arg<string>()));
        _db.Notifications.When(r => r.Add(Arg.Any<Notification>())).Do(call => _stored.Add(call.Arg<Notification>()));
    }

    private NotificationDispatcher Sut(DateTimeOffset now)
    {
        _clock.UtcNow.Returns(now);
        return new NotificationDispatcher(_db, new NotificationTemplates(), _scheduler, _clock, NullLogger<NotificationDispatcher>.Instance);
    }

    private NotificationPreference Prefer(NotificationChannel channel, string address, string? quietHours = null, params NotificationType[] types)
    {
        var preference = NotificationPreference.Create(Guid.CreateVersion7(), channel, address, types.Length == 0 ? [NotificationType.NewMatch] : types);
        preference.Update(address, preference.Types, true, quietHours);
        _preferences.Add(preference);
        return preference;
    }

    [Fact]
    public async Task One_row_per_preference_that_wants_the_type_and_the_same_event_twice_gives_no_second_row()
    {
        Prefer(NotificationChannel.Email, "jane@example.com", null, NotificationType.NewMatch, NotificationType.QualificationReady);
        Prefer(NotificationChannel.Telegram, "123456", null, NotificationType.NewMatch);
        Prefer(NotificationChannel.Teams, "https://example.webhook.office.com/x", null, NotificationType.PipelineFailure);
        var tender = Guid.CreateVersion7();
        var profile = Guid.CreateVersion7();
        var model = NotificationTemplateTests.SampleModel(NotificationType.NewMatch);

        var first = await Sut(Noon).DispatchAsync(NotificationType.NewMatch, model, tender, profile);

        first.Created.Should().Be(2, "email and Telegram want NewMatch; Teams does not");
        first.Deduplicated.Should().Be(0);
        _stored.Select(n => n.Channel).Should().BeEquivalentTo([NotificationChannel.Email, NotificationChannel.Telegram]);
        _stored.Should().AllSatisfy(n =>
        {
            n.Status.Should().Be(NotificationStatus.Pending);
            n.NotBefore.Should().BeNull();
            n.Subject.Should().StartWith("New match: WCGHCC0038/1/2025");
            n.PayloadJson.Should().Contain(model.TenderUrl);
        });
        _stored.Single(n => n.Channel == NotificationChannel.Email).Body.Should().Contain("<h2");
        _stored.Single(n => n.Channel == NotificationChannel.Telegram).Body.Should().Contain("<b>WCGHCC0038/1/2025</b>");
        _scheduler.Received(2).EnqueueNotificationSend(Arg.Any<Guid>());
        await _db.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());

        var second = await Sut(Noon).DispatchAsync(NotificationType.NewMatch, model, tender, profile);

        second.Created.Should().Be(0);
        second.Deduplicated.Should().Be(2);
        _stored.Should().HaveCount(2);
        _scheduler.Received(2).EnqueueNotificationSend(Arg.Any<Guid>());
    }

    [Fact]
    public async Task A_different_bucket_or_profile_is_a_new_message()
    {
        Prefer(NotificationChannel.Email, "jane@example.com", null, NotificationType.ClosingSoon3d);
        var tender = Guid.CreateVersion7();
        var model = NotificationTemplateTests.SampleModel(NotificationType.ClosingSoon3d);

        await Sut(Noon).DispatchAsync(NotificationType.ClosingSoon3d, model, tender, null, "2026-11-17");
        await Sut(Noon).DispatchAsync(NotificationType.ClosingSoon3d, model, tender, null, "2026-11-17");
        await Sut(Noon).DispatchAsync(NotificationType.ClosingSoon3d, model, tender, null, "2026-11-24");
        await Sut(Noon).DispatchAsync(NotificationType.ClosingSoon3d, model, tender, Guid.CreateVersion7(), "2026-11-24");

        _stored.Should().HaveCount(3);
    }

    [Fact]
    public async Task Quiet_hours_hold_the_message_until_the_window_ends_unless_it_is_urgent()
    {
        Prefer(NotificationChannel.Telegram, "123456", "{\"start\":\"22:00\",\"end\":\"06:00\"}", NotificationType.NewMatch, NotificationType.PipelineFailure);
        var model = NotificationTemplateTests.SampleModel(NotificationType.NewMatch);

        var held = await Sut(Night).DispatchAsync(NotificationType.NewMatch, model, Guid.CreateVersion7(), null);
        held.Deferred.Should().Be(1);
        _stored.Single().NotBefore.Should().Be(new DateTimeOffset(2026, 10, 24, 4, 0, 0, TimeSpan.Zero), "06:00 SAST the next morning");
        _scheduler.DidNotReceive().EnqueueNotificationSend(Arg.Any<Guid>());

        var urgent = await Sut(Night).DispatchAsync(NotificationType.NewMatch, model, Guid.CreateVersion7(), null, urgent: true);
        urgent.Deferred.Should().Be(0);
        _stored[1].NotBefore.Should().BeNull("a tender closing within two days bypasses quiet hours");
        _scheduler.Received(1).EnqueueNotificationSend(_stored[1].Id);

        var daytime = await Sut(Noon).DispatchAsync(NotificationType.NewMatch, model, Guid.CreateVersion7(), null);
        daytime.Deferred.Should().Be(0);
    }

    [Fact]
    public async Task Disabled_preferences_and_unwanted_types_get_nothing()
    {
        var disabled = Prefer(NotificationChannel.Email, "off@example.com", null, NotificationType.NewMatch);
        disabled.Update(disabled.Address, disabled.Types, false, null);
        Prefer(NotificationChannel.Email, "digest-only@example.com", null, NotificationType.DailyDigest);

        var result = await Sut(Noon).DispatchAsync(NotificationType.NewMatch, NotificationTemplateTests.SampleModel(NotificationType.NewMatch), Guid.CreateVersion7(), null);

        result.Created.Should().Be(0);
        _stored.Should().BeEmpty();
        await _db.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
    }
}
