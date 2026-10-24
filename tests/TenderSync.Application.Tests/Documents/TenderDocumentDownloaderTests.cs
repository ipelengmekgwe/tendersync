using System.Security.Cryptography;
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

public class TenderDocumentDownloaderTests
{
    private readonly MemoryDocumentStore _store = new();
    private readonly FakeDownloader _downloader = new();
    private readonly IAppDbContext _db = Substitute.For<IAppDbContext>();
    private readonly DocumentOptions _options = new();

    private TenderDocumentDownloader Sut(Tender tender)
    {
        _db.Tenders.GetWithDetailsAsync(tender.Id, Arg.Any<CancellationToken>()).Returns(tender);
        var clock = Substitute.For<IDateTimeProvider>();
        clock.UtcNow.Returns(DocumentTestData.Now);
        return new TenderDocumentDownloader(_db, _downloader, _store, Options.Create(_options), clock, NullLogger<TenderDocumentDownloader>.Instance);
    }

    [Fact]
    public async Task A_pdf_is_stored_under_the_plan_path_with_its_hash_and_size()
    {
        var doc = DocumentTestData.Document("d1", "Bid Document.pdf", "pdf");
        var tender = DocumentTestData.TenderWith(doc);
        _downloader.Returns(doc.Url, DocumentTestData.PdfBytes, "application/pdf", "Bid Document.pdf");

        var summary = await Sut(tender).DownloadAsync(tender.Id);

        summary.Should().BeEquivalentTo(new { Attempted = 1, Downloaded = 1, Skipped = 0, Failed = 0 });
        var stored = tender.Documents.Single();
        stored.Status.Should().Be(DocumentStatus.Downloaded);
        stored.StoragePath.Should().Be($"tenders/{DocumentTestData.Ocid}/{stored.Id}.pdf");
        stored.Sha256.Should().Be(Convert.ToHexStringLower(SHA256.HashData(DocumentTestData.PdfBytes)));
        stored.SizeBytes.Should().Be(DocumentTestData.PdfBytes.Length);
        stored.DownloadedAt.Should().Be(DocumentTestData.Now);
        _store.Files[stored.StoragePath!].ContentType.Should().Be("application/pdf");
        _store.Files[stored.StoragePath!].Bytes.Should().Equal(DocumentTestData.PdfBytes);
        await _db.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Audio_files_and_foreign_hosts_are_skipped_without_a_request()
    {
        var audio = DocumentTestData.Document("a", "Briefing recording.mp3", "mp3");
        var foreign = DocumentTestData.Document("f", "Elsewhere.pdf", "pdf", "https://evil.example.org/x.pdf");
        var tender = DocumentTestData.TenderWith(audio, foreign);

        var summary = await Sut(tender).DownloadAsync(tender.Id);

        summary.Skipped.Should().Be(2);
        _downloader.Requested.Should().BeEmpty();
        tender.Documents.Should().AllSatisfy(d => d.Status.Should().Be(DocumentStatus.Skipped));
        tender.Documents.Single(d => d.ExternalId == "f").Error.Should().Contain("evil.example.org");
    }

    [Fact]
    public async Task An_html_login_page_and_a_download_error_are_recorded_as_failures()
    {
        var html = DocumentTestData.Document("h", "Pack.pdf", "pdf");
        var broken = DocumentTestData.Document("b", "Broken.pdf", "pdf");
        var tender = DocumentTestData.TenderWith(html, broken);
        _downloader.Returns(html.Url, DocumentTestData.HtmlBytes, "text/html");
        _downloader.Throws(broken.Url, new DocumentDownloadException("HTTP 404 Not Found from www.etenders.gov.za."));

        var summary = await Sut(tender).DownloadAsync(tender.Id);

        summary.Failed.Should().Be(2);
        summary.Errors.Should().HaveCount(2);
        tender.Documents.Single(d => d.ExternalId == "h").Error.Should().Contain("HTML page");
        tender.Documents.Single(d => d.ExternalId == "b").Error.Should().Contain("HTTP 404");
        _store.Files.Should().BeEmpty();
        await _db.Received(2).SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task The_sniffed_format_decides_the_stored_extension()
    {
        var doc = DocumentTestData.Document("z", "Pack.pdf", "pdf");
        var tender = DocumentTestData.TenderWith(doc);
        _downloader.Returns(doc.Url, DocumentTestData.ZipBytes);

        await Sut(tender).DownloadAsync(tender.Id);

        tender.Documents.Single().StoragePath.Should().EndWith(".zip", "the body is a zip even though the feed says pdf");
    }

    [Fact]
    public async Task Documents_already_stored_are_left_alone_and_failed_ones_are_retried()
    {
        var done = DocumentTestData.Document("done", "Done.pdf", "pdf");
        var failed = DocumentTestData.Document("failed", "Failed.pdf", "pdf");
        var tender = DocumentTestData.TenderWith(done, failed);
        tender.Documents.Single(d => d.ExternalId == "done").MarkDownloaded("tenders/x/done.pdf", new string('a', 64), 1, DocumentTestData.Now);
        tender.Documents.Single(d => d.ExternalId == "failed").MarkFailed("earlier timeout");
        _downloader.Returns(failed.Url, DocumentTestData.PdfBytes);

        var summary = await Sut(tender).DownloadAsync(tender.Id);

        summary.Attempted.Should().Be(1);
        summary.Downloaded.Should().Be(1);
        _downloader.Requested.Should().ContainSingle().Which.AbsoluteUri.Should().Be(failed.Url);
        tender.Documents.Single(d => d.ExternalId == "failed").Status.Should().Be(DocumentStatus.Downloaded);
    }

    [Fact]
    public async Task An_unknown_tender_is_not_found()
    {
        var sut = Sut(DocumentTestData.TenderWith());

        await FluentActions.Awaiting(() => sut.DownloadAsync(Guid.CreateVersion7())).Should().ThrowAsync<NotFoundException>();
    }
}
