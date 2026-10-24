using System.Net;
using System.Text.Json.Nodes;
using Microsoft.Extensions.DependencyInjection;
using TenderSync.Infrastructure.ETenders;

namespace TenderSync.Infrastructure.Tests.ETenders;

/// <summary>
/// The real API returns HTTP 500 for any page that contains certain releases (verified 24 Oct 2026). This virtual API serves
/// the 103 recorded releases at any PageNumber/PageSize and poisons one of them, so bisecting and skipping can be proven.
/// </summary>
public sealed class VirtualApi
{
    private readonly List<JsonNode> _rows;

    public VirtualApi(int poisonIndex)
    {
        var page1 = JsonNode.Parse(Fixtures.Page1())!["releases"]!.AsArray().Select(r => r!.DeepClone()).ToList();
        var page2 = JsonNode.Parse(Fixtures.Read(Fixtures.Page2File))!["releases"]!.AsArray().Select(r => r!.DeepClone()).ToList();
        _rows = [.. page1, .. page2];
        PoisonIndex = poisonIndex;
    }

    public int PoisonIndex { get; }

    public int Total => _rows.Count;

    public int DistinctOcids => _rows.Select(Ocid).Distinct(StringComparer.Ordinal).Count();

    /// <summary>True when the poisoned row's ocid appears nowhere else, so skipping it removes one distinct ocid.</summary>
    public bool PoisonIsUnique => _rows.Count(r => Ocid(r) == Ocid(_rows[PoisonIndex])) == 1;

    private static string Ocid(JsonNode row) => row["ocid"]!.GetValue<string>();

    public List<(int Page, int Size, HttpStatusCode Status)> Served { get; } = [];

    public HttpResponseMessage Respond(HttpRequestMessage request)
    {
        var query = System.Web.HttpUtility.ParseQueryString(request.RequestUri!.Query);
        var page = int.Parse(query["PageNumber"]!, System.Globalization.CultureInfo.InvariantCulture);
        var size = int.Parse(query["PageSize"]!, System.Globalization.CultureInfo.InvariantCulture);
        var start = (page - 1) * size;
        var slice = _rows.Skip(start).Take(size).ToList();

        if (PoisonIndex >= start && PoisonIndex < start + size && slice.Count > 0)
        {
            Served.Add((page, size, HttpStatusCode.InternalServerError));
            return ScriptedHandler.Status(HttpStatusCode.InternalServerError);
        }

        Served.Add((page, size, HttpStatusCode.OK));
        var package = new JsonObject
        {
            ["version"] = "1.1",
            ["releases"] = new JsonArray(slice.Select(r => r.DeepClone()).ToArray()),
            ["links"] = new JsonObject { ["next"] = start + size < _rows.Count ? $"https://ocds-api.etenders.gov.za/api/OCDSReleases?PageNumber={page + 1}&PageSize={size}" : null },
        };
        return ScriptedHandler.Json(package.ToJsonString());
    }
}

public class PoisonRowTests
{
    private static readonly DateOnly From = new(2026, 10, 15);
    private static readonly DateOnly To = new(2026, 10, 23);

    [Fact]
    public async Task A_poisoned_page_is_bisected_and_only_the_poison_row_is_skipped()
    {
        var api = new VirtualApi(poisonIndex: 37);
        var handler = new ScriptedHandler((request, _) => api.Respond(request));
        await using var provider = ETendersTestServices.Build(handler);
        var client = provider.GetRequiredService<ETendersClient>();

        var releases = new List<OcdsReleaseEnvelope>();
        await foreach (var page in client.GetPagesAsync(From, To))
        {
            releases.AddRange(page.Releases);
        }

        releases.Should().HaveCount(api.Total - 1, "every release except the poison row is served");
        releases.Select(r => r.Release.Ocid).Distinct().Should().HaveCount(api.DistinctOcids - (api.PoisonIsUnique ? 1 : 0), "the two recorded pages overlap on three ocids");
        client.LastStats.SkippedReleases.Should().Be(1);
        client.LastStats.SkippedRanges.Should().ContainSingle().Which.Should().EndWith("@37");

        // 500 is deterministic, so it must not be retried: exactly one request per poisoned page/sub-page.
        api.Served.Where(s => s.Status == HttpStatusCode.InternalServerError).Should().HaveCount(4, "size 100, then 10, then 5, then 1");
        api.Served.Should().Contain((4, 10, HttpStatusCode.InternalServerError)).And.Contain((8, 5, HttpStatusCode.InternalServerError)).And.Contain((38, 1, HttpStatusCode.InternalServerError));
    }

    [Fact]
    public async Task The_whole_source_still_maps_and_reports_the_skip()
    {
        var api = new VirtualApi(poisonIndex: 0);
        var handler = new ScriptedHandler((request, _) => api.Respond(request));
        await using var provider = ETendersTestServices.Build(handler);
        var source = new ETendersOcdsSource(
            provider.GetRequiredService<ETendersClient>(),
            provider.GetRequiredService<ReleaseMapper>(),
            provider.GetRequiredService<ReleaseSchemaValidator>(),
            Microsoft.Extensions.Logging.Abstractions.NullLogger<ETendersOcdsSource>.Instance);

        var count = 0;
        await foreach (var _ in source.FetchAsync(From, To))
        {
            count++;
        }

        count.Should().Be(api.Total - 1);
        source.LastFetchStats.SkippedReleases.Should().Be(1);
        source.LastFetchStats.SchemaDrift.Should().BeEmpty();
    }

    [Theory]
    [InlineData(100, 10)]
    [InlineData(50, 10)]
    [InlineData(25, 5)]
    [InlineData(10, 5)]
    [InlineData(7, 1)]
    [InlineData(2, 1)]
    public void Sub_page_sizes_tile_the_parent(int size, int expected)
    {
        ETendersClient.SubPageSize(size).Should().Be(expected);
    }
}
