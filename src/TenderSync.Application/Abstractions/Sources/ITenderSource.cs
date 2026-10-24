using TenderSync.Domain.Enums;
using TenderSync.Domain.Tenders;

namespace TenderSync.Application.Abstractions.Sources;

/// <summary>
/// A place tenders come from (ADR-014). v1 has one implementation over the eTenders OCDS API; municipal and SOE portal
/// scrapers plug in here later. Implementations map their native format to <see cref="TenderSnapshot"/>.
/// </summary>
public interface ITenderSource
{
    TenderSourceKind Kind { get; }

    /// <summary>Streams every release published in the window, newest first, page by page.</summary>
    IAsyncEnumerable<TenderSnapshot> FetchAsync(DateOnly from, DateOnly to, CancellationToken cancellationToken = default);

    /// <summary>What the last <see cref="FetchAsync"/> enumeration encountered: pages, rows the source could not serve, schema drift.</summary>
    SourceFetchStats LastFetchStats { get; }

    /// <summary>The current compiled release for one contracting process, or null when the source does not know it.</summary>
    Task<TenderSnapshot?> FetchByOcidAsync(string ocid, CancellationToken cancellationToken = default);

    /// <summary>Reachability, latency and schema-drift probe for the source health view.</summary>
    Task<SourceProbeResult> ProbeAsync(CancellationToken cancellationToken = default);
}

public sealed record SourceFetchStats(int Pages, int Requests, int SkippedReleases, IReadOnlyList<string> SkippedRanges, IReadOnlyList<string> SchemaDrift)
{
    public static SourceFetchStats Empty { get; } = new(0, 0, 0, [], []);
}

public sealed record SourceProbeResult(string Source, bool Ok, int? HttpStatus, int LatencyMs, IReadOnlyList<string> SchemaDrift, string? Error)
{
    public bool HasDrift => SchemaDrift.Count > 0;
}
