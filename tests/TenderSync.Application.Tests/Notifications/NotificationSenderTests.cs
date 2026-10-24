using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NSubstitute;
using TenderSync.Application.Abstractions;
using TenderSync.Application.Abstractions.Notifications;
using TenderSync.Application.Abstractions.Persistence;
using TenderSync.Application.Features.Notifications;
using TenderSync.Domain.Enums;
using TenderSync.Domain.Notifications;

namespace TenderSync.Application.Tests.Notifications;

public class NotificationSenderTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 23, 10, 0, 0, TimeSpan.Zero);

    private readonly IAppDbContext _db = Substitute.For<IAppDbContext>();
    private readonly IDateTimeProvider _clock = Substitute.For<IDateTimeProvider>();
    private readonly FakeNotificationChannel _email = new(NotificationChannel.Email);
    private readonly List<Notification> _rows = [];

    private NotificationSender Sut(DateTimeOffset? now = null)
    {
        _clock.UtcNow.Returns(now ?? Now);
        _db.Notifications.GetByIdAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>()).Returns(call => _rows.FirstOrDefault(n => n.Id == call.Arg<Guid>()));
        _db.Notifications.GetDueAsync(Arg.Any<DateTimeOffset>(), Arg.Any<DateTimeOffset>(), Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(call => _rows.Where(n => n.IsDue(call.ArgAt<DateTimeOffset>(0))).ToList());
        return new NotificationSender(_db, [_email], Options.Create(new NotificationOptions()), _clock, NullLogger<NotificationSender>.Instance);
    }

    private Notification Row(NotificationChannel channel = NotificationChannel.Email, DateTimeOffset? notBefore = null)
    {
        var row = Notification.Create(NotificationType.NewMatch, channel, "jane@example.com", "Subject", "<p>Body</p>", Guid.CreateVersion7(), null, null,
            "{\"actionUrl\":\"http://localhost:3000/tenders/1\",\"actionLabel\":\"Open tender\"}", notBefore);
        _rows.Add(row);
        return row;
    }

    [Fact]
    public async Task A_successful_send_marks_the_row_sent_and_passes_the_action_link()
    {
        var row = Row();

        var outcome = await Sut().SendAsync(row.Id);

        outcome.Status.Should().Be(NotificationStatus.Sent);
        row.SentAt.Should().Be(Now);
        row.Attempts.Should().Be(1);
        _email.Sent.Should().ContainSingle().Which.Should().Be(new NotificationMessage("jane@example.com", "Subject", "<p>Body</p>", "http://localhost:3000/tenders/1", "Open tender"));
        await _db.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task A_failed_send_stays_pending_with_a_backoff_and_a_thrown_exception_counts_as_a_failure()
    {
        var row = Row();
        _email.Respond = _ => NotificationSendResult.Failed("550 mailbox unavailable");

        (await Sut().SendAsync(row.Id)).Status.Should().Be(NotificationStatus.Pending);
        row.Error.Should().Be("550 mailbox unavailable");
        row.NotBefore.Should().Be(Now.AddMinutes(1));

        _email.Respond = _ => throw new InvalidOperationException("socket closed");
        (await Sut(Now.AddMinutes(1)).SendAsync(row.Id)).Status.Should().Be(NotificationStatus.Pending);
        row.Attempts.Should().Be(2);
        row.Error.Should().Contain("socket closed");
    }

    [Fact]
    public async Task Rows_that_are_not_due_or_not_pending_are_left_alone()
    {
        var held = Row(notBefore: Now.AddHours(1));
        var sent = Row();
        sent.MarkSent(Now);

        (await Sut().SendAsync(held.Id)).Attempted.Should().BeFalse();
        (await Sut().SendAsync(sent.Id)).Attempted.Should().BeFalse();
        _email.Sent.Should().BeEmpty();
    }

    [Fact]
    public async Task Missing_or_unconfigured_channels_skip_the_row_instead_of_retrying()
    {
        var noChannel = Row(NotificationChannel.Teams);
        (await Sut().SendAsync(noChannel.Id)).Status.Should().Be(NotificationStatus.Skipped);
        noChannel.Error.Should().Contain("No Teams channel");

        var unconfigured = Row();
        _email.Respond = _ => NotificationSendResult.Skip("Email is not configured");
        (await Sut().SendAsync(unconfigured.Id)).Status.Should().Be(NotificationStatus.Skipped);
        unconfigured.Error.Should().Be("Email is not configured");
    }

    [Fact]
    public async Task The_sweep_sends_everything_due_and_nothing_else()
    {
        Row();
        Row(notBefore: Now.AddMinutes(-1));
        Row(notBefore: Now.AddMinutes(5));
        var done = Row();
        done.MarkSent(Now);

        var attempted = await Sut().SendDueAsync();

        attempted.Should().Be(2);
        _email.Sent.Should().HaveCount(2);
    }

    [Fact]
    public void Recipients_are_masked_in_logs_and_listings()
    {
        NotificationSender.Mask("jane.doe@example.com").Should().Be("ja***@example.com");
        NotificationSender.Mask("+27821234567").Should().Be("***4567");
        NotificationSender.Mask("12").Should().Be("***");
    }
}
