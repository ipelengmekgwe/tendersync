using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using TenderSync.Application.Abstractions.Persistence;
using TenderSync.Domain.Enums;
using TenderSync.Domain.Tenders;
using TenderSync.Infrastructure.Persistence;

namespace TenderSync.Infrastructure.Tests.Persistence;

[Collection(PostgresCollection.Name)]
[Trait("Category", "Integration")]
public class TenderPersistenceTests(PostgresFixture fixture)
{
    private static readonly DateTimeOffset Now = new(2026, 10, 23, 12, 0, 0, TimeSpan.Zero);

    private static TenderSnapshot Snapshot(string ocid, DateTimeOffset? closingAt = null, IReadOnlyList<TenderDocumentSnapshot>? documents = null) =>
        new(
            Ocid: ocid,
            ExternalTenderId: ocid.Split('-')[^1],
            BidNumber: $"BID/{ocid[^6..]}",
            Title: "Supply and delivery of comfort packs",
            Description: "SUPPLEMENTARY BID FOR THE SUPPLY AND DELIVERY OF COMFORT PACKS TO DISTRICT OFFICES",
            BuyerExternalId: "723",
            BuyerName: "Western Cape - Health",
            MainCategory: ProcurementCategory.Goods,
            AdditionalCategories: ["goods"],
            ProcurementMethod: "open",
            ProcurementMethodDetails: "Request for Bid(Open-Tender)",
            ValueAmount: 0,
            ValueCurrency: "ZAR",
            PublishedAt: Now,
            ClosingAt: closingAt ?? Now.AddDays(28),
            SourceUrl: $"https://www.etenders.gov.za/Home/TenderDetails?Id={ocid[^6..]}",
            ReleaseId: $"{ocid}-2026/10/23 00:00:00",
            ReleaseDate: Now,
            ReleaseTag: "compiled",
            RawJson: $$"""{"ocid":"{{ocid}}","tag":["compiled"]}""",
            Documents: documents ?? [new TenderDocumentSnapshot("doc-1", "Bid Document.pdf", "https://www.etenders.gov.za/home/Download?blobName=doc-1.pdf", "pdf", "en", Now, Now)],
            HasAwards: false);

    private async Task<Guid> SaveNewTenderAsync(string ocid, DateTimeOffset? closingAt = null)
    {
        using var scope = fixture.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<IAppDbContext>();

        var buyer = await db.Buyers.GetByExternalIdAsync("723");
        if (buyer is null)
        {
            buyer = Buyer.Create("723", "Western Cape - Health", BuyerType.Provincial, Province.WesternCape);
            db.Buyers.Add(buyer);
        }

        var tender = Tender.CreateFromSnapshot(Snapshot(ocid, closingAt), buyer.Id, Province.WesternCape, TenderSourceKind.ETendersOcds, Now);
        db.Tenders.Add(tender);
        await db.SaveChangesAsync();
        return tender.Id;
    }

    [Fact]
    public async Task Saving_a_tender_persists_its_documents_release_and_an_outbox_row()
    {
        var ocid = $"ocds-9t57fa-{Random.Shared.Next(100000, 999999)}";
        var id = await SaveNewTenderAsync(ocid);

        using var scope = fixture.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<IAppDbContext>();
        var context = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var loaded = await db.Tenders.GetByOcidAsync(ocid);
        loaded.Should().NotBeNull();
        loaded!.Id.Should().Be(id);
        loaded.Buyer!.Name.Should().Be("Western Cape - Health");
        loaded.Documents.Should().ContainSingle(d => d.ExternalId == "doc-1");
        loaded.Releases.Should().ContainSingle();
        loaded.CreatedAt.Should().NotBe(default);
        loaded.DomainEvents.Should().BeEmpty("events are cleared once written to the outbox");

        var outbox = await OutboxRowsForAsync(context, ocid, "TenderSync.Domain.Events.TenderFirstSeen");
        outbox.Should().ContainSingle();
        outbox[0].ProcessedAt.Should().BeNull();
    }

    [Fact]
    public async Task Inserting_the_same_ocid_twice_violates_the_unique_index()
    {
        var ocid = $"ocds-9t57fa-{Random.Shared.Next(100000, 999999)}";
        await SaveNewTenderAsync(ocid);

        var act = () => SaveNewTenderAsync(ocid);

        await act.Should().ThrowAsync<DbUpdateException>();
    }

