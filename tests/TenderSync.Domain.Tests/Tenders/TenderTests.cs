using TenderSync.Domain.Enums;
using TenderSync.Domain.Events;
using TenderSync.Domain.Tenders;

namespace TenderSync.Domain.Tests.Tenders;

public class TenderTests
{
    private static readonly DateTimeOffset Now = TestSnapshots.Now;

    private static Tender NewTender(TenderSnapshot? snapshot = null) =>
        Tender.CreateFromSnapshot(snapshot ?? TestSnapshots.ComfortPacks(), Guid.CreateVersion7(), Province.WesternCape, TenderSourceKind.ETendersOcds, Now);

    [Fact]
    public void Creating_from_a_snapshot_maps_fields_documents_and_release()
    {
        var tender = NewTender();

        tender.Ocid.Should().Be("ocds-9t57fa-175445");
        tender.BidNumber.Should().Be("WCGHCC0038/1/2025");
        tender.Title.Should().StartWith("Supplementary bid");
        tender.Province.Should().Be(Province.WesternCape);
        tender.ValueAmount.Should().BeNull("the feed sends 0 which means unknown");
        tender.Documents.Should().ContainSingle(d => d.Format == "pdf" && d.Status == DocumentStatus.Pending);
        tender.Releases.Should().ContainSingle(r => r.Tag == "compiled");
        tender.ContentHash.Should().HaveLength(64);
        tender.FirstSeenAt.Should().Be(Now);
        tender.DomainEvents.Should().ContainSingle(e => e is TenderFirstSeen);
    }

    [Fact]
    public void Applying_an_identical_release_is_a_no_op_apart_from_last_seen()
    {
        var tender = NewTender();
        tender.ClearDomainEvents();
        var later = Now.AddHours(2);

        var diff = tender.ApplyRelease(TestSnapshots.ComfortPacks(releaseId: "different-release-id"), Province.WesternCape, later);

        diff.Should().Be(ReleaseDiff.Unchanged);
        tender.LastSeenAt.Should().Be(later);
        tender.Releases.Should().HaveCount(1, "the release id is volatile and must not create a new row");
        tender.DomainEvents.Should().BeEmpty();
    }

    [Fact]
    public void Applying_a_release_with_a_moved_closing_date_records_the_change()
    {
        var tender = NewTender();
        tender.ClearDomainEvents();
        var newClosing = new DateTimeOffset(2026, 12, 4, 11, 0, 0, TimeSpan.Zero);

        var diff = tender.ApplyRelease(TestSnapshots.ComfortPacks(closingAt: newClosing), Province.WesternCape, Now.AddDays(1));

        diff.IsNew.Should().BeFalse();
        diff.ClosingChanged.Should().BeTrue();
        diff.OldClosingAt.Should().Be(new DateTimeOffset(2026, 11, 20, 11, 0, 0, TimeSpan.Zero));
        diff.NewClosingAt.Should().Be(newClosing);
        tender.ClosingAt.Should().Be(newClosing);
        tender.Releases.Should().HaveCount(2);
        tender.DomainEvents.OfType<TenderChanged>().Should().ContainSingle().Which.Diff.ClosingChanged.Should().BeTrue();
    }

    [Fact]
    public void Applying_a_release_with_an_added_document_lists_it_and_keeps_existing_documents()
    {
        var tender = NewTender();
        tender.ClearDomainEvents();
        var addendum = TestSnapshots.Document(id: "addendum-1", title: "Addendum 1.pdf");

        var diff = tender.ApplyRelease(TestSnapshots.ComfortPacks(documents: [TestSnapshots.Document(), addendum]), Province.WesternCape, Now.AddDays(1));

        diff.NewDocumentIds.Should().Equal("addendum-1");
        diff.RemovedDocumentIds.Should().BeEmpty();
        tender.Documents.Should().HaveCount(2);
    }

    [Fact]
    public void A_re_published_document_goes_back_to_pending()
    {
        var tender = NewTender();
        var document = tender.Documents.Single();
        document.MarkDownloaded("tenders/x/doc.pdf", new string('a', 64), 1024, Now);
        document.Status.Should().Be(DocumentStatus.Downloaded);

        tender.ApplyRelease(TestSnapshots.ComfortPacks(documents: [TestSnapshots.Document(modified: Now.AddDays(1))]), Province.WesternCape, Now.AddDays(1));

        tender.Documents.Single().Status.Should().Be(DocumentStatus.Pending);
    }

    [Fact]
    public void Applying_a_release_for_another_ocid_is_rejected()
    {
        var tender = NewTender();
        var other = TestSnapshots.ComfortPacks() with { Ocid = "ocds-9t57fa-999999" };

        var act = () => tender.ApplyRelease(other, Province.WesternCape, Now);

        act.Should().Throw<Common.DomainException>();
    }

    [Theory]
    [InlineData(null, TenderStatus.Unknown)]
    [InlineData(30, TenderStatus.Open)]
    [InlineData(7, TenderStatus.ClosingSoon)]
    [InlineData(1, TenderStatus.ClosingSoon)]
    [InlineData(-1, TenderStatus.Closed)]
    public void Status_is_derived_from_the_closing_date(int? daysFromNow, TenderStatus expected)
    {
        var tender = NewTender(daysFromNow is null
            ? TestSnapshots.ComfortPacks(noClosing: true)
            : TestSnapshots.ComfortPacks(closingAt: Now.AddDays(daysFromNow.Value)));
        tender.ClearDomainEvents();

        var status = tender.DeriveStatus(Now);

        status.Should().Be(expected);
        tender.Status.Should().Be(expected);
        if (expected != TenderStatus.Unknown)
        {
            tender.StatusHistory.Should().ContainSingle(h => h.FromStatus == TenderStatus.Unknown && h.ToStatus == expected);
            tender.DomainEvents.Should().ContainSingle(e => e is TenderStatusChanged);
        }
    }

    [Fact]
    public void Deriving_status_twice_does_not_duplicate_history()
    {
        var tender = NewTender(TestSnapshots.ComfortPacks(closingAt: Now.AddDays(30)));

        tender.DeriveStatus(Now);
        tender.DeriveStatus(Now.AddHours(1));

        tender.StatusHistory.Should().HaveCount(1);
    }

    [Fact]
    public void Cancelled_and_awarded_are_sticky()
    {
        var tender = NewTender(TestSnapshots.ComfortPacks(closingAt: Now.AddDays(30)));
        tender.MarkCancelled("Cancellation notice published", Now);

        tender.DeriveStatus(Now.AddDays(1)).Should().Be(TenderStatus.Cancelled);

        var awarded = NewTender(TestSnapshots.ComfortPacks(closingAt: Now.AddDays(-30), hasAwards: true));
        awarded.DeriveStatus(Now).Should().Be(TenderStatus.Awarded);
        awarded.StatusReason.Should().Be("awards published");
    }

    [Fact]
    public void Awards_appearing_in_a_later_release_are_flagged_in_the_diff()
    {
        var tender = NewTender();

        var diff = tender.ApplyRelease(TestSnapshots.ComfortPacks(hasAwards: true), Province.WesternCape, Now.AddDays(40));

        diff.AwardsPublished.Should().BeTrue();
        tender.HasAwards.Should().BeTrue();
    }
}
