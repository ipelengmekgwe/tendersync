using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using TenderSync.Application.Abstractions;
using TenderSync.Application.Abstractions.Persistence;
using TenderSync.Application.Features.Ingest;
using TenderSync.Domain.Enums;

namespace TenderSync.Application.Features.Status;

public sealed record RefreshSummary(int Candidates, int Changed, int Unchanged, int NotFound, int Failed, IReadOnlyList<string> Errors);

/// <summary>
/// RefreshActiveTendersJob (PLAN.md §7.7): every matched tender that is not finished and closed less than RefreshLookbackDays
/// ago is fetched again by ocid and run through the ingest upsert, so new documents, moved closing dates, awards and
/// cancellations raise the same events as a normal ingest. The least recently checked tenders go first.
/// </summary>
public sealed class TenderStatusRefresher(IReadDb readDb, TenderIngestor ingestor, IOptions<StatusOptions> options, IDateTimeProvider clock, ILogger<TenderStatusRefresher> logger)
{
    private const string CandidatesSql = """
        select t.ocid
        from tendersync.tenders t
        where exists (select 1 from tendersync.tender_matches m where m.tender_id = t.id)
          and t.status not in (@awarded, @cancelled)
          and (t.closing_at is null or t.closing_at >= @since)
        order by t.last_checked_at asc nulls first, t.closing_at asc nulls last
        limit @take
        """;

    public async Task<RefreshSummary> RefreshActiveAsync(CancellationToken cancellationToken = default)
    {
        var settings = options.Value;
        var now = clock.UtcNow;
        var ocids = await readDb.QueryAsync<string>(CandidatesSql, new
        {
            awarded = (int)TenderStatus.Awarded,
            cancelled = (int)TenderStatus.Cancelled,
            since = now.AddDays(-settings.RefreshLookbackDays),
            take = settings.RefreshMaxTenders,
        }, cancellationToken);

        int changed = 0, unchanged = 0, notFound = 0, failed = 0;
        var errors = new List<string>();
        foreach (var ocid in ocids)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                switch (await ingestor.RefreshAsync(ocid, cancellationToken))
                {
                    case null:
                        notFound++;
                        logger.LogWarning("Tender {Ocid} is no longer served by the source", ocid);
                        break;
                    case IngestOutcome.Changed:
                        changed++;
                        break;
                    default:
                        unchanged++;
                        break;
                }
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                failed++;
                if (errors.Count < 20)
                {
                    errors.Add($"{ocid}: {ex.Message}");
                }

                logger.LogError(ex, "Refresh of {Ocid} failed", ocid);
            }

            if (settings.RefreshSpacingMs > 0)
            {
                await Task.Delay(settings.RefreshSpacingMs, cancellationToken);
            }
        }

        logger.LogInformation("Refreshed {Count} matched tender(s): {Changed} changed, {Unchanged} unchanged, {NotFound} gone, {Failed} failed", ocids.Count, changed, unchanged, notFound, failed);
        return new RefreshSummary(ocids.Count, changed, unchanged, notFound, failed, errors);
    }
}
