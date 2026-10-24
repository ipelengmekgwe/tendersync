using TenderSync.Domain.Common;
using TenderSync.Domain.Enums;
using TenderSync.Domain.Events;
using TenderSync.Domain.Tenders;

namespace TenderSync.Domain.Tests.Tenders;

public class TenderDocumentTests
{
    private static readonly DateTimeOffset Now = TestSnapshots.Now;

    private static Tender TenderWithDocuments(params TenderDocumentSnapshot[] documents) =>
        Tender.CreateFromSnapshot(TestSnapshots.ComfortPacks(documents: documents), Guid.CreateVersion7(), Province.WesternCape, TenderSourceKind.ETendersOcds, Now);

    [Fact]
    public void A_document_moves_from_pending_through_downloaded_to_extracted()
    {
        var tender = TenderWithDocuments(TestSnapshots.Document());
        var document = tender.Documents.Single();

        document.MarkDownloaded("tenders/ocds-9t57fa-175445/x.pdf", new string('a', 64), 1234, Now);
        document.Status.Should().Be(DocumentStatus.Downloaded);
        document.Sha256.Should().HaveLength(64);
        document.SizeBytes.Should().Be(1234);
        document.DownloadedAt.Should().Be(Now);

        document.MarkExtracted(pageCount: 3, needsOcr: true, Now.AddMinutes(1));
        document.Status.Should().Be(DocumentStatus.Extracted);
        document.PageCount.Should().Be(3);
        document.NeedsOcr.Should().BeTrue();
        document.ExtractedAt.Should().Be(Now.AddMinutes(1));
    }

    [Fact]
    public void Extraction_requires_a_downloaded_file()
    {
        var tender = TenderWithDocuments(TestSnapshots.Document());
        var document = tender.Documents.Single();

        var act = () => document.MarkExtracted(1, false, Now);

        act.Should().Throw<DomainException>();
    }

    [Fact]
    public void A_republished_document_goes_back_to_pending()
    {
        var tender = TenderWithDocuments(TestSnapshots.Document());
        var document = tender.Documents.Single();
        document.MarkDownloaded("tenders/x/y.pdf", new string('a', 64), 10, Now);
        document.MarkExtracted(2, false, Now);

        tender.ApplyRelease(TestSnapshots.ComfortPacks(documents: [TestSnapshots.Document(modified: Now.AddDays(1))], releaseId: "r2"), Province.WesternCape, Now.AddDays(1));

        document.Status.Should().Be(DocumentStatus.Pending, "a changed file must be downloaded and extracted again");
        document.StoragePath.Should().NotBeNull("the previous file stays until it is replaced");
    }

    [Fact]
    public void Requesting_a_download_queues_failed_and_skipped_documents_but_not_downloaded_ones()
    {
        var tender = TenderWithDocuments(
            TestSnapshots.Document(id: "a", title: "a.pdf"),
            TestSnapshots.Document(id: "b", title: "b.pdf"),
            TestSnapshots.Document(id: "c", title: "c.pdf"),
            TestSnapshots.Document(id: "d", title: "d.mp3"));
        var docs = tender.Documents.ToDictionary(d => d.ExternalId, StringComparer.Ordinal);
        docs["a"].MarkDownloaded("tenders/x/a.pdf", new string('a', 64), 10, Now);
        docs["b"].MarkFailed("boom");
        docs["d"].MarkSkipped("audio");
        tender.ClearDomainEvents();

        var queued = tender.RequestDocumentDownload(Now);

        queued.Should().Be(3);
        docs["a"].Status.Should().Be(DocumentStatus.Downloaded);
        docs["b"].Status.Should().Be(DocumentStatus.Pending);
        docs["b"].Error.Should().BeNull();
        docs["c"].Status.Should().Be(DocumentStatus.Pending);
        docs["d"].Status.Should().Be(DocumentStatus.Pending);
        tender.DomainEvents.OfType<TenderDocumentsRequested>().Should().ContainSingle().Which.Should().Match<TenderDocumentsRequested>(e => e.TenderId == tender.Id && e.PendingDocuments == 3);
    }
}
