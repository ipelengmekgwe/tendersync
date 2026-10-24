using TenderSync.Application.Features.Notifications.EventHandlers;
using TenderSync.Application.Tests.Documents;
using TenderSync.Domain.Tenders;

namespace TenderSync.Application.Tests.Notifications;

public class NotificationTriggerTests
{
    [Fact]
    public void A_release_diff_is_described_in_one_sentence_and_hashed_into_a_stable_bucket()
    {
        var tender = DocumentTestData.TenderWith(DocumentTestData.Document("d0", "Addendum 1.pdf", "pdf"), DocumentTestData.Document("d1", "Bid Document.pdf", "pdf"));
        var old = new DateTimeOffset(2026, 11, 20, 9, 0, 0, TimeSpan.Zero);
        var diff = new ReleaseDiff(false, ["d0"], [], [], old, old.AddDays(7), false, false, false);

        var text = NotifyOnTenderChanged.Describe(diff, tender);

        text.Should().Be("1 new document: Addendum 1.pdf; closing date moved from Fri 20 Nov 2026 11:00 SAST to Fri 27 Nov 2026 11:00 SAST.");
        NotifyOnTenderChanged.Bucket(diff).Should().HaveLength(12).And.Be(NotifyOnTenderChanged.Bucket(diff with { }));
        NotifyOnTenderChanged.Bucket(diff).Should().NotBe(NotifyOnTenderChanged.Bucket(diff with { NewDocumentIds = ["d1"] }));
        NotifyOnTenderChanged.Describe(ReleaseDiff.Unchanged, tender).Should().Be("The release was updated.");
    }
}
