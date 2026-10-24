using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NSubstitute;
using TenderSync.Application.Abstractions;
using TenderSync.Application.Abstractions.Llm;
using TenderSync.Application.Abstractions.Persistence;
using TenderSync.Application.Features.Llm;
using TenderSync.Application.Features.Requirements;
using TenderSync.Application.Tests.Documents;
using TenderSync.Domain.Enums;
using TenderSync.Domain.Events;
using TenderSync.Domain.Tenders;

namespace TenderSync.Application.Tests.Requirements;

/// <summary>Document selection, native-vs-text input, persistence of the merged result and the tender columns, with the fake model.</summary>
public class RequirementExtractorTests
{
    private static readonly DateTimeOffset Now = DocumentTestData.Now;

    private readonly IAppDbContext _db = Substitute.For<IAppDbContext>();
    private readonly MemoryDocumentStore _store = new();
    private readonly FakeLlmClient _llm = new();
    private readonly List<TenderRequirement> _written = [];
    private readonly Dictionary<Guid, List<DocumentText>> _texts = [];

    private RequirementExtractor Sut(Tender tender, ExtractionOptions? options = null)
    {
        _db.Tenders.GetWithDetailsAsync(tender.Id, Arg.Any<CancellationToken>()).Returns(tender);
        _db.Tenders.GetDocumentTextsAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>()).Returns(call => _texts.GetValueOrDefault(call.Arg<Guid>(), []));
        _db.Tenders.ReplaceRequirementsAsync(tender.Id, Arg.Do<IEnumerable<TenderRequirement>>(r => { _written.Clear(); _written.AddRange(r); }), Arg.Any<CancellationToken>()).Returns(Task.CompletedTask);
        var prompts = Substitute.For<IPromptRegistry>();
        prompts.Get("extract-requirements", null).Returns(new PromptTemplate("extract-requirements", 1, ModelTier.Strong, "TenderRequirementsV1", null, "Extract."));
        var clock = Substitute.For<IDateTimeProvider>();
        clock.UtcNow.Returns(Now);
        return new RequirementExtractor(_db, _store, _llm, prompts, Options.Create(options ?? new ExtractionOptions()), clock, NullLogger<RequirementExtractor>.Instance);
    }

    private async Task<TenderDocument> AddDocumentAsync(Tender tender, string title, string format, int pages, long size, bool needsOcr = false, params string[] pageTexts)
    {
        var document = tender.Documents.Single(d => d.Title == title);
        var path = $"tenders/{tender.Ocid}/{document.Id}.{format}";
        await _store.SaveAsync(path, new MemoryStream(format == "pdf" ? DocumentTestData.PdfBytes : DocumentTestData.ZipBytes), null);
        document.MarkDownloaded(path, new string('a', 64), size, Now);
        document.MarkExtracted(pages, needsOcr, Now);
        _texts[document.Id] = pageTexts.Select((t, i) => new DocumentText(document.Id, i + 1, t)).ToList();
        return document;
    }

    private static Tender TenderWith(params string[] titles) =>
        DocumentTestData.TenderWith(titles.Select((t, i) => DocumentTestData.Document($"d{i}", t, Path.GetExtension(t).TrimStart('.'))).ToArray());

    private static TenderRequirementsV1 Answer(string summary, params MandatoryRequirementV1[] mandatory) =>
        new(summary, mandatory, [], null, null, [], null, null, null, null, null);

    [Fact]
    public async Task Small_pdfs_go_natively_with_citations_and_the_merged_result_is_persisted()
    {
        var tender = TenderWith("Bid Document.pdf", "Annexure B Pricing.xlsx");
        var main = await AddDocumentAsync(tender, "Bid Document.pdf", "pdf", 12, 500_000, false, "INVITATION TO BID", "Closing 20 November 2026 11:00", "CSD registration is mandatory");
        var pricing = await AddDocumentAsync(tender, "Annexure B Pricing.xlsx", "xlsx", 1, 20_000, false, "# Sheet: Pricing\nItem\tPrice");
        _llm.Answer("extract-requirements", request =>
        {
            var doc = request.UserParts.OfType<LlmDocumentPart>().Single();
            return doc.MediaType == "application/pdf"
                ? new TenderRequirementsV1("Supply of comfort packs.", [new(RequirementType.CsdRegistration, "Registered on the CSD", null, new(3, "CSD registration is mandatory"), 0.95)], [new("Experience", 40, null, null, new(5, "Experience 40 points"), 0.8)], 70, "80/20",
                    [new("Preference points claim form", "SBD 6.1", true, null, new(8, "SBD 6.1"), 0.9)],
                    new BriefingV1("2026-11-03T10:00:00+02:00", true, "Boardroom A", null, new(2, "compulsory briefing"), 0.9),
                    new SubmissionV1(SubmissionMethod.Physical, "Tender box, 4 Dorp Street", "2026-11-20T11:00:00+02:00", "two copies", new(2, "Closing 20 November 2026 11:00"), 0.9),
                    new PricingV1(PricingBasis.FixedPrice, "ZAR", "inclusive of VAT", 90, [], new(10, "valid for 90 days"), 0.7),
                    new ContractV1(36, null, "optional 12 months", new(1, "36 months"), 0.8),
                    new ContactsV1("scm@wcg.gov.za", "021 000 0000"))
                : Answer(string.Empty, new MandatoryRequirementV1(RequirementType.Other, "Price all items", null, new(1, "Item\tPrice"), 0.4));
        });
        _llm.CostUsd = 0.30m;
        _llm.Model = "claude-opus-5-5";

        var outcome = await Sut(tender).ExtractAsync(tender.Id);

        outcome.DocumentsProcessed.Should().Be(2);
        outcome.CostUsd.Should().Be(0.60m);
        outcome.Model.Should().Be("claude-opus-5-5");

        _llm.Requests.Should().HaveCount(2);
        var first = _llm.Requests[0];
        first.Tier.Should().Be(ModelTier.Strong);
        first.EnableCitations.Should().BeTrue();
        first.TenderId.Should().Be(tender.Id);
        var pdfPart = first.UserParts.OfType<LlmDocumentPart>().Single();
        pdfPart.MediaType.Should().Be("application/pdf", "the main document is first and small enough for the native path");
        pdfPart.Content.ToArray().Should().Equal(DocumentTestData.PdfBytes);
        pdfPart.ExtractedText.Should().Contain("[page 2]\nClosing 20 November 2026 11:00");
        first.UserParts.OfType<LlmTextPart>().Single().Text.Should().Contain("Bid Document.pdf (12 pages)").And.Contain("WCGHCC0038/1/2025");
        _llm.Requests[1].UserParts.OfType<LlmDocumentPart>().Single().MediaType.Should().Be("text/plain", "spreadsheets go as extracted text");

        _written.Select(r => r.Kind).Should().BeEquivalentTo([RequirementKind.Mandatory, RequirementKind.Mandatory, RequirementKind.Functional, RequirementKind.Evaluation, RequirementKind.Document, RequirementKind.Briefing, RequirementKind.Submission, RequirementKind.Pricing, RequirementKind.Contract, RequirementKind.Other]);
        var csd = _written.Single(r => r.Type == RequirementType.CsdRegistration);
        csd.Source.Should().Be(new RequirementSource(main.Id, 3, "CSD registration is mandatory"));
        csd.NeedsVerification.Should().BeFalse();
        csd.Model.Should().Be("claude-opus-5-5");
        csd.PromptVersion.Should().Be("v1");
        _written.Single(r => r.Kind == RequirementKind.Evaluation).Threshold.Should().Be(70);
        _written.Single(r => r.Kind == RequirementKind.Document).Text.Should().Be("SBD 6.1: Preference points claim form");
        _written.Single(r => r.Text == "Price all items").Should().Match<TenderRequirement>(r => r.NeedsVerification && r.Source!.DocumentId == pricing.Id, "confidence 0.4 is below the verify line");
        _written.Single(r => r.Kind == RequirementKind.Other).ValueJson.Should().Contain("scm@wcg.gov.za");
        outcome.Requirements.Should().Be(_written.Count);
        outcome.NeedsVerification.Should().Be(3, "the low-confidence pricing item, plus the evaluation and contact rows, which have no page of their own");

        tender.Summary.Should().Be("Supply of comfort packs.");
        tender.BriefingAt.Should().Be(new DateTimeOffset(2026, 11, 3, 10, 0, 0, TimeSpan.FromHours(2)));
        tender.BriefingCompulsory.Should().BeTrue();
        tender.BriefingVenue.Should().Be("Boardroom A");
        tender.SubmissionMethod.Should().Be(SubmissionMethod.Physical);
        tender.DomainEvents.OfType<RequirementsExtracted>().Should().ContainSingle().Which.RequirementCount.Should().Be(_written.Count);
        await _db.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Large_or_scanned_pdfs_go_as_text_and_documents_without_text_are_skipped()
    {
        var tender = TenderWith("Bid Document.pdf", "Scan.pdf", "Empty.pdf", "Pending.pdf");
        await AddDocumentAsync(tender, "Bid Document.pdf", "pdf", 700, 1_000, false, "[big]");
        await AddDocumentAsync(tender, "Scan.pdf", "pdf", 3, 1_000, true, "", "", "a few chars");
        await AddDocumentAsync(tender, "Empty.pdf", "pdf", 2, 1_000, false, "", "");
        _llm.Answer("extract-requirements", Answer("Something."));

        var outcome = await Sut(tender).ExtractAsync(tender.Id);

        outcome.DocumentsProcessed.Should().Be(3, "the big PDF and the scan go as text, the small text-less PDF goes natively so the model can read it; the pending one is not extracted");
        outcome.DocumentsSkipped.Should().Be(1);
        _llm.Requests.Select(r => r.UserParts.OfType<LlmDocumentPart>().Single().MediaType).Should().Equal("text/plain", "text/plain", "application/pdf");
        _llm.Requests[2].UserParts.OfType<LlmDocumentPart>().Single().ExtractedText.Should().BeNull();
        _llm.Requests[0].UserParts.OfType<LlmDocumentPart>().Single().ExtractedText.Should().StartWith("[page 1]\n[big]");
        _llm.Requests[1].UserParts.OfType<LlmDocumentPart>().Single().ExtractedText.Should().Be("[page 3]\na few chars\n\n", "empty pages are left out of the marker text");
    }

    [Fact]
    public async Task The_main_document_is_chosen_by_title_then_by_size_and_the_document_cap_applies()
    {
        var tender = TenderWith("Annexure A.pdf", "Terms of Reference.pdf", "Annexure C.pdf");
        await AddDocumentAsync(tender, "Annexure A.pdf", "pdf", 40, 1_000, false, "a");
        await AddDocumentAsync(tender, "Terms of Reference.pdf", "pdf", 5, 1_000, false, "tor");
        await AddDocumentAsync(tender, "Annexure C.pdf", "pdf", 10, 1_000, false, "c");

        var ordered = RequirementExtractor.OrderDocuments(tender, new ExtractionOptions { MaxDocumentsPerTender = 2 });

        ordered.Select(d => d.Title).Should().Equal("Terms of Reference.pdf", "Annexure A.pdf");
    }

    [Fact]
    public async Task A_tender_without_extracted_text_fails_clearly_and_a_failing_document_does_not_stop_the_others()
    {
        var none = TenderWith("Pending.pdf");
        await FluentActions.Awaiting(() => Sut(none).ExtractAsync(none.Id)).Should().ThrowAsync<LlmException>().WithMessage("*no document with extracted text*");

        var tender = TenderWith("Bid Document.pdf", "Annexure.pdf");
        await AddDocumentAsync(tender, "Bid Document.pdf", "pdf", 2, 1_000, false, "main");
        await AddDocumentAsync(tender, "Annexure.pdf", "pdf", 1, 1_000, false, "annex");
        _llm.Answer("extract-requirements", request => request.UserParts.OfType<LlmDocumentPart>().Single().Title == "Annexure.pdf"
            ? throw new LlmOutputException("bad json")
            : Answer("Main.", new MandatoryRequirementV1(RequirementType.TaxCompliance, "Tax compliant", null, new(1, "main"), 0.9)));

        var outcome = await Sut(tender).ExtractAsync(tender.Id);

        outcome.DocumentsProcessed.Should().Be(1);
        outcome.Errors.Should().ContainSingle().Which.Should().Contain("Annexure.pdf").And.Contain("bad json");
        _written.Should().ContainSingle(r => r.Type == RequirementType.TaxCompliance);
    }

    [Theory]
    [InlineData("2026-11-20T11:00:00+02:00", "2026-11-20T09:00:00Z")]
    [InlineData("not a date", null)]
    [InlineData(null, null)]
    public void Dates_parse_with_their_offset(string? iso, string? expectedUtc)
    {
        var parsed = RequirementExtractor.ParseDate(iso);
        if (expectedUtc is null)
        {
            parsed.Should().BeNull();
        }
        else
        {
            parsed!.Value.ToUniversalTime().Should().Be(DateTimeOffset.Parse(expectedUtc, System.Globalization.CultureInfo.InvariantCulture));
        }
    }
}
