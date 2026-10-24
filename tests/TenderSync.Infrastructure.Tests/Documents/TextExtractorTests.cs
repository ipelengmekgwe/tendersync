using System.Text;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using TenderSync.Application.Abstractions.Documents;
using TenderSync.Application.Features.Documents;
using TenderSync.Infrastructure.Documents.Extractors;

namespace TenderSync.Infrastructure.Tests.Documents;

public class TextExtractorTests
{
    private static CompositeTextExtractor Composite(DocumentOptions? options = null) => new(
        [new PdfTextExtractor(), new WordTextExtractor(), new SpreadsheetTextExtractor(), new PlainTextExtractor()],
        Options.Create(options ?? new DocumentOptions()),
        NullLogger<CompositeTextExtractor>.Instance);

    [Fact]
    public async Task Pdf_pages_come_back_in_order_with_their_text()
    {
        var pdf = SampleFiles.Pdf("INVITATION TO BID\nBid number WCGHCC0038/1/2025", "Closing date: 20 November 2026 at 11:00");

        var result = await Composite().ExtractAsync(new MemoryStream(pdf), "pdf");

        result.Pages.Should().HaveCount(2);
        result.Pages[0].Page.Should().Be(1);
        result.Pages[0].Text.Should().Contain("INVITATION TO BID").And.Contain("WCGHCC0038/1/2025");
        result.Pages[1].Text.Should().Contain("20 November 2026");
    }

    [Fact]
    public async Task A_blank_pdf_yields_empty_pages_so_the_ocr_heuristic_can_flag_it()
    {
        var pdf = SampleFiles.Pdf(string.Empty, string.Empty, string.Empty);

        var result = await Composite().ExtractAsync(new MemoryStream(pdf), "pdf");

        result.Pages.Should().HaveCount(3);
        result.CharCount.Should().Be(0);
    }

    [Fact]
    public async Task Corrupt_pdf_bytes_raise_a_text_extraction_error()
    {
        var bytes = Encoding.ASCII.GetBytes("%PDF-1.7 this is not really a pdf");

        var act = () => Composite().ExtractAsync(new MemoryStream(bytes), "pdf");

        await act.Should().ThrowAsync<TextExtractionException>().WithMessage("PDF could not be read*");
    }

    [Fact]
    public async Task Word_documents_split_on_page_breaks_and_flatten_tables()
    {
        var docx = SampleFiles.Docx("SBD 1: Invitation to bid", "Compulsory briefing: 3 November 2026", "---", "Pricing schedule");

        var result = await Composite().ExtractAsync(new MemoryStream(docx), "docx");

        result.Pages.Should().HaveCount(2);
        result.Pages[0].Text.Should().Contain("SBD 1: Invitation to bid").And.Contain("Compulsory briefing");
        result.Pages[1].Text.Should().Contain("Pricing schedule").And.Contain("Item\tQty");
    }

    [Fact]
    public async Task Spreadsheets_give_one_page_per_sheet_with_tab_separated_rows()
    {
        var xlsx = SampleFiles.Xlsx(
            ("Pricing", [["Item", "Unit", "Price"], ["Laptop", "each", "15000"]]),
            ("Notes", [["All prices include VAT"]]));

        var result = await Composite().ExtractAsync(new MemoryStream(xlsx), "xlsx");

        result.Pages.Should().HaveCount(2);
        result.Pages[0].Text.Should().StartWith("# Sheet: Pricing").And.Contain("Laptop\teach\t15000");
        result.Pages[1].Text.Should().Contain("# Sheet: Notes").And.Contain("All prices include VAT");
    }

    [Fact]
    public async Task Zip_archives_are_expanded_recursively_with_file_names_on_every_page()
    {
        var inner = SampleFiles.Zip(("deep/terms.docx", SampleFiles.Docx("Contract terms")));
        var zip = SampleFiles.Zip(
            ("pack/bid.pdf", SampleFiles.Pdf("Bid page one", "Bid page two")),
            ("pack/prices.xlsx", SampleFiles.Xlsx(("Prices", [["Total", "100"]]))),
            ("pack/readme.txt", Encoding.UTF8.GetBytes("Read me first")),
            ("pack/recording.mp3", [1, 2, 3]),
            ("pack/nested.zip", inner));

        var result = await Composite().ExtractAsync(new MemoryStream(zip), "zip");

        result.Pages.Select(p => p.Page).Should().Equal(1, 2, 3, 4, 5);
        result.Pages[0].Text.Should().StartWith("[file: pack/bid.pdf]").And.Contain("Bid page one");
        result.Pages[1].Text.Should().Contain("Bid page two");
        result.Pages[2].Text.Should().StartWith("[file: pack/prices.xlsx]").And.Contain("Total\t100");
        result.Pages[3].Text.Should().StartWith("[file: pack/readme.txt]").And.Contain("Read me first");
        result.Pages[4].Text.Should().StartWith("[file: pack/nested.zip/deep/terms.docx]").And.Contain("Contract terms");
    }

    [Fact]
    public async Task Zip_depth_and_size_limits_are_enforced()
    {
        var level3 = SampleFiles.Zip(("three.txt", Encoding.UTF8.GetBytes("level three")));
        var level2 = SampleFiles.Zip(("two.txt", Encoding.UTF8.GetBytes("level two")), ("level3.zip", level3));
        var zip = SampleFiles.Zip(("one.txt", Encoding.UTF8.GetBytes("level one")), ("level2.zip", level2));

        var result = await Composite().ExtractAsync(new MemoryStream(zip), "zip");
        result.Pages.Select(p => p.Text).Should().Contain(t => t.Contains("level one")).And.Contain(t => t.Contains("level two"));
        result.Pages.Should().NotContain(p => p.Text.Contains("level three"), "depth 2 means the archive inside the archive is the last level expanded");

        var tight = await Composite(new DocumentOptions { ZipMaxEntries = 1 }).ExtractAsync(new MemoryStream(zip), "zip");
        tight.Pages.Should().HaveCount(2);
        tight.Pages[1].Text.Should().Contain("archive limit reached");
    }

    [Fact]
    public void Supported_formats_cover_the_feed()
    {
        var composite = Composite();

        composite.Supports("pdf").Should().BeTrue();
        composite.Supports("docx").Should().BeTrue();
        composite.Supports("xlsx").Should().BeTrue();
        composite.Supports("xls").Should().BeTrue();
        composite.Supports("zip").Should().BeTrue();
        composite.Supports("mp3").Should().BeFalse();
        composite.Supports("jpg").Should().BeFalse();
    }
}
