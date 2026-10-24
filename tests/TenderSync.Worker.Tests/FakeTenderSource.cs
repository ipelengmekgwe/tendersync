using System.Runtime.CompilerServices;
using TenderSync.Application.Abstractions.Sources;
using TenderSync.Domain.Enums;
using TenderSync.Domain.Tenders;

namespace TenderSync.Worker.Tests;

/// <summary>Replaces the eTenders source in the test host: snapshots by ocid, scripted per test.</summary>
public sealed class FakeTenderSource : ITenderSource
{
    private readonly Dictionary<string, TenderSnapshot> _byOcid = new(StringComparer.Ordinal);

    public TenderSourceKind Kind => TenderSourceKind.ETendersOcds;

    public SourceFetchStats LastFetchStats => SourceFetchStats.Empty;

    public int FetchByOcidCalls { get; private set; }

    public void Set(TenderSnapshot snapshot)
    {
        lock (_byOcid)
        {
            _byOcid[snapshot.Ocid] = snapshot;
        }
    }

    public void Remove(string ocid)
    {
        lock (_byOcid)
        {
            _byOcid.Remove(ocid);
        }
    }

    public async IAsyncEnumerable<TenderSnapshot> FetchAsync(DateOnly from, DateOnly to, [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        List<TenderSnapshot> all;
        lock (_byOcid)
        {
            all = [.. _byOcid.Values];
        }

        foreach (var snapshot in all)
        {
            yield return snapshot;
        }

        await Task.CompletedTask;
    }

    public Task<TenderSnapshot?> FetchByOcidAsync(string ocid, CancellationToken cancellationToken = default)
    {
        FetchByOcidCalls++;
        lock (_byOcid)
        {
            return Task.FromResult(_byOcid.GetValueOrDefault(ocid));
        }
    }

    public Task<SourceProbeResult> ProbeAsync(CancellationToken cancellationToken = default) =>
        Task.FromResult(new SourceProbeResult("fake", true, 200, 1, [], null));
}
