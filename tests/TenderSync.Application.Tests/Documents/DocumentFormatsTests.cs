using System.Text;
using TenderSync.Application.Features.Documents;

namespace TenderSync.Application.Tests.Documents;

public class DocumentFormatsTests
{
    [Theory]
    [InlineData("pdf", null, null, "pdf")]
    [InlineData(".PDF", null, null, "pdf")]
    [InlineData("application/pdf", null, null, "pdf")]
    [InlineData("application/vnd.openxmlformats-officedocument.wordprocessingml.document", null, null, "docx")]
    [InlineData(null, "Annexure B.xlsx", null, "xlsx")]
    [InlineData(null, "no-extension", "https://www.etenders.gov.za/home/Download?blobName=01e93c3b.xls&downloadedFileName=x", "xls")]
    [InlineData(null, null, "https://example.org/files/pack.ZIP", "zip")]
    [InlineData("", null, null, null)]
    public void Normalise_accepts_extensions_mime_types_file_names_and_blob_urls(string? declared, string? fileName, string? url, string? expected)
    {
        DocumentFormats.Normalise(declared, fileName, url is null ? null : new Uri(url)).Should().Be(expected);
    }

    [Fact]
    public void Sniff_recognises_pdf_office_zip_ole_and_html()
    {
        DocumentFormats.Sniff(DocumentTestData.PdfBytes, "docx").Should().Be("pdf", "magic bytes beat the declared format");
        DocumentFormats.Sniff(DocumentTestData.ZipBytes, "docx").Should().Be("docx", "Office Open XML is a zip, so the declared format is kept");
        DocumentFormats.Sniff(DocumentTestData.ZipBytes, "xlsx").Should().Be("xlsx");
        DocumentFormats.Sniff(DocumentTestData.ZipBytes, "pdf").Should().Be("zip", "a zip declared as pdf is stored as what it is");
        DocumentFormats.Sniff(DocumentTestData.ZipBytes, null).Should().Be("zip");
        DocumentFormats.Sniff([0xD0, 0xCF, 0x11, 0xE0, 0xA1, 0xB1, 0x1A, 0xE1], "xls").Should().Be("xls");
        DocumentFormats.Sniff([0xD0, 0xCF, 0x11, 0xE0, 0xA1, 0xB1, 0x1A, 0xE1], "doc").Should().Be("doc");
        DocumentFormats.Sniff(DocumentTestData.HtmlBytes, "pdf").Should().Be("html");
        DocumentFormats.Sniff(Encoding.UTF8.GetBytes("  \n<html lang='en'>"), "pdf").Should().Be("html");
        DocumentFormats.Sniff(Encoding.UTF8.GetBytes("plain words"), "txt").Should().Be("txt", "unknown signatures fall back to the declared format");
        DocumentFormats.Sniff([], "pdf").Should().Be("pdf");
    }

    [Fact]
    public void Storage_path_follows_the_plan_and_sanitises_the_ocid()
    {
        var id = Guid.Parse("0199e8c6-6d9e-7a4e-9b44-6d3a1c1f4c2a");

        DocumentFormats.StoragePath("ocds-9t57fa-175445", id, "pdf").Should().Be($"tenders/ocds-9t57fa-175445/{id}.pdf");
        DocumentFormats.StoragePath("weird/../ocid name", id, null).Should().Be($"tenders/weird____ocid_name/{id}", "only letters, digits, dash and underscore survive");
    }

    [Theory]
    [InlineData("pdf", "application/pdf")]
    [InlineData("docx", "application/vnd.openxmlformats-officedocument.wordprocessingml.document")]
    [InlineData("zip", "application/zip")]
    [InlineData("xyz", "application/octet-stream")]
    [InlineData(null, "application/octet-stream")]
    public void Content_type_is_derived_from_the_format(string? format, string expected)
    {
        DocumentFormats.ContentTypeFor(format).Should().Be(expected);
    }
}
