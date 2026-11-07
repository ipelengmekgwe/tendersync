using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using TenderSync.Application.Abstractions.Jobs;
using TenderSync.Application.Abstractions.Persistence;
using TenderSync.Application.Common.Events;
using TenderSync.Application.Features.Requirements.EventHandlers;
using TenderSync.Application.Tests.Documents;
using TenderSync.Domain.Events;
using TenderSync.Domain.Tenders;

namespace TenderSync.Application.Tests.Requirements;

/// <summary>Requirement extraction starts when the last document of a matched tender is done, whether it got text or not.</summary>
public class ExtractionTriggerTests
{
    private static readonly DateTimeOffset Now = DocumentTestData.Now;

    private readonly IAppDbContext _db = Substitute.For<IAppDbContext>();
    private readonly IPipelineScheduler _scheduler = Substitute.For<IPipelineScheduler>();

    private ExtractWhenDocumentsAreReady Sut(Tender tender, bool matched = true)
    {
        _db.Tenders.GetWithDetailsAsync(tender.Id, Arg.Any<CancellationToken>()).Returns(tender);
        _db.TenderMatches.ListByTenderAsync(tender.Id, Arg.Any<CancellationToken>())
            .Returns(matched ? [TenderMatch.Create(tender.Id, Guid.CreateVersion7(), 80, Now)] : []);
        return new ExtractWhenDocumentsAreReady(_db, _scheduler, NullLogger<ExtractWhenDocumentsAreReady>.Instance);
    }

    private static Tender TenderWithTwoDocuments() =>
        DocumentTestData.TenderWith(DocumentTestData.Document("a", "Bid Document.pdf", "pdf"), DocumentTestData.Document("b", "Annexure.pdf", "pdf"));

    private static void Extract(TenderDocument document)
    {
        document.MarkDownloaded($"tenders/x/{document.ExternalId}.pdf", new string('a', 64), 10, Now);
        document.MarkExtracted(3, false, Now);
    }

    private static DomainEventNotification<DocumentNotExtracted> Failed(TenderDocument document) =>
        new(new DocumentNotExtracted(document.TenderId, document.Id, document.Status, document.Error!, Now));

    [Fact]
    public async Task A_document_that_fails_after_the_others_have_text_still_starts_extraction()
    {
        var tender = TenderWithTwoDocuments();
        var docs = tender.Documents.ToList();
        Extract(docs[0]);
        var sut = Sut(tender);

        // The first document's event arrived while the second was still downloading, so it waited.
        await sut.Handle(new DomainEventNotification<DocumentTextExtracted>(new DocumentTextExtracted(tender.Id, docs[0].Id, 3, false, Now)), CancellationToken.None);
        _scheduler.DidNotReceive().EnqueueRequirementExtraction(Arg.Any<Guid>());

        docs[1].MarkFailed("The server returned an empty body.", Now);
        await sut.Handle(Failed(docs[1]), CancellationToken.None);

        _scheduler.Received(1).EnqueueRequirementExtraction(tender.Id);
    }

    [Fact]
    public async Task Nothing_starts_while_a_document_is_in_flight_or_when_no_document_has_text()
    {
        var tender = TenderWithTwoDocuments();
        var docs = tender.Documents.ToList();
        docs[0].MarkFailed("timeout", Now);
        var sut = Sut(tender);

        await sut.Handle(Failed(docs[0]), CancellationToken.None);
        docs[1].MarkSkipped("audio", Now);
        await sut.Handle(Failed(docs[1]), CancellationToken.None);

        _scheduler.DidNotReceive().EnqueueRequirementExtraction(Arg.Any<Guid>());
    }

    [Fact]
    public void Failed_and_skipped_documents_raise_the_event_the_trigger_listens_to()
    {
        var tender = TenderWithTwoDocuments();
        var docs = tender.Documents.ToList();

        docs[0].MarkFailed("timeout", Now);
        docs[1].MarkSkipped("audio", Now);

        docs[0].DomainEvents.OfType<DocumentNotExtracted>().Should().ContainSingle(e => e.Reason == "timeout");
        docs[1].DomainEvents.OfType<DocumentNotExtracted>().Should().ContainSingle(e => e.Status == Domain.Enums.DocumentStatus.Skipped);
    }

    [Fact]
    public async Task Unmatched_tenders_are_ignored()
    {
        var tender = TenderWithTwoDocuments();
        var docs = tender.Documents.ToList();
        Extract(docs[0]);
        docs[1].MarkFailed("timeout", Now);

        await Sut(tender, matched: false).Handle(Failed(docs[1]), CancellationToken.None);

        _scheduler.DidNotReceive().EnqueueRequirementExtraction(Arg.Any<Guid>());
    }
}
