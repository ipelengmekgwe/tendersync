using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using TenderSync.Application.Abstractions;
using TenderSync.Application.Abstractions.Persistence;
using TenderSync.Application.Features.Ingest;
using TenderSync.Domain.Common;
using TenderSync.Domain.Enums;
using TenderSync.Domain.Events;
using TenderSync.Infrastructure.ETenders;
using TenderSync.Infrastructure.Persistence;

namespace TenderSync.Infrastructure.Tests.ETenders;

/// <summary>The real eTenders source over scripted HTTP, feeding the real ingestor into a real PostgreSQL. Acceptance for WP03.</summary>
[Collection(PostgresCollection.Name)]
[Trait("Category", "Integration")]
public class TenderIngestorTests(PostgresFixture fixture)
{
    private static readonly DateOnly From = new(2026, 10, 15);
    private static readonly DateOnly To = new(2026, 10, 23);

    private async Task<IngestSummary> IngestAsync(ScriptedHandler handler)
    {
        await using var http = ETendersTestServices.Build(handler);
        var source = new ETendersOcdsSource(
            http.GetRequiredService<ETendersClient>(),
            http.GetRequiredService<ReleaseMapper>(),
            http.GetRequiredService<ReleaseSchemaValidator>(),
            NullLogger<ETendersOcdsSource>.Instance);

        using var scope = fixture.CreateScope();
        var ingestor = new TenderIngestor(
            source,
            scope.ServiceProvider.GetRequiredService<IAppDbContext>(),
            scope.ServiceProvider.GetRequiredService<IMunicipalityLookup>(),
            scope.ServiceProvider.GetRequiredService<IDateTimeProvider>(),
            NullLogger<TenderIngestor>.Instance);

        return await ingestor.IngestWindowAsync(From, To);
    }

    [Fact]
    public async Task Replaying_the_recorded_pages_is_idempotent()
    {
        var expected = Fixtures.DistinctOcidsInPage1();

        var first = await IngestAsync(ScriptedHandler.TwoPages());
        var second = await IngestAsync(ScriptedHandler.TwoPages());

        first.Fetched.Should().Be(103, "100 rows on page 1 and 3 on page 2");
        first.Failed.Should().Be(0, string.Join("; ", first.Errors));
        (first.Created + first.Unchanged).Should().Be(103);
        first.Created.Should().BeGreaterThanOrEqualTo(expected, "every distinct ocid on page 1 is new the first time");

        second.Created.Should().Be(0);
        second.Changed.Should().Be(0);
        second.Unchanged.Should().Be(103);

        using var scope = fixture.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var ocids = ETendersClient.ParsePage(1, Fixtures.Page1()).Releases.Select(r => r.Release.Ocid!).ToArray();
        (await context.TenderSet.CountAsync(t => ocids.Contains(t.Ocid))).Should().Be(expected, "no duplicates on replay");
        (await context.TenderReleaseSet.CountAsync(r => context.TenderSet.Any(t => t.Id == r.TenderId && ocids.Contains(t.Ocid)))).Should().Be(expected, "one release row per unchanged replay");

        var firstSeen = (await context.OutboxMessageSet.AsNoTracking().Where(m => m.Type == typeof(TenderFirstSeen).FullName).ToListAsync())
            .Select(m => DomainJson.Deserialize<TenderFirstSeen>(m.PayloadJson)!)
            .Count(e => ocids.Contains(e.Ocid));
        firstSeen.Should().Be(expected, "TenderFirstSeen is raised once per tender, never on replay");
    }

    [Fact]
    public async Task A_changed_release_produces_a_TenderChanged_event_with_the_diff()
    {
        await IngestAsync(ScriptedHandler.TwoPages());
        var (modifiedPage1, ocid, newClosing) = Fixtures.Page1WithMovedClosing();
        var handler = new ScriptedHandler((request, _) => ScriptedHandler.Json(ScriptedHandler.PageNumber(request) == 1 ? modifiedPage1 : Fixtures.Page2Terminal()));

        var summary = await IngestAsync(handler);

        summary.Changed.Should().Be(1);
        summary.Unchanged.Should().Be(102);

        using var scope = fixture.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var tender = await context.TenderSet.AsNoTracking().SingleAsync(t => t.Ocid == ocid);
        tender.ClosingAt.Should().Be(newClosing);
        (await context.TenderReleaseSet.CountAsync(r => r.TenderId == tender.Id)).Should().Be(2);

        var changed = (await context.OutboxMessageSet.AsNoTracking().Where(m => m.Type == typeof(TenderChanged).FullName).ToListAsync())
            .Select(m => DomainJson.Deserialize<TenderChanged>(m.PayloadJson)!)
            .Where(e => e.Ocid == ocid)
            .ToList();
        changed.Should().ContainSingle().Which.Diff.Should().Match<Domain.Tenders.ReleaseDiff>(d => d.ClosingChanged && d.NewClosingAt == newClosing && d.NewDocumentIds.Count == 0);
    }

    [Fact]
    public async Task Buyers_get_a_type_and_province_from_their_name()
    {
        await IngestAsync(ScriptedHandler.TwoPages());

        using var scope = fixture.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var westernCape = await context.BuyerSet.AsNoTracking().SingleAsync(b => b.ExternalId == "723");
        westernCape.Type.Should().Be(BuyerType.Provincial);
        westernCape.Province.Should().Be(Province.WesternCape);

        var eskom = await context.BuyerSet.AsNoTracking().SingleAsync(b => b.Name == "ESKOM");
        eskom.Type.Should().Be(BuyerType.StateOwnedEntity);
        eskom.Province.Should().Be(Province.National);

        var tzaneen = await context.BuyerSet.AsNoTracking().SingleAsync(b => b.Name == "Greater Tzaneen Municipality");
        tzaneen.Type.Should().Be(BuyerType.Municipal);
        tzaneen.Province.Should().Be(Province.Limpopo);

        var tenders = await context.TenderSet.AsNoTracking().Where(t => t.BuyerId == westernCape.Id).ToListAsync();
        tenders.Should().AllSatisfy(t => t.Province.Should().Be(Province.WesternCape));
        tenders.Should().AllSatisfy(t => t.Status.Should().NotBe(TenderStatus.Unknown, "closing dates are present so a status is derived"));
    }
}
