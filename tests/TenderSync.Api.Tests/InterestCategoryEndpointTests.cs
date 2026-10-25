using System.Net;
using System.Net.Http.Json;
using Microsoft.Extensions.DependencyInjection;
using TenderSync.Application.Features.Categories;
using TenderSync.Application.Features.Classification;
using TenderSync.Application.Features.Llm;
using TenderSync.Application.Features.Matches;
using TenderSync.Domain.Enums;
using TenderSync.Infrastructure.Persistence.Seed;

namespace TenderSync.Api.Tests;

[Collection(ApiCollection.Name)]
[Trait("Category", "Integration")]
public class InterestCategoryEndpointTests(ApiFactory factory)
{
    private const string Url = "/api/v1/interest-categories";

    private static object Body(string name, string[] keywords, bool enabled = true, string? code = null) => new
    {
        code,
        name,
        description = "Test category",
        keywords,
        negativeKeywords = new[] { "Hardware Only" },
        weight = 2,
        enabled,
    };

    [Fact]
    public async Task Admin_creates_lists_updates_and_deletes_a_category()
    {
        using var admin = await factory.CreateAdminClientAsync();
        var code = $"test_{Random.Shared.Next(1000, 9999)}";

        var create = await admin.PostAsJsonAsync(Url, Body("Test", ["  Kubernetes ", "kubernetes", "Helm"], code: code));
        create.StatusCode.Should().Be(HttpStatusCode.Created);
        var created = (await create.Content.ReadFromJsonAsync<InterestCategoryDto>())!;
        created.Code.Should().Be(code.ToUpperInvariant());
        created.Keywords.Should().Equal("kubernetes", "helm");
        created.NegativeKeywords.Should().Equal("hardware only");

        (await admin.PostAsJsonAsync(Url, Body("Again", ["x"], code: code))).StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await admin.PostAsJsonAsync(Url, Body("Bad code", ["x"], code: "9 bad"))).StatusCode.Should().Be(HttpStatusCode.BadRequest);

        var listed = await admin.GetFromJsonAsync<List<InterestCategoryDto>>(Url);
        listed!.Should().Contain(c => c.Id == created.Id).And.Contain(c => c.Code == "SW_DEV");
        listed.Select(c => c.Code).Should().BeInAscendingOrder(StringComparer.Ordinal);

        var updated = await (await admin.PutAsJsonAsync($"{Url}/{created.Id}", Body("Renamed", ["argo cd"], enabled: false))).Content.ReadFromJsonAsync<InterestCategoryDto>();
        updated!.Should().Match<InterestCategoryDto>(c => c.Name == "Renamed" && !c.Enabled && c.Keywords.Single() == "argo cd" && c.Weight == 2);
        (await admin.PutAsJsonAsync($"{Url}/{created.Id}", Body("", ["x"]))).StatusCode.Should().Be(HttpStatusCode.BadRequest);

        (await admin.DeleteAsync($"{Url}/{created.Id}")).StatusCode.Should().Be(HttpStatusCode.NoContent);
        (await admin.DeleteAsync($"{Url}/{created.Id}")).StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Viewers_cannot_read_or_change_categories()
    {
        using var viewer = await factory.CreateViewerClientAsync();
        (await viewer.GetAsync(Url)).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await viewer.PostAsJsonAsync(Url, Body("x", ["y"], code: "NOPE"))).StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    /// <summary>The WP14 acceptance flow through the API: category edit → reclassify → a match appears.</summary>
    [Fact]
    public async Task Adding_a_keyword_then_reclassifying_creates_a_match()
    {
        DemoSeedResult demo;
        using (var scope = factory.Services.CreateScope())
        {
            demo = await scope.ServiceProvider.GetRequiredService<DemoDataSeeder>().SeedAsync();
        }

        factory.Services.GetRequiredService<FakeLlmClient>()
            .Answer("classify", new ClassificationAnswer([new CategoryConfidence("APP_SUPPORT", 0.9)], 85, true, "Fake answer for the test.", []));

        using var admin = await factory.CreateAdminClientAsync();
        var categories = await admin.GetFromJsonAsync<List<InterestCategoryDto>>(Url);
        var appSupport = categories!.Single(c => c.Code == "APP_SUPPORT");
        var input = (string[] keywords) => new { appSupport.Name, appSupport.Description, keywords, negativeKeywords = appSupport.NegativeKeywords, appSupport.Weight, appSupport.Enabled };

        var before = await (await admin.PostAsJsonAsync($"{Url}/test", new { tenderId = demo.KeywordTenderId })).Content.ReadFromJsonAsync<PrefilterResult>();
        before!.IsCandidate.Should().BeFalse();

        try
        {
            (await admin.PutAsJsonAsync($"{Url}/{appSupport.Id}", input([.. appSupport.Keywords, DemoDataSeeder.KeywordDemoWord]))).EnsureSuccessStatusCode();

            var after = await (await admin.PostAsJsonAsync($"{Url}/test", new { tenderId = demo.KeywordTenderId })).Content.ReadFromJsonAsync<PrefilterResult>();
            after!.IsCandidate.Should().BeTrue();
            after.Scores.Single(s => s.Code == "APP_SUPPORT").Hits.Should().Contain(h => h.Keyword == DemoDataSeeder.KeywordDemoWord && h.Field == "title");

            var outcome = await (await admin.PostAsync($"/api/v1/tenders/{demo.KeywordTenderId}/reclassify", null)).Content.ReadFromJsonAsync<ClassificationOutcome>();
            outcome!.Relevance.Should().Be(85);
            outcome.MatchesCreated.Should().BeGreaterThanOrEqualTo(1);

            var inbox = await admin.GetFromJsonAsync<MatchInboxDto>($"/api/v1/matches?tenderId={demo.KeywordTenderId}");
            inbox!.Items.Should().Contain(m => m.CompanyProfileId == demo.CompanyProfileId && m.Status == MatchStatus.New && m.Relevance == 85);
        }
        finally
        {
            (await admin.PutAsJsonAsync($"{Url}/{appSupport.Id}", input([.. appSupport.Keywords]))).EnsureSuccessStatusCode();
        }
    }
}
