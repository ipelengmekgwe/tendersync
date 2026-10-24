using System.Net;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using TenderSync.Application.Abstractions.Persistence;
using TenderSync.Application.Common.Models;
using TenderSync.Application.Features.Classification;
using TenderSync.Application.Features.Llm;
using TenderSync.Application.Features.Tenders;
using TenderSync.Domain.Companies;
using TenderSync.Domain.Enums;
using TenderSync.Domain.Events;
using TenderSync.Domain.Tenders;
using TenderSync.Infrastructure.Persistence;

namespace TenderSync.Api.Tests;

/// <summary>Reclassify, the category dry run and the near-miss filter, with the classify purpose routed to the fake model.</summary>
[Collection(ApiCollection.Name)]
[Trait("Category", "Integration")]
public class ClassificationEndpointTests(ApiFactory factory)
{
    private static readonly DateTimeOffset Now = DateTimeOffset.UtcNow;
    private static readonly System.Text.Json.JsonSerializerOptions WebJson = new(System.Text.Json.JsonSerializerDefaults.Web);

    private async Task<Guid> InsertTenderAsync(string title, string description)
    {
        var ocid = $"ocds-9t57fa-{Random.Shared.Next(100000, 999999)}";
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<IAppDbContext>();
        var buyer = await db.Buyers.GetByExternalIdAsync("167");
        if (buyer is null)
        {
            buyer = Buyer.Create("167", "ESKOM", BuyerType.StateOwnedEntity, Province.National);
            db.Buyers.Add(buyer);
        }

        var snapshot = new TenderSnapshot(
            ocid, ocid[^6..], $"E{ocid[^6..]}", title, description, "167", "ESKOM",
            ProcurementCategory.Services, ["services"], "open", "Request for Quotation", null, "ZAR", Now, Now.AddDays(21),
            $"https://www.etenders.gov.za/Home/TenderDetails?Id={ocid[^6..]}", $"{ocid}-release", Now, "compiled", "{}", [], false);
        var tender = Tender.CreateFromSnapshot(snapshot, buyer.Id, Province.National, TenderSourceKind.ETendersOcds, Now);
        tender.DeriveStatus(Now);
        db.Tenders.Add(tender);
        await db.SaveChangesAsync();
        return tender.Id;
    }

