using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using TenderSync.Application.Abstractions;
using TenderSync.Application.Abstractions.Persistence;
using TenderSync.Domain.Enums;

namespace TenderSync.Application.Features.Status;

public sealed record ComputeStatusSummary(int Examined, int Transitioned, IReadOnlyDictionary<string, int> Transitions);

/// <summary>
/// ComputeStatusJob (PLAN.md §7.7, hourly): time alone moves a tender from Open to ClosingSoon to Closed, so every tender
/// whose derived status may have changed since it was last saved is re-derived. Each transition lands in
/// tender_status_history and raises TenderStatusChanged through the entity.
/// </summary>
public sealed class StatusComputer(IAppDbContext db, IReadDb readDb, IOptions<StatusOptions> options, IDateTimeProvider clock, ILogger<StatusComputer> logger)
{
    private const string CandidatesSql = """
        select t.id
        from tendersync.tenders t
        where (t.status = @open and t.closing_at <= @soonUntil)
           or (t.status in (@open, @closingSoon) and t.closing_at <= @now)
           or (t.status = @unknown and t.closing_at is not null)
           or (t.status in (@open, @closingSoon, @unknown) and t.has_awards)
        order by t.closing_at asc nulls last
        limit @take
        """;

    public async Task<ComputeStatusSummary> ComputeAsync(CancellationToken cancellationToken = default)
    {
        var settings = options.Value;
        var now = clock.UtcNow;
        var window = TimeSpan.FromDays(settings.ClosingSoonDays);
        var ids = await readDb.QueryAsync<Guid>(CandidatesSql, new
        {
            open = (int)TenderStatus.Open,
            closingSoon = (int)TenderStatus.ClosingSoon,
            unknown = (int)TenderStatus.Unknown,
            now,
            soonUntil = now + window,
            take = settings.ComputeBatchSize,
        }, cancellationToken);

        var transitions = new Dictionary<string, int>(StringComparer.Ordinal);
        var transitioned = 0;
        foreach (var id in ids)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var tender = await db.Tenders.GetWithDetailsAsync(id, cancellationToken);
            if (tender is null)
            {
                continue;
            }

            var before = tender.Status;
            var after = tender.DeriveStatus(now, window);
            if (before != after)
            {
                transitioned++;
                var key = $"{before}->{after}";
                transitions[key] = transitions.GetValueOrDefault(key) + 1;
                await db.SaveChangesAsync(cancellationToken);
            }
        }

        logger.LogInformation("Status computed for {Examined} tender(s): {Transitioned} transition(s) {Transitions}", ids.Count, transitioned, string.Join(", ", transitions.Select(t => $"{t.Key} ×{t.Value}")));
        return new ComputeStatusSummary(ids.Count, transitioned, transitions);
    }
}
