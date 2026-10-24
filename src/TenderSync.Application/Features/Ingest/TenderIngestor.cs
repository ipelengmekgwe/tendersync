using Microsoft.Extensions.Logging;
using TenderSync.Application.Abstractions;
using TenderSync.Application.Abstractions.Persistence;
using TenderSync.Application.Abstractions.Sources;
using TenderSync.Domain.Enums;
using TenderSync.Domain.Geography;
using TenderSync.Domain.Tenders;

namespace TenderSync.Application.Features.Ingest;

public enum IngestOutcome
{
    Created = 0,
    Changed = 1,
    Unchanged = 2,
    Failed = 3,
}

public sealed record IngestSummary(
    DateOnly From,
    DateOnly To,
    int Fetched,
    int Created,
    int Changed,
    int Unchanged,
    int Failed,
    int SkippedBySource,
    int Pages,
    IReadOnlyList<string> Errors,
    IReadOnlyList<string> SkippedRanges,
    IReadOnlyList<string> SchemaDrift)
{
    public int Processed => Created + Changed + Unchanged;
}

/// <summary>
/// Upserts tender snapshots from a source (PLAN.md §7.1): buyer resolution, create-or-apply release with content-hash
/// change detection, status derivation, one transaction per tender so the outbox rows travel with the change.
/// Idempotent: replaying a window creates no rows and raises no events.
/// </summary>
public sealed class TenderIngestor(ITenderSource source, IAppDbContext db, IMunicipalityLookup municipalities, IDateTimeProvider clock, ILogger<TenderIngestor> logger)
{
    public async Task<IngestSummary> IngestWindowAsync(DateOnly from, DateOnly to, CancellationToken cancellationToken = default)
    {
        int fetched = 0, created = 0, changed = 0, unchanged = 0, failed = 0;
        var errors = new List<string>();

        await foreach (var snapshot in source.FetchAsync(from, to, cancellationToken))
        {
            fetched++;
            try
            {
                switch (await UpsertAsync(snapshot, cancellationToken))
                {
                    case IngestOutcome.Created: created++; break;
                    case IngestOutcome.Changed: changed++; break;
                    default: unchanged++; break;
                }
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                failed++;
                if (errors.Count < 20)
                {
                    errors.Add($"{snapshot.Ocid}: {ex.Message}");
                }

                logger.LogError(ex, "Failed to ingest {Ocid}", snapshot.Ocid);
            }
        }

        var stats = source.LastFetchStats;
        var summary = new IngestSummary(from, to, fetched, created, changed, unchanged, failed, stats.SkippedReleases, stats.Pages, errors, stats.SkippedRanges, stats.SchemaDrift);
        logger.LogInformation(
            "Ingest {From}..{To}: {Pages} page(s), fetched {Fetched}, created {Created}, changed {Changed}, unchanged {Unchanged}, failed {Failed}, skipped by source {Skipped}",
            from, to, stats.Pages, fetched, created, changed, unchanged, failed, stats.SkippedReleases);
        return summary;
    }

    /// <summary>Re-fetches one tender from the source and applies it (used by status refresh in WP11).</summary>
    public async Task<IngestOutcome?> RefreshAsync(string ocid, CancellationToken cancellationToken = default)
    {
        var snapshot = await source.FetchByOcidAsync(ocid, cancellationToken);
        return snapshot is null ? null : await UpsertAsync(snapshot, cancellationToken);
    }

    public async Task<IngestOutcome> UpsertAsync(TenderSnapshot snapshot, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        var now = clock.UtcNow;
        var resolution = ProvinceResolver.Resolve(snapshot.BuyerName, municipalities.TryResolve);

        var buyer = await db.Buyers.GetByExternalIdAsync(snapshot.BuyerExternalId, cancellationToken);
        if (buyer is null)
        {
            buyer = Buyer.Create(snapshot.BuyerExternalId, snapshot.BuyerName, resolution.Type, resolution.Province);
            db.Buyers.Add(buyer);
        }
        else if (!string.Equals(buyer.Name, snapshot.BuyerName, StringComparison.Ordinal) || buyer.Type == BuyerType.Unknown || buyer.Province == Province.Unknown)
        {
            buyer.Update(snapshot.BuyerName, resolution.Type, resolution.Province);
        }

        var tender = await db.Tenders.GetByOcidAsync(snapshot.Ocid, cancellationToken);
        IngestOutcome outcome;
        if (tender is null)
        {
            tender = Tender.CreateFromSnapshot(snapshot, buyer.Id, resolution.Province, source.Kind, now);
            tender.DeriveStatus(now);
            db.Tenders.Add(tender);
            outcome = IngestOutcome.Created;
        }
        else
        {
            var diff = tender.ApplyRelease(snapshot, resolution.Province, now);
            tender.DeriveStatus(now);
            outcome = diff.HasChanges ? IngestOutcome.Changed : IngestOutcome.Unchanged;
        }

        await db.SaveChangesAsync(cancellationToken);
        return outcome;
    }
}
