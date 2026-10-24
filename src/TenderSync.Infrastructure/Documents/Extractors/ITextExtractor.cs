using TenderSync.Application.Abstractions.Documents;

namespace TenderSync.Infrastructure.Documents.Extractors;

/// <summary>One extractor per family of formats (PLAN.md §7.3 step 4). The stream is seekable and positioned at 0.</summary>
public interface ITextExtractor
{
    IReadOnlyCollection<string> Formats { get; }

    Task<ExtractedDocument> ExtractAsync(Stream content, string format, CancellationToken cancellationToken);
}
