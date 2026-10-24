using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NSubstitute;
using TenderSync.Application.Abstractions;
using TenderSync.Application.Abstractions.Documents;
using TenderSync.Application.Abstractions.Persistence;
using TenderSync.Application.Common.Exceptions;
using TenderSync.Application.Features.Documents;
using TenderSync.Domain.Enums;
using TenderSync.Domain.Tenders;

namespace TenderSync.Application.Tests.Documents;

public class DocumentTextExtractionTests
{
    private readonly MemoryDocumentStore _store = new();
    private readonly IAppDbContext _db = Substitute.For<IAppDbContext>();
    private readonly List<DocumentText> _written = [];

    private DocumentTextExtraction Sut(TenderDocument document, IDocumentTextExtractor extractor)
    {
        _db.Tenders.GetDocumentAsync(document.Id, Arg.Any<CancellationToken>()).Returns(document);
        _db.Tenders.ReplaceDocumentTextsAsync(document.Id, Arg.Do<IEnumerable<DocumentText>>(p => _written.AddRange(p)), Arg.Any<CancellationToken>()).Returns(Task.CompletedTask);
        var clock = Substitute.For<IDateTimeProvider>();
        clock.UtcNow.Returns(DocumentTestData.Now);
        return new DocumentTextExtraction(_db, _store, extractor, Options.Create(new DocumentOptions()), clock, NullLogger<DocumentTextExtraction>.Instance);
    }

    private async Task<TenderDocument> DownloadedAsync(string format)
    {
        var tender = DocumentTestData.TenderWith(DocumentTestData.Document("d", $"Pack.{format}", format));
        var document = tender.Documents.Single();
        var path = DocumentFormats.StoragePath(tender.Ocid, document.Id, format);
        await _store.SaveAsync(path, new MemoryStream(DocumentTestData.PdfBytes), null);
        document.MarkDownloaded(path, new string('a', 64), 10, DocumentTestData.Now);
        return document;
    }

    [Fact]
    public async Task Pages_are_written_and_a_text_pdf_does_not_need_ocr()
    {
        var document = await DownloadedAsync("pdf");
        var extractor = new FakeExtractor(_ => new ExtractedDocument([new ExtractedPage(1, "Bid   number  WCGHCC0038/1/2025 \r\n\r\n\r\nClosing 20 November 2026"), new ExtractedPage(2, new string('x', 200))]), "pdf");

        var result = await Sut(document, extractor).ExtractAsync(document.Id);

        result.Status.Should().Be(DocumentStatus.Extracted);
        result.Pages.Should().Be(2);
        result.NeedsOcr.Should().BeFalse();
        document.PageCount.Should().Be(2);
        document.ExtractedAt.Should().Be(DocumentTestData.Now);
        _written.Should().HaveCount(2);
        _written[0].Text.Should().Be("Bid number WCGHCC0038/1/2025\n\nClosing 20 November 2026", "whitespace runs collapse and blank lines are capped at one");
        _written[0].CharCount.Should().Be(_written[0].Text.Length);
    }

    [Fact]
    public async Task A_scanned_pdf_is_flagged_needs_ocr()
    {
        var document = await DownloadedAsync("pdf");
        var extractor = new FakeExtractor(_ => new ExtractedDocument([new ExtractedPage(1, string.Empty), new ExtractedPage(2, "3"), new ExtractedPage(3, new string('y', 120))]), "pdf");

        var result = await Sut(document, extractor).ExtractAsync(document.Id);

        result.NeedsOcr.Should().BeTrue("121 chars over 3 pages is below the 50 chars/page threshold");
        document.NeedsOcr.Should().BeTrue();
        document.Status.Should().Be(DocumentStatus.Extracted, "the sparse text is still stored; the dashboard shows the scanned flag");
    }

    [Fact]
    public async Task Sparse_word_documents_are_not_flagged_because_the_heuristic_is_for_scans()
    {
        var document = await DownloadedAsync("docx");
        var extractor = new FakeExtractor(_ => new ExtractedDocument([new ExtractedPage(1, "short")]), "docx");

        var result = await Sut(document, extractor).ExtractAsync(document.Id);

        result.NeedsOcr.Should().BeFalse();
    }

    [Fact]
    public async Task Unsupported_formats_are_skipped_and_unreadable_files_fail_with_the_reason()
    {
        var image = await DownloadedAsync("jpg");
        var skipped = await Sut(image, new FakeExtractor(_ => ExtractedDocument.Empty, "pdf")).ExtractAsync(image.Id);
        skipped.Status.Should().Be(DocumentStatus.Skipped);
        image.Error.Should().Contain("jpg");

        var corrupt = await DownloadedAsync("pdf");
        var failed = await Sut(corrupt, new FakeExtractor(_ => throw new TextExtractionException("PDF could not be read: bad xref"), "pdf")).ExtractAsync(corrupt.Id);
        failed.Status.Should().Be(DocumentStatus.Failed);
        corrupt.Error.Should().Contain("bad xref");
        _written.Should().BeEmpty();
    }

    [Fact]
    public async Task A_pending_document_cannot_be_extracted()
    {
        var tender = DocumentTestData.TenderWith(DocumentTestData.Document("p", "Pack.pdf", "pdf"));
        var document = tender.Documents.Single();
        var sut = Sut(document, new FakeExtractor(_ => ExtractedDocument.Empty, "pdf"));

        await FluentActions.Awaiting(() => sut.ExtractAsync(document.Id)).Should().ThrowAsync<ConflictException>();
    }

    [Theory]
    [InlineData("", "")]
    [InlineData("a  b\t c", "a b c")]
    [InlineData("one\r\n\r\n\r\n\r\ntwo\n", "one\n\ntwo")]
    [InlineData("\n\n lead", "lead")]
    public void Whitespace_normalisation_keeps_paragraphs_but_collapses_runs(string input, string expected)
    {
        DocumentTextExtraction.NormaliseWhitespace(input).Should().Be(expected);
    }
}
