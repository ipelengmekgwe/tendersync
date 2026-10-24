using System.Text;
using TenderSync.Application.Abstractions.Documents;

namespace TenderSync.Infrastructure.Documents.Extractors;

/// <summary>Plain text and CSV: a single page 0 (the format has no pages).</summary>
public sealed class PlainTextExtractor : ITextExtractor
{
    public IReadOnlyCollection<string> Formats { get; } = ["txt", "csv", "md"];

    public async Task<ExtractedDocument> ExtractAsync(Stream content, string format, CancellationToken cancellationToken)
    {
        using var reader = new StreamReader(content, Encoding.UTF8, detectEncodingFromByteOrderMarks: true, leaveOpen: true);
        var text = await reader.ReadToEndAsync(cancellationToken);
        return new ExtractedDocument([new ExtractedPage(0, text)]);
    }
}
