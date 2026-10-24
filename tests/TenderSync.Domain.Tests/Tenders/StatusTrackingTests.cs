using TenderSync.Domain.Enums;
using TenderSync.Domain.Events;
using TenderSync.Domain.Tenders;

namespace TenderSync.Domain.Tests.Tenders;

/// <summary>WP11: cancellation patterns, release diffs producing the right events, and reminder buckets across SAST day boundaries.</summary>
public class StatusTrackingTests
{
    private static readonly DateTimeOffset Now = TestSnapshots.Now;

    private static Tender NewTender(TenderSnapshot snapshot)
    {
        var tender = Tender.CreateFromSnapshot(snapshot, Guid.CreateVersion7(), Province.WesternCape, TenderSourceKind.ETendersOcds, Now);
        tender.DeriveStatus(Now);
        tender.ClearDomainEvents();
        return tender;
    }

    [Theory]
    [InlineData("Cancellation E2696NTCSANEG", true)]
    [InlineData("CANCELLED: Supply of comfort packs", true)]
    [InlineData("Withdrawal of bid WCGHCC0038", true)]
    [InlineData("Supply and delivery of cancellation-proof locks", true)]
    [InlineData("Supply of comfort packs", false)]
    public void Titles_are_matched_loosely(string title, bool expected) =>
        CancellationDetector.IsCancellationTitle(title).Should().Be(expected);

    [Theory]
    [InlineData("Bidders please note that this tender has been cancelled.", true)]
    [InlineData("Notice of cancellation: RFQ 123 is withdrawn", true)]
    [InlineData("The request for quotation is no longer required.", true)]
    [InlineData("Supply of software. Cancellation fees apply to late withdrawals of orders.", false)]
    [InlineData("Maintenance of the cancellation module of the billing system", false)]
    public void Descriptions_are_matched_only_on_sentence_level_wording(string description, bool expected)
    {
        var snapshot = TestSnapshots.ComfortPacks(description: description);

        (CancellationDetector.Detect(snapshot) is not null).Should().Be(expected);
    }

    [Fact]
    public void A_cancellation_document_cancels_the_tender()
    {
        var snapshot = TestSnapshots.ComfortPacks(documents: [TestSnapshots.Document(), TestSnapshots.Document(id: "c1", title: "Cancellation notice WCGHCC0038-1-2025.pdf")]);

        CancellationDetector.Detect(snapshot).Should().StartWith("Cancellation document published: Cancellation notice");
    }

    [Fact]
    public void A_later_release_that_says_cancelled_cancels_the_tender_once_and_raises_the_status_event()
    {
        var tender = NewTender(TestSnapshots.ComfortPacks());
        var cancelled = TestSnapshots.ComfortPacks(description: "This bid has been cancelled by the department.");

        var diff = tender.ApplyRelease(cancelled, Province.WesternCape, Now.AddDays(1));
        tender.DeriveStatus(Now.AddDays(1));

        diff.DescriptionChanged.Should().BeTrue();
        tender.Status.Should().Be(TenderStatus.Cancelled);
        tender.StatusReason.Should().Contain("Description says cancelled");
        tender.DomainEvents.OfType<TenderStatusChanged>().Should().ContainSingle(e => e.To == TenderStatus.Cancelled);
        tender.DomainEvents.OfType<TenderChanged>().Should().ContainSingle();

        tender.ClearDomainEvents();
        tender.ApplyRelease(cancelled, Province.WesternCape, Now.AddDays(2));
        tender.DomainEvents.Should().BeEmpty("the same release again changes nothing");
        tender.StatusHistory.Count(h => h.ToStatus == TenderStatus.Cancelled).Should().Be(1);
    }

    [Fact]
    public void A_moved_closing_date_raises_one_change_event_with_the_old_and_new_dates()
    {
        var tender = NewTender(TenderWithClosing(Now.AddDays(10)));

        var diff = tender.ApplyRelease(TenderWithClosing(Now.AddDays(17)), Province.WesternCape, Now.AddHours(1));

        diff.ClosingChanged.Should().BeTrue();
        diff.OldClosingAt.Should().Be(Now.AddDays(10));
        diff.NewClosingAt.Should().Be(Now.AddDays(17));
        diff.NewDocumentIds.Should().BeEmpty();
        tender.DomainEvents.OfType<TenderChanged>().Should().ContainSingle();
        tender.DeriveStatus(Now.AddHours(1)).Should().Be(TenderStatus.Open);
    }

