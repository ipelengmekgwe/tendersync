using System.Globalization;
using TenderSync.Application.Abstractions.Jobs;
using TenderSync.Application.Abstractions.Persistence;

namespace TenderSync.Infrastructure.Jobs;

/// <summary>
/// Reads recurring jobs straight from Hangfire.PostgreSql's tables (schema <c>hangfire</c>): the <c>recurring-jobs</c> set
/// and one <c>recurring-job:{id}</c> hash each. The API has no Hangfire storage of its own, so this is read-only SQL; the
/// Worker owns registration and execution. Missing tables (Worker never started) read as no jobs.
/// </summary>
internal sealed class HangfireRecurringJobCatalog(IReadDb db) : IRecurringJobCatalog
{
    private const string Sql = """
        select s.value as id, h.field, h.value
        from hangfire.set s
        left join hangfire.hash h on h.key = 'recurring-job:' || s.value
        where s.key = 'recurring-jobs'
        """;

    private const string StatesSql = """
        select id::text as id, statename as state from hangfire.job where id::text = any(@ids)
        """;

    private const string TablesExistSql = """
        select to_regclass('hangfire.set') is not null and to_regclass('hangfire.hash') is not null and to_regclass('hangfire.job') is not null
        """;

    public async Task<IReadOnlyList<RecurringJobInfo>> ListAsync(CancellationToken cancellationToken = default)
    {
        if (!await db.ExecuteScalarAsync<bool>(TablesExistSql, null, cancellationToken))
        {
            return [];
        }

        var rows = await db.QueryAsync<Row>(Sql, null, cancellationToken);
        var jobs = rows.GroupBy(r => r.Id, StringComparer.Ordinal)
            .Select(g => (Id: g.Key, Fields: g.Where(r => r.Field is not null).ToDictionary(r => r.Field!, r => r.Value, StringComparer.Ordinal)))
            .ToList();

        var lastIds = jobs.Select(j => j.Fields.GetValueOrDefault("LastJobId")).OfType<string>().ToArray();
        var states = lastIds.Length == 0
            ? new Dictionary<string, string?>(StringComparer.Ordinal)
            : (await db.QueryAsync<StateRow>(StatesSql, new { ids = lastIds }, cancellationToken)).ToDictionary(s => s.Id, s => s.State, StringComparer.Ordinal);

        return jobs
            .Select(j => new RecurringJobInfo(
                j.Id,
                j.Fields.GetValueOrDefault("Cron") ?? string.Empty,
                j.Fields.GetValueOrDefault("TimeZoneId"),
                j.Fields.GetValueOrDefault("Queue"),
                ParseTime(j.Fields.GetValueOrDefault("NextExecution")),
                ParseTime(j.Fields.GetValueOrDefault("LastExecution")),
                j.Fields.GetValueOrDefault("LastJobId") is { } last ? states.GetValueOrDefault(last) : null,
                j.Fields.GetValueOrDefault("Error")))
            .OrderBy(j => j.Id, StringComparer.Ordinal)
            .ToList();
    }

    /// <summary>Hangfire writes times as Unix milliseconds (storage v2) or as ISO strings (older entries).</summary>
    private static DateTimeOffset? ParseTime(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        if (long.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var ms))
        {
            return DateTimeOffset.FromUnixTimeMilliseconds(ms);
        }

        return DateTimeOffset.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out var parsed) ? parsed : null;
    }

    private sealed class Row
    {
        public string Id { get; init; } = string.Empty;

        public string? Field { get; init; }

        public string? Value { get; init; }
    }

    private sealed class StateRow
    {
        public string Id { get; init; } = string.Empty;

        public string? State { get; init; }
    }
}
