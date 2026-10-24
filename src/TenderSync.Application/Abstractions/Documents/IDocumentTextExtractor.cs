namespace TenderSync.Application.Abstractions.Documents;

/// <summary>
/// Turns a stored file into page texts (PLAN.md §7.3 step 4). Infrastructure composes one extractor per format (PdfPig,
/// OpenXml, ClosedXML, zip recursion) behind this port.
/// </summary>
public interface IDocumentTextExtractor
{
    /// <summary>True when <paramref name="format"/> (a lower-case extension such as "pdf") has an extractor.</summary>
    bool Supports(string format);

    /// <summary>Extracts every page. Pages are 1-based for paged formats (PDF, Word page breaks, one per sheet); page 0 for formats without pages.</summary>
    Task<ExtractedDocument> ExtractAsync(Stream content, string format, CancellationToken cancellationToken = default);
}

public sealed record ExtractedPage(int Page, string Text);

public sealed record ExtractedDocument(IReadOnlyList<ExtractedPage> Pages)
{
    public static ExtractedDocument Empty { get; } = new([]);

    public int CharCount => Pages.Sum(p => p.Text.Length);
}

/// <summary>The file could not be read (corrupt, encrypted, truncated). Recorded on the document as the failure reason.</summary>
public class TextExtractionException : Exception
{
    public TextExtractionException()
    {
    }

    public TextExtractionException(string message)
        : base(message)
    {
    }

    public TextExtractionException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
