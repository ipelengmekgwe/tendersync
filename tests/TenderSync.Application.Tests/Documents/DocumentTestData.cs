using System.Collections.Concurrent;
using System.Text;
using TenderSync.Application.Abstractions.Documents;
using TenderSync.Application.Abstractions.Storage;
using TenderSync.Domain.Enums;
using TenderSync.Domain.Tenders;

namespace TenderSync.Application.Tests.Documents;

public static class DocumentTestData
{
    public static readonly DateTimeOffset Now = new(2026, 10, 24, 9, 0, 0, TimeSpan.Zero);
    public const string Ocid = "ocds-9t57fa-175445";

    public static byte[] PdfBytes => Encoding.ASCII.GetBytes("%PDF-1.7\n1 0 obj << /Type /Catalog >> endobj\n%%EOF\n");

    public static byte[] ZipBytes => [0x50, 0x4B, 0x03, 0x04, 0x14, 0x00, 0x00, 0x00, 0x08, 0x00, 0x00, 0x00];

    public static byte[] HtmlBytes => Encoding.UTF8.GetBytes("<!DOCTYPE html><html><body>Please log in</body></html>");

    public static TenderDocumentSnapshot Document(string id, string title, string? format, string? url = null) =>
        new(id, title, url ?? $"https://www.etenders.gov.za/home/Download?blobName={id}.{format ?? "bin"}&downloadedFileName={Uri.EscapeDataString(title)}", format, "en", Now.AddHours(-1), Now.AddHours(-1));

    public static Tender TenderWith(params TenderDocumentSnapshot[] documents)
    {
        var snapshot = new TenderSnapshot(
            Ocid, "175445", "WCGHCC0038/1/2025", "Supplementary bid for comfort packs", "SUPPLEMENTARY BID", "723", "Western Cape - Health",
            ProcurementCategory.Goods, ["goods"], "open", "Request for Bid", null, "ZAR", Now.AddDays(-1), Now.AddDays(20),
            "https://www.etenders.gov.za/Home/TenderDetails?Id=175445", "release-1", Now.AddDays(-1), "compiled", "{}", documents, false);
        var tender = Tender.CreateFromSnapshot(snapshot, Guid.CreateVersion7(), Province.WesternCape, TenderSourceKind.ETendersOcds, Now);
        tender.ClearDomainEvents();
        return tender;
    }
}

/// <summary>In-memory IDocumentStore for unit tests.</summary>
public sealed class MemoryDocumentStore : IDocumentStore
{
    public ConcurrentDictionary<string, (byte[] Bytes, string? ContentType)> Files { get; } = new(StringComparer.Ordinal);

    public async Task<string> SaveAsync(string path, Stream content, string? contentType, CancellationToken cancellationToken = default)
    {
        using var buffer = new MemoryStream();
        await content.CopyToAsync(buffer, cancellationToken);
        Files[path] = (buffer.ToArray(), contentType);
        return path;
    }

    public Task<Stream> OpenReadAsync(string path, CancellationToken cancellationToken = default) =>
        Files.TryGetValue(path, out var file) ? Task.FromResult<Stream>(new MemoryStream(file.Bytes)) : throw new FileNotFoundException(path);

    public Task<bool> ExistsAsync(string path, CancellationToken cancellationToken = default) => Task.FromResult(Files.ContainsKey(path));

    public Task DeleteAsync(string path, CancellationToken cancellationToken = default)
    {
        Files.TryRemove(path, out _);
        return Task.CompletedTask;
    }
}

/// <summary>Scripted IDocumentDownloader: a byte array per URL, or an exception.</summary>
public sealed class FakeDownloader : IDocumentDownloader
{
    private readonly Dictionary<string, Func<DownloadedFile>> _responses = new(StringComparer.Ordinal);

    public List<Uri> Requested { get; } = [];

    public FakeDownloader Returns(string url, byte[] bytes, string? contentType = null, string? fileName = null)
    {
        _responses[url] = () => new DownloadedFile(new MemoryStream(bytes), contentType, fileName, bytes.Length);
        return this;
    }

    public FakeDownloader Throws(string url, Exception exception)
    {
        _responses[url] = () => throw exception;
        return this;
    }

    public Task<DownloadedFile> DownloadAsync(Uri url, long maxBytes, CancellationToken cancellationToken = default)
    {
        Requested.Add(url);
        return _responses.TryGetValue(url.AbsoluteUri, out var factory)
            ? Task.FromResult(factory())
            : throw new DocumentDownloadException($"No scripted response for {url}");
    }
}

/// <summary>Scripted IDocumentTextExtractor.</summary>
public sealed class FakeExtractor(Func<string, ExtractedDocument> extract, params string[] formats) : IDocumentTextExtractor
{
    public bool Supports(string format) => formats.Contains(format, StringComparer.Ordinal);

    public Task<ExtractedDocument> ExtractAsync(Stream content, string format, CancellationToken cancellationToken = default) => Task.FromResult(extract(format));
}