    [Fact]
    public void Awards_appearing_in_a_later_release_mark_the_tender_awarded()
    {
        var tender = NewTender(TestSnapshots.ComfortPacks());

        var diff = tender.ApplyRelease(TestSnapshots.ComfortPacks() with { HasAwards = true }, Province.WesternCape, Now.AddDays(30));
        tender.DeriveStatus(Now.AddDays(30));

        diff.AwardsPublished.Should().BeTrue();
        tender.Status.Should().Be(TenderStatus.Awarded);
        tender.DomainEvents.OfType<TenderStatusChanged>().Should().ContainSingle(e => e.To == TenderStatus.Awarded);
    }

    [Fact]
    public void Time_alone_moves_a_tender_through_closing_soon_to_closed_with_one_history_row_each()
    {
        var tender = NewTender(TenderWithClosing(Now.AddDays(10)));
        tender.Status.Should().Be(TenderStatus.Open);

        tender.DeriveStatus(Now.AddDays(4)).Should().Be(TenderStatus.ClosingSoon);
        tender.DeriveStatus(Now.AddDays(5)).Should().Be(TenderStatus.ClosingSoon);
        tender.DeriveStatus(Now.AddDays(11)).Should().Be(TenderStatus.Closed);

        tender.StatusHistory.Select(h => h.ToStatus).Should().Equal(TenderStatus.Open, TenderStatus.ClosingSoon, TenderStatus.Closed);
    }

    private static TenderSnapshot TenderWithClosing(DateTimeOffset closing) => TestSnapshots.ComfortPacks(closingAt: closing);

    // ---- reminder buckets (SAST = UTC+2) ----

    private static DateTimeOffset Sast(int day, int hour, int minute = 0) => new(2026, 11, day, hour, minute, 0, TimeSpan.FromHours(2));

    [Theory]
    [InlineData(20, 23, 30, 1)] // 23:30 SAST the evening before: tomorrow is 1 day away
    [InlineData(21, 0, 30, 0)] // 00:30 SAST on the day: 0 days
    [InlineData(20, 6, 0, 1)]
    [InlineData(18, 6, 0, 3)]
    [InlineData(14, 6, 0, 7)]
    [InlineData(13, 23, 59, 8)]
    public void Days_until_counts_south_african_calendar_days(int day, int hour, int minute, int expected) =>
        ReminderBuckets.DaysUntil(Sast(21, 11), Sast(day, hour, minute)).Should().Be(expected);

    [Fact]
    public void Utc_inputs_give_the_same_answer_as_sast_inputs()
    {
        // 22:30 UTC on the 20th is 00:30 SAST on the 21st: closing at 11:00 SAST the 21st is "today".
        ReminderBuckets.DaysUntil(Sast(21, 11), new DateTimeOffset(2026, 11, 20, 22, 30, 0, TimeSpan.Zero)).Should().Be(0);
        ReminderBuckets.DaysUntil(Sast(21, 11), new DateTimeOffset(2026, 11, 20, 21, 30, 0, TimeSpan.Zero)).Should().Be(1);
    }

    [Theory]
    [InlineData(21, 10, NotificationType.ClosingSoon1d)] // one hour before closing, still open
    [InlineData(20, 6, NotificationType.ClosingSoon1d)]
    [InlineData(19, 6, NotificationType.ClosingSoon3d)]
    [InlineData(18, 6, NotificationType.ClosingSoon3d)]
    [InlineData(17, 6, NotificationType.ClosingSoon7d)]
    [InlineData(14, 6, NotificationType.ClosingSoon7d)]
    [InlineData(13, 6, null)]
    [InlineData(21, 12, null)] // closed an hour ago
    public void Closing_buckets_are_7_3_and_1_days(int day, int hour, NotificationType? expected) =>
        ReminderBuckets.Closing(Sast(21, 11), Sast(day, hour)).Should().Be(expected);

    [Fact]
    public void Briefing_reminders_and_date_buckets()
    {
        ReminderBuckets.BriefingDue(Sast(21, 10), Sast(19, 6)).Should().BeTrue("two days ahead");
        ReminderBuckets.BriefingDue(Sast(21, 10), Sast(18, 6)).Should().BeFalse("three days ahead");
        ReminderBuckets.BriefingDue(Sast(21, 10), Sast(21, 11)).Should().BeFalse("already started");
        ReminderBuckets.DateBucket(new DateTimeOffset(2026, 11, 20, 22, 30, 0, TimeSpan.Zero)).Should().Be("20261121", "the South African date, not the UTC one");
    }
}
