using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using TenderSync.Application.Abstractions;
using TenderSync.Application.Abstractions.Documents;
using TenderSync.Application.Abstractions.Persistence;
using TenderSync.Application.Abstractions.Storage;
using TenderSync.Application.Common.Exceptions;
using TenderSync.Domain.Enums;
using TenderSync.Domain.Tenders;

namespace TenderSync.Application.Features.Documents;

public sealed record DocumentExtractionResult(Guid DocumentId, DocumentStatus Status, int Pages, int Chars, bool NeedsOcr, string? Error);

/// <summary>
/// Extracts the text of one downloaded document (PLAN.md §7.3 steps 4–5): picks the extractor by the stored file's format,
/// replaces document_texts, applies the needs_ocr heuristic and marks the document Extracted. Formats without an extractor
/// are marked Skipped; unreadable files are marked Failed with the reason. Safe to re-run.
/// </summary>
public sealed class DocumentTextExtraction(
    IAppDbContext db,
    IDocumentStore store,
    IDocumentTextExtractor extractor,
    IOptions<DocumentOptions> options,
    IDateTimeProvider clock,
    ILogger<DocumentTextExtraction> logger)
{
    public async Task<DocumentExtractionResult> ExtractAsync(Guid documentId, CancellationToken cancellationToken = default)
    {
        var document = await db.Tenders.GetDocumentAsync(documentId, cancellationToken)
            ?? throw new NotFoundException(nameof(TenderDocument), documentId);

        if (document.Status is not (DocumentStatus.Downloaded or DocumentStatus.Extracted) || document.StoragePath is null)
        {
            throw new ConflictException($"Document '{documentId}' is {document.Status}; it must be downloaded before its text can be extracted.");
        }

        var format = DocumentFormats.Normalise(Path.GetExtension(document.StoragePath)) ?? DocumentFormats.Normalise(document.Format);
        if (format is null || !extractor.Supports(format))
        {
            document.MarkSkipped($"No text extractor for format '{format ?? "unknown"}'.");
            await db.SaveChangesAsync(cancellationToken);
            return new DocumentExtractionResult(document.Id, document.Status, 0, 0, false, document.Error);
        }

        ExtractedDocument extracted;
        try
        {
            await using var content = await store.OpenReadAsync(document.StoragePath, cancellationToken);
            extracted = await extractor.ExtractAsync(content, format, cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            var message = ex.Message.Length <= 2000 ? ex.Message : ex.Message[..2000];
            document.MarkFailed($"Text extraction failed: {message}");
            await db.SaveChangesAsync(cancellationToken);
            logger.LogError(ex, "Text extraction of {DocumentId} ({Format}) failed", document.Id, format);
            return new DocumentExtractionResult(document.Id, document.Status, 0, 0, false, document.Error);
        }

        var pages = extracted.Pages.Select(p => new DocumentText(document.Id, p.Page, NormaliseWhitespace(p.Text))).ToList();
        var chars = pages.Sum(p => p.CharCount);
        var needsOcr = format == DocumentFormats.Pdf && pages.Count > 0 && (double)chars / pages.Count < options.Value.OcrMinCharsPerPage;

        await db.Tenders.ReplaceDocumentTextsAsync(document.Id, pages, cancellationToken);
        document.MarkExtracted(pages.Count, needsOcr, clock.UtcNow);
        await db.SaveChangesAsync(cancellationToken);

        logger.LogInformation("Extracted {Pages} page(s), {Chars} chars from {DocumentId} ({Format}); needs OCR: {NeedsOcr}", pages.Count, chars, document.Id, format, needsOcr);
        return new DocumentExtractionResult(document.Id, document.Status, pages.Count, chars, needsOcr, null);
    }

    /// <summary>Collapses runs of spaces and more than two line breaks; keeps paragraph structure for the LLM steps.</summary>
    public static string NormaliseWhitespace(string text)
    {
        if (string.IsNullOrEmpty(text))
        {
            return string.Empty;
        }

        var lines = text.Replace("\r\n", "\n", StringComparison.Ordinal).Replace('\r', '\n').Split('\n');
        var result = new List<string>(lines.Length);
        var blankRun = 0;
        foreach (var raw in lines)
        {
            var line = string.Join(' ', raw.Split([' ', '\t', ' '], StringSplitOptions.RemoveEmptyEntries));
            if (line.Length == 0)
            {
                if (++blankRun <= 1 && result.Count > 0)
                {
                    result.Add(string.Empty);
                }

                continue;
            }

            blankRun = 0;
            result.Add(line);
        }

        while (result.Count > 0 && result[^1].Length == 0)
        {
            result.RemoveAt(result.Count - 1);
        }

        return string.Join('\n', result);
    }
}