    [Fact]
    public async Task Replaying_an_identical_release_adds_nothing()
    {
        var ocid = $"ocds-9t57fa-{Random.Shared.Next(100000, 999999)}";
        await SaveNewTenderAsync(ocid);

        using (var scope = fixture.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<IAppDbContext>();
            var tender = (await db.Tenders.GetByOcidAsync(ocid))!;
            var diff = tender.ApplyRelease(Snapshot(ocid), Province.WesternCape, Now.AddHours(1));
            diff.Should().Be(ReleaseDiff.Unchanged);
            await db.SaveChangesAsync();
        }

        using (var scope = fixture.CreateScope())
        {
            var context = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            (await context.TenderReleaseSet.CountAsync(r => r.ReleaseId.StartsWith(ocid))).Should().Be(1);
            (await OutboxRowsForAsync(context, ocid)).Should().HaveCount(1, "only TenderFirstSeen was raised");
        }
    }

    [Fact]
    public async Task A_changed_release_writes_a_TenderChanged_outbox_row_and_a_second_release()
    {
        var ocid = $"ocds-9t57fa-{Random.Shared.Next(100000, 999999)}";
        await SaveNewTenderAsync(ocid);

        using (var scope = fixture.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<IAppDbContext>();
            var tender = (await db.Tenders.GetByOcidAsync(ocid))!;
            var diff = tender.ApplyRelease(Snapshot(ocid, closingAt: Now.AddDays(45)), Province.WesternCape, Now.AddDays(1));
            diff.ClosingChanged.Should().BeTrue();
            await db.SaveChangesAsync();
        }

        using (var scope = fixture.CreateScope())
        {
            var context = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            (await context.TenderReleaseSet.CountAsync(r => r.ReleaseId.StartsWith(ocid))).Should().Be(2);
            (await OutboxRowsForAsync(context, ocid, "TenderSync.Domain.Events.TenderChanged")).Should().ContainSingle();
        }
    }

    /// <summary>jsonb columns do not support LIKE, so the payload filter runs client-side on the (small) test table.</summary>
    private static async Task<List<Domain.Notifications.OutboxMessage>> OutboxRowsForAsync(AppDbContext context, string ocid, string? type = null)
    {
        var query = context.OutboxMessageSet.AsNoTracking();
        if (type is not null)
        {
            query = query.Where(m => m.Type == type);
        }

        var rows = await query.ToListAsync();
        return rows.Where(m => m.PayloadJson.Contains(ocid, StringComparison.Ordinal)).ToList();
    }

    [Fact]
    public async Task Full_text_search_finds_tenders_by_description()
    {
        var ocid = $"ocds-9t57fa-{Random.Shared.Next(100000, 999999)}";
        await SaveNewTenderAsync(ocid);

        using var scope = fixture.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<IReadDb>();

        var hits = await db.QueryAsync<string>(
            "select ocid from tendersync.tenders where search_vector @@ plainto_tsquery('english', @q) and ocid = @ocid",
            new { q = "comfort packs district", ocid });

        hits.Should().Equal(ocid);
    }

    [Fact]
    public async Task Document_texts_can_be_replaced_per_document()
    {
        var ocid = $"ocds-9t57fa-{Random.Shared.Next(100000, 999999)}";
        await SaveNewTenderAsync(ocid);

        using var scope = fixture.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<IAppDbContext>();
        var tender = (await db.Tenders.GetByOcidAsync(ocid))!;
        var documentId = tender.Documents.Single().Id;

        await db.Tenders.ReplaceDocumentTextsAsync(documentId, [new DocumentText(documentId, 1, "Page one"), new DocumentText(documentId, 2, "Page two")]);
        await db.SaveChangesAsync();
        await db.Tenders.ReplaceDocumentTextsAsync(documentId, [new DocumentText(documentId, 1, "Page one, revised")]);
        await db.SaveChangesAsync();

        var pages = await db.Tenders.GetDocumentTextsAsync(documentId);
        pages.Should().ContainSingle().Which.Text.Should().Be("Page one, revised");
    }
}
