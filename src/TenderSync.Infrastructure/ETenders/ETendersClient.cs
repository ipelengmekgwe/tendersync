using System.Globalization;
using System.Net;
using System.Runtime.CompilerServices;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace TenderSync.Infrastructure.ETenders;

/// <summary>Outcome of paging one window: how many requests, and how many releases the API could not serve.</summary>
public sealed record PagingStats(int Pages, int Requests, int SkippedReleases, IReadOnlyList<string> SkippedRanges);

/// <summary>
/// Typed HttpClient for the eTenders OCDS API (PLAN.md Appendix A). Resilience (timeouts, retry, circuit breaker) is
/// attached to the HttpClient in <see cref="ETendersServiceExtensions"/>; plain HTTP 500 is excluded there because the API
/// returns it deterministically for certain releases ("poison rows"). This class bisects such a page (100 → 10 → 1) and
/// skips only the rows the API cannot serve, so one bad release never blocks a window.
/// </summary>
public sealed class ETendersClient(HttpClient http, IOptions<ETendersOptions> options, ILogger<ETendersClient> logger)
{
    public const string ReleasesPath = "api/OCDSReleases";

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        NumberHandling = System.Text.Json.Serialization.JsonNumberHandling.AllowReadingFromString,
    };

    private readonly ETendersOptions _options = options.Value;

    public PagingStats LastStats { get; private set; } = new(0, 0, 0, []);

    public Task<OcdsPage> GetPageAsync(DateOnly from, DateOnly to, int pageNumber, CancellationToken cancellationToken = default) =>
        GetPageAsync(from, to, pageNumber, _options.PageSize, cancellationToken);

    public async Task<OcdsPage> GetPageAsync(DateOnly from, DateOnly to, int pageNumber, int pageSize, CancellationToken cancellationToken = default)
    {
        var url = $"{ReleasesPath}?PageNumber={pageNumber}&PageSize={pageSize}&dateFrom={FormatDate(from)}&dateTo={FormatDate(to)}";
        using var response = await http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        response.EnsureSuccessStatusCode();

        var raw = await response.Content.ReadAsStringAsync(cancellationToken);
        return ParsePage(pageNumber, raw);
    }

    /// <summary>
    /// Follows the window page by page until the feed ends (no links.next or an empty page) or MaxPagesPerRun is hit.
    /// A page the API cannot serve is bisected; its surviving rows are yielded as a synthetic page.
    /// </summary>
    public async IAsyncEnumerable<OcdsPage> GetPagesAsync(DateOnly from, DateOnly to, [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        int pages = 0, requests = 0, skipped = 0;
        var skippedRanges = new List<string>();
        var size = _options.PageSize;

        for (var pageNumber = 1; pageNumber <= _options.MaxPagesPerRun; pageNumber++)
        {
            OcdsPage? page = null;
            var bisected = false;
            requests++;
            try
            {
                page = await GetPageAsync(from, to, pageNumber, size, cancellationToken);
            }
            catch (HttpRequestException ex) when (ex.StatusCode == HttpStatusCode.InternalServerError)
            {
                bisected = true;
            }

            if (bisected)
            {
                var offset = (pageNumber - 1) * size;
                logger.LogWarning("eTenders returned 500 for {From}..{To} page {Page} (size {Size}); bisecting offsets {Offset}-{End}", from, to, pageNumber, size, offset, offset + size - 1);
                var (releases, subRequests, subSkipped, ranges) = await BisectAsync(from, to, offset, size, cancellationToken);
                requests += subRequests;
                skipped += subSkipped;
                skippedRanges.AddRange(ranges);

                if (releases.Count == 0 && subSkipped == 0)
                {
                    break;
                }

                pages++;
                yield return new OcdsPage(pageNumber, string.Empty, releases, NextUrl: null);
                continue; // links are unknown after a bisect; the next page decides whether the window has ended
            }

            if (page!.Releases.Count == 0)
            {
                break;
            }

            pages++;
            yield return page;

            if (string.IsNullOrWhiteSpace(page.NextUrl))
            {
                break;
            }
        }

        if (pages >= _options.MaxPagesPerRun)
        {
            logger.LogWarning("Stopped paging eTenders after {MaxPages} pages for {From}..{To}", _options.MaxPagesPerRun, from, to);
        }

        LastStats = new PagingStats(pages, requests, skipped, skippedRanges);
    }

    public async Task<OcdsReleaseEnvelope?> GetReleaseAsync(string ocid, CancellationToken cancellationToken = default)
    {
        using var response = await http.GetAsync($"{ReleasesPath}/release/{Uri.EscapeDataString(ocid)}", cancellationToken);
        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            return null;
        }

        response.EnsureSuccessStatusCode();
        var raw = await response.Content.ReadAsStringAsync(cancellationToken);
        return ParseRelease(JsonDocument.Parse(raw).RootElement);
    }

    public static OcdsPage ParsePage(int pageNumber, string rawJson)
    {
        var package = JsonSerializer.Deserialize<OcdsReleasePackage>(rawJson, Json)
            ?? throw new InvalidDataException("eTenders returned an empty release package.");

        var releases = package.Releases.Select(ParseRelease).ToList();
        return new OcdsPage(pageNumber, rawJson, releases, package.Links?.Next);
    }

    /// <summary>Largest sub-page size that tiles the current size: 100 → 10 → 1, 50 → 10 → 1, 25 → 5 → 1.</summary>
    public static int SubPageSize(int size)
    {
        foreach (var candidate in new[] { 10, 5, 2 })
        {
            if (candidate < size && size % candidate == 0)
            {
                return candidate;
            }
        }

        return 1;
    }

    internal static string FormatDate(DateOnly date) => date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

    private async Task<(List<OcdsReleaseEnvelope> Releases, int Requests, int Skipped, List<string> Ranges)> BisectAsync(DateOnly from, DateOnly to, int offset, int size, CancellationToken cancellationToken)
    {
        var releases = new List<OcdsReleaseEnvelope>();
        var requests = 0;
        var skipped = 0;
        var ranges = new List<string>();

        if (size == 1)
        {
            skipped++;
            ranges.Add($"{FormatDate(from)}..{FormatDate(to)}@{offset}");
            logger.LogError("eTenders cannot serve the release at offset {Offset} for {From}..{To}; skipped", offset, from, to);
            return (releases, requests, skipped, ranges);
        }

        var subSize = SubPageSize(size);
        for (var subOffset = offset; subOffset < offset + size; subOffset += subSize)
        {
            var subPage = subOffset / subSize + 1;
            requests++;
            try
            {
                var page = await GetPageAsync(from, to, subPage, subSize, cancellationToken);
                releases.AddRange(page.Releases);
                if (page.Releases.Count < subSize)
                {
                    break; // the window ended inside this block
                }
            }
            catch (HttpRequestException ex) when (ex.StatusCode == HttpStatusCode.InternalServerError)
            {
                var (nested, nestedRequests, nestedSkipped, nestedRanges) = await BisectAsync(from, to, subOffset, subSize, cancellationToken);
                releases.AddRange(nested);
                requests += nestedRequests;
                skipped += nestedSkipped;
                ranges.AddRange(nestedRanges);
            }
        }

        return (releases, requests, skipped, ranges);
    }

    private static OcdsReleaseEnvelope ParseRelease(JsonElement element)
    {
        var release = element.Deserialize<OcdsRelease>(Json) ?? throw new InvalidDataException("eTenders returned a null release.");
        return new OcdsReleaseEnvelope(release, element.GetRawText());
    }
}
