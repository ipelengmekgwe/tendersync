using TenderSync.Application.Abstractions.Documents;
using UglyToad.PdfPig;
using UglyToad.PdfPig.DocumentLayoutAnalysis.TextExtractor;

namespace TenderSync.Infrastructure.Documents.Extractors;

/// <summary>PdfPig, one page per PDF page in reading order. Scanned pages come back (nearly) empty, which the needs_ocr heuristic picks up.</summary>
public sealed class PdfTextExtractor : ITextExtractor
{
    public IReadOnlyCollection<string> Formats { get; } = ["pdf"];

    public Task<ExtractedDocument> ExtractAsync(Stream content, string format, CancellationToken cancellationToken)
    {
        try
        {
            using var document = PdfDocument.Open(content, new ParsingOptions { UseLenientParsing = true, ClipPaths = false });
            var pages = new List<ExtractedPage>(document.NumberOfPages);
            foreach (var page in document.GetPages())
            {
                cancellationToken.ThrowIfCancellationRequested();
                string text;
                try
                {
                    text = ContentOrderTextExtractor.GetText(page, addDoubleNewline: true);
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    // Fall back to the raw glyph order for pages whose layout analysis fails.
                    text = page.Text;
                }

                pages.Add(new ExtractedPage(page.Number, text));
            }

            return Task.FromResult(new ExtractedDocument(pages));
        }
        catch (Exception ex) when (ex is not OperationCanceledException and not TextExtractionException)
        {
            throw new TextExtractionException($"PDF could not be read: {ex.Message}", ex);
        }
    }
}
