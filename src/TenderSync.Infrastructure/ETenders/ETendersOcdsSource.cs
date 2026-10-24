using System.Diagnostics;
using System.Net;
using System.Runtime.CompilerServices;
using Microsoft.Extensions.Logging;
using Polly.CircuitBreaker;
using TenderSync.Application.Abstractions.Sources;
using TenderSync.Domain.Enums;
using TenderSync.Domain.Tenders;

namespace TenderSync.Infrastructure.ETenders;

/// <summary>The eTenders OCDS API as an <see cref="ITenderSource"/>: pages, maps, and validates every page for drift.</summary>
public sealed class ETendersOcdsSource(ETendersClient client, ReleaseMapper mapper, ReleaseSchemaValidator validator, ILogger<ETendersOcdsSource> logger) : ITenderSource
{
    public const string SourceName = "eTendersApi";

    public TenderSourceKind Kind => TenderSourceKind.ETendersOcds;

    public SourceFetchStats LastFetchStats { get; private set; } = SourceFetchStats.Empty;

    public async IAsyncEnumerable<TenderSnapshot> FetchAsync(DateOnly from, DateOnly to, [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var drift = new List<string>();

        await foreach (var page in client.GetPagesAsync(from, to, cancellationToken))
        {
            if (page.RawJson.Length > 0)
            {
                var pageDrift = validator.Validate(page.RawJson);
                if (pageDrift.Count > 0)
                {
                    drift.AddRange(pageDrift);
                    logger.LogError("eTenders page {Page} for {From}..{To} drifted from the expected schema: {Drift}", page.PageNumber, from, to, string.Join(" | ", pageDrift.Take(5)));
                }
            }

            foreach (var envelope in page.Releases)
            {
                TenderSnapshot snapshot;
                try
                {
                    snapshot = mapper.ToSnapshot(envelope);
                }
                catch (InvalidDataException ex)
                {
                    logger.LogWarning(ex, "Skipping unmappable release {Ocid}", envelope.Release.Ocid);
                    continue;
                }

                yield return snapshot;
            }
        }

        var stats = client.LastStats;
        LastFetchStats = new SourceFetchStats(stats.Pages, stats.Requests, stats.SkippedReleases, stats.SkippedRanges, drift.Distinct(StringComparer.Ordinal).ToList());
    }

    public async Task<TenderSnapshot?> FetchByOcidAsync(string ocid, CancellationToken cancellationToken = default)
    {
        var envelope = await client.GetReleaseAsync(ocid, cancellationToken);
        return envelope is null ? null : mapper.ToSnapshot(envelope);
    }

    public async Task<SourceProbeResult> ProbeAsync(CancellationToken cancellationToken = default)
    {
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var stopwatch = Stopwatch.StartNew();
        try
        {
            // Small page: a poison row in the first 100 would otherwise turn a healthy API into a failed probe.
            var page = await client.GetPageAsync(today.AddDays(-7), today.AddDays(1), 1, 10, cancellationToken);
            stopwatch.Stop();
            var drift = validator.Validate(page.RawJson);
            var error = page.Releases.Count == 0 ? "The last 7 days returned no releases." : null;
            return new SourceProbeResult(SourceName, error is null, (int)HttpStatusCode.OK, (int)stopwatch.ElapsedMilliseconds, drift, error);
        }
        catch (BrokenCircuitException ex)
        {
            return new SourceProbeResult(SourceName, false, null, (int)stopwatch.ElapsedMilliseconds, [], $"Circuit open: {ex.Message}");
        }
        catch (HttpRequestException ex)
        {
            return new SourceProbeResult(SourceName, false, (int?)ex.StatusCode, (int)stopwatch.ElapsedMilliseconds, [], ex.Message);
        }
        catch (Exception ex) when (ex is TaskCanceledException or TimeoutException or InvalidDataException)
        {
            return new SourceProbeResult(SourceName, false, null, (int)stopwatch.ElapsedMilliseconds, [], ex.Message);
        }
    }
}
