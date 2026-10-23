using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using TenderSync.Application.Abstractions;
using TenderSync.Domain.Enums;
using TenderSync.Domain.Geography;
using TenderSync.Infrastructure.Persistence;

namespace TenderSync.Infrastructure.Services;

/// <summary>
/// Loads the municipalities table once and answers lookups from memory. Falls back to the embedded CSV when the
/// table is empty (before seeding). Exact normalised match first, then a unique prefix match.
/// </summary>
internal sealed class CachedMunicipalityLookup(IServiceScopeFactory scopeFactory) : IMunicipalityLookup
{
    private readonly Lazy<IReadOnlyDictionary<string, Province>> _map = new(() => Load(scopeFactory), LazyThreadSafetyMode.ExecutionAndPublication);

    public Province? TryResolve(string normalisedName)
    {
        if (string.IsNullOrWhiteSpace(normalisedName))
        {
            return null;
        }

        var map = _map.Value;
        if (map.TryGetValue(normalisedName, out var province))
        {
            return province;
        }

        var candidates = map.Where(kv => kv.Key.StartsWith(normalisedName, StringComparison.Ordinal) || normalisedName.StartsWith(kv.Key, StringComparison.Ordinal))
            .Select(kv => kv.Value)
            .Distinct()
            .ToList();

        return candidates.Count == 1 ? candidates[0] : null;
    }

    private static Dictionary<string, Province> Load(IServiceScopeFactory scopeFactory)
    {
        using var scope = scopeFactory.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        List<(string Name, Province Province)> rows;
        try
        {
            rows = context.MunicipalitySet.AsNoTracking().Select(m => new ValueTuple<string, Province>(m.NormalisedName, m.Province)).ToList();
        }
        catch (Exception ex) when (ex is Npgsql.PostgresException or InvalidOperationException)
        {
            rows = [];
        }

        if (rows.Count == 0)
        {
            rows = Persistence.Seed.DatabaseSeeder.ReadMunicipalityCsv().Select(m => (m.NormalisedName, m.Province)).ToList();
        }

        var map = new Dictionary<string, Province>(StringComparer.Ordinal);
        foreach (var (name, province) in rows)
        {
            map.TryAdd(name, province);
        }

        return map;
    }
}
