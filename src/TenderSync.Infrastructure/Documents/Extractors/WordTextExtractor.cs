using System.Text;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;
using TenderSync.Application.Abstractions.Documents;

namespace TenderSync.Infrastructure.Documents.Extractors;

/// <summary>
/// Open XML Word documents. Pages follow Word's last-rendered page breaks when the file has them (saved by Word), otherwise
/// explicit page breaks; a document without either is one page. Table cells become tab-separated lines.
/// </summary>
public sealed class WordTextExtractor : ITextExtractor
{
    public IReadOnlyCollection<string> Formats { get; } = ["docx", "docm", "dotx"];

    public Task<ExtractedDocument> ExtractAsync(Stream content, string format, CancellationToken cancellationToken)
    {
        try
        {
            using var document = WordprocessingDocument.Open(content, isEditable: false);
            var body = document.MainDocumentPart?.Document?.Body;
            if (body is null)
            {
                return Task.FromResult(new ExtractedDocument([new ExtractedPage(1, string.Empty)]));
            }

            var useRendered = body.Descendants<LastRenderedPageBreak>().Any();
            var pages = new List<ExtractedPage>();
            var current = new StringBuilder();

            foreach (var paragraph in body.Descendants<Paragraph>())
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (paragraph.Ancestors<Paragraph>().Any())
                {
                    continue; // text boxes nest paragraphs; the outer paragraph already walks them
                }

                foreach (var element in paragraph.Descendants())
                {
                    switch (element)
                    {
                        case Text text:
                            current.Append(text.Text);
                            break;
                        case TabChar:
                            current.Append('\t');
                            break;
                        case Break br when br.Type?.Value == BreakValues.Page && !useRendered:
                        case LastRenderedPageBreak when useRendered:
                            pages.Add(new ExtractedPage(pages.Count + 1, current.ToString()));
                            current.Clear();
                            break;
                        case Break:
                            current.Append('\n');
                            break;
                    }
                }

                current.Append(paragraph.Parent is TableCell ? '\t' : '\n');
            }

            pages.Add(new ExtractedPage(pages.Count + 1, current.ToString()));
            return Task.FromResult(new ExtractedDocument(pages));
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            throw new TextExtractionException($"Word document could not be read: {ex.Message}", ex);
        }
    }
}
