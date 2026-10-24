using System.IO.Compression;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using TenderSync.Application.Abstractions.Documents;
using TenderSync.Application.Features.Documents;

namespace TenderSync.Infrastructure.Documents.Extractors;

/// <summary>
/// The Application port: routes a format to its <see cref="ITextExtractor"/> and expands zip archives (PLAN.md §7.3: depth 2).
/// Pages of archived files are numbered consecutively across the archive and each page starts with a "[file: name]" line
/// so a citation can name the inner file. Entries without an extractor are skipped; zip-bomb limits come from DocumentOptions.
/// </summary>
public sealed class CompositeTextExtractor : IDocumentTextExtractor
{
    private readonly Dictionary<string, ITextExtractor> _byFormat;
    private readonly IOptions<DocumentOptions> _options;
    private readonly ILogger<CompositeTextExtractor> _logger;

    public CompositeTextExtractor(IEnumerable<ITextExtractor> extractors, IOptions<DocumentOptions> options, ILogger<CompositeTextExtractor> logger)
    {
        _byFormat = new Dictionary<string, ITextExtractor>(StringComparer.Ordinal);
        foreach (var extractor in extractors)
        {
            foreach (var format in extractor.Formats)
            {
                _byFormat[format] = extractor;
            }
        }

        _options = options;
        _logger = logger;
    }

    public bool Supports(string format) => format == DocumentFormats.Zip || _byFormat.ContainsKey(format);

    public async Task<ExtractedDocument> ExtractAsync(Stream content, string format, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(content);
        var seekable = await EnsureSeekableAsync(content, cancellationToken);
        try
        {
            if (format == DocumentFormats.Zip)
            {
                var pages = new List<ExtractedPage>();
                var budget = new ZipBudget(_options.Value.ZipMaxUncompressedBytes, _options.Value.ZipMaxEntries);
                await ExpandZipAsync(seekable, depth: 1, prefix: string.Empty, pages, budget, cancellationToken);
                return new ExtractedDocument(pages);
            }

            if (!_byFormat.TryGetValue(format, out var extractor))
            {
                throw new TextExtractionException($"No text extractor for format '{format}'.");
            }

            return await extractor.ExtractAsync(seekable, format, cancellationToken);
        }
        finally
        {
            if (!ReferenceEquals(seekable, content))
            {
                await seekable.DisposeAsync();
            }
        }
    }

    private async Task ExpandZipAsync(Stream archiveStream, int depth, string prefix, List<ExtractedPage> pages, ZipBudget budget, CancellationToken cancellationToken)
    {
        using var archive = new ZipArchive(archiveStream, ZipArchiveMode.Read, leaveOpen: true);
        foreach (var entry in archive.Entries)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (entry.FullName.EndsWith('/') || entry.Name.Length == 0)
            {
                continue;
            }

            var name = prefix.Length == 0 ? entry.FullName : $"{prefix}/{entry.FullName}";
            var format = DocumentFormats.Normalise(Path.GetExtension(entry.Name));
            if (format is null || (format != DocumentFormats.Zip && !_byFormat.ContainsKey(format)))
            {
                _logger.LogDebug("Skipping archive entry {Entry}: no extractor", name);
                continue;
            }

            if (!budget.TryTake(entry.Length))
            {
                _logger.LogWarning("Stopped expanding archive at {Entry}: entry or size limit reached", name);
                pages.Add(new ExtractedPage(pages.Count + 1, $"[file: {name}]\n[archive limit reached; remaining entries skipped]"));
                return;
            }

            await using var buffer = new MemoryStream();
            await using (var entryStream = entry.Open())
            {
                await entryStream.CopyToAsync(buffer, cancellationToken);
            }

            buffer.Position = 0;

            if (format == DocumentFormats.Zip)
            {
                if (depth < _options.Value.ZipMaxDepth)
                {
                    await ExpandZipAsync(buffer, depth + 1, name, pages, budget, cancellationToken);
                }
                else
                {
                    _logger.LogInformation("Skipping nested archive {Entry}: depth limit {Depth}", name, _options.Value.ZipMaxDepth);
                }

                continue;
            }

            try
            {
                var inner = await _byFormat[format].ExtractAsync(buffer, format, cancellationToken);
                foreach (var page in inner.Pages)
                {
                    pages.Add(new ExtractedPage(pages.Count + 1, $"[file: {name}]\n{page.Text}"));
                }
            }
            catch (TextExtractionException ex)
            {
                _logger.LogWarning(ex, "Archive entry {Entry} could not be extracted", name);
                pages.Add(new ExtractedPage(pages.Count + 1, $"[file: {name}]\n[could not be read: {ex.Message}]"));
            }
        }
    }

    private static async Task<Stream> EnsureSeekableAsync(Stream content, CancellationToken cancellationToken)
    {
        if (content.CanSeek)
        {
            content.Position = 0;
            return content;
        }

        var buffer = new MemoryStream();
        await content.CopyToAsync(buffer, cancellationToken);
        buffer.Position = 0;
        return buffer;
    }

    private sealed class ZipBudget(long maxBytes, int maxEntries)
    {
        private long _bytes;
        private int _entries;

        public bool TryTake(long entryBytes)
        {
            if (_entries + 1 > maxEntries || _bytes + entryBytes > maxBytes)
            {
                return false;
            }

            _entries++;
            _bytes += entryBytes;
            return true;
        }
    }
}