    private async Task EnsureProfileAsync()
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<IAppDbContext>();
        if ((await db.CompanyProfiles.ListActiveAsync()).Count == 0)
        {
            var profile = CompanyProfile.Create("Acme Software");
            profile.UpdateCapabilities(new CompanyCapabilities { CategoryCodes = ["SW_DEV", "SYS_INT"] });
            db.CompanyProfiles.Add(profile);
            await db.SaveChangesAsync();
        }
    }

    [Fact]
    public async Task Reclassify_runs_both_stages_creates_a_match_and_raises_the_event()
    {
        await EnsureProfileAsync();
        var tenderId = await InsertTenderAsync("Development of a custom software solution for permit management", "Bespoke software development, SAP integration and three years of application support.");
        factory.Services.GetRequiredService<FakeLlmClient>().Answer("classify", new ClassificationAnswer([new("SW_DEV", 0.95), new("SYS_INT", 0.8)], 91, true, "Bespoke development.", []));

        using var viewer = await factory.CreateViewerClientAsync();
        (await viewer.PostAsync($"/api/v1/tenders/{tenderId}/reclassify", null)).StatusCode.Should().Be(HttpStatusCode.Forbidden);

        using var admin = await factory.CreateAdminClientAsync();
        var response = await admin.PostAsync($"/api/v1/tenders/{tenderId}/reclassify", null);
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var outcome = await response.Content.ReadFromJsonAsync<ClassificationOutcome>();
        outcome!.Stage.Should().Be(ClassificationStage.Llm);
        outcome.Relevance.Should().Be(91);
        outcome.CategoryCodes.Should().Equal("SW_DEV", "SYS_INT");
        outcome.MatchesCreated.Should().Be(1);

        var detail = await admin.GetFromJsonAsync<TenderDetailDto>($"/api/v1/tenders/{tenderId}");
        detail!.Classification!.Relevance.Should().Be(91);
        detail.Classification.Stage.Should().Be(ClassificationStage.Llm);

        var list = await admin.GetFromJsonAsync<PagedResult<TenderListItemDto>>("/api/v1/tenders?matchedOnly=true&minRelevance=90&pageSize=100");
        list!.Items.Should().Contain(t => t.Id == tenderId && t.MatchStatus == MatchStatus.New && t.CategoryCodes.Contains("SW_DEV"));

        using var scope = factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        (await context.OutboxMessageSet.AsNoTracking().Where(m => m.Type == typeof(TenderMatched).FullName).ToListAsync()).Should().Contain(m => m.PayloadJson.Contains(tenderId.ToString()));
        (await context.LlmCallSet.AsNoTracking().Where(c => c.TenderId == tenderId).ToListAsync()).Should().ContainSingle(c => c.Purpose == "classify" && c.Success);
        (await context.AuditLogSet.AsNoTracking().AnyAsync(a => a.Action == "ReclassifyTenderCommand")).Should().BeTrue();
    }

    [Fact]
    public async Task Near_misses_are_listed_separately_and_prefilter_only_skips_the_model()
    {
        var tenderId = await InsertTenderAsync("Cloud migration assessment for the finance system", "Assess the cloud readiness of the finance system.");
        factory.Services.GetRequiredService<FakeLlmClient>().Answer("classify", new ClassificationAnswer([new("CLOUD", 0.9)], 50, true, "Assessment only.", []));
        using var admin = await factory.CreateAdminClientAsync();

        var outcome = await (await admin.PostAsync($"/api/v1/tenders/{tenderId}/reclassify", null)).Content.ReadFromJsonAsync<ClassificationOutcome>();
        outcome!.IsNearMiss.Should().BeTrue();
        outcome.MatchesCreated.Should().Be(0);

        var nearMisses = await admin.GetFromJsonAsync<PagedResult<TenderListItemDto>>("/api/v1/tenders?nearMisses=true&pageSize=100");
        nearMisses!.Items.Should().Contain(t => t.Id == tenderId && t.Relevance == 50 && t.MatchStatus == null);
        var matched = await admin.GetFromJsonAsync<PagedResult<TenderListItemDto>>("/api/v1/tenders?matchedOnly=true&pageSize=100");
        matched!.Items.Should().NotContain(t => t.Id == tenderId);

        var stageOne = await (await admin.PostAsync($"/api/v1/tenders/{tenderId}/reclassify?prefilterOnly=true", null)).Content.ReadFromJsonAsync<ClassificationOutcome>();
        stageOne!.Stage.Should().Be(ClassificationStage.Keyword);
        stageOne.Candidate.Should().BeTrue();
        stageOne.CategoryCodes.Should().Contain("CLOUD");

        (await admin.PostAsync($"/api/v1/tenders/{Guid.CreateVersion7()}/reclassify", null)).StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Admins_can_dry_run_the_keyword_stage_on_free_text()
    {
        using var viewer = await factory.CreateViewerClientAsync();
        (await viewer.PostAsJsonAsync("/api/v1/interest-categories/test", new { title = "x" })).StatusCode.Should().Be(HttpStatusCode.Forbidden);

        using var admin = await factory.CreateAdminClientAsync();
        var itResponse = await admin.PostAsJsonAsync("/api/v1/interest-categories/test", new { title = "Appointment of a service provider for software development and system integration", mainCategory = (int)ProcurementCategory.Services, procurementMethodDetails = "Request for Quotation" });
        var itJson = await itResponse.Content.ReadAsStringAsync();
        itResponse.StatusCode.Should().Be(HttpStatusCode.OK, itJson);
        var it = System.Text.Json.JsonSerializer.Deserialize<PrefilterResult>(itJson, WebJson);
        it!.IsCandidate.Should().BeTrue(itJson);
        it.CandidateCodes.Should().Contain("SW_DEV").And.Contain("SYS_INT");

        var ups = await (await admin.PostAsJsonAsync("/api/v1/interest-categories/test", new { title = "Replacement of UPS units at head office", mainCategory = (int)ProcurementCategory.Goods })).Content.ReadFromJsonAsync<PrefilterResult>();
        ups!.IsCandidate.Should().BeFalse();
        ups.GlobalNegativeHits.Should().Contain("ups");

        (await admin.PostAsJsonAsync("/api/v1/interest-categories/test", new { })).StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }
}
