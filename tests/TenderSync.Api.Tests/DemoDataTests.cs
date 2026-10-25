using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using TenderSync.Application.Abstractions.Persistence;
using TenderSync.Application.Features.Matches;
using TenderSync.Application.Features.Requirements.Queries.GetTenderRequirements;
using TenderSync.Domain.Enums;
using TenderSync.Infrastructure.Persistence;
using TenderSync.Infrastructure.Persistence.Seed;
using UglyToad.PdfPig;

namespace TenderSync.Api.Tests;

/// <summary>The demo data behind the dashboard e2e test (Worker --seed-demo), checked through the API the pages use.</summary>
[Collection(ApiCollection.Name)]
[Trait("Category", "Integration")]
public class DemoDataTests(ApiFactory factory)
{
    private async Task<DemoSeedResult> SeedAsync()
    {
        using var scope = factory.Services.CreateScope();
        return await scope.ServiceProvider.GetRequiredService<DemoDataSeeder>().SeedAsync();
    }

    [Fact]
    public async Task Demo_seed_creates_a_new_match_whose_requirements_point_at_pages_of_a_stored_pdf()
    {
        var demo = await SeedAsync();
        using var viewer = await factory.CreateViewerClientAsync();

        var inbox = await viewer.GetFromJsonAsync<MatchInboxDto>($"/api/v1/matches?tenderId={demo.TenderId}&companyProfileId={demo.CompanyProfileId}");
        inbox!.Items.Should().ContainSingle().Which.Should().Match<MatchListItemDto>(m =>
            m.Id == demo.MatchId && m.Status == MatchStatus.New && m.BidNumber == DemoDataSeeder.DemoBidNumber && m.Verdict == QualificationVerdict.Partial);

        var requirements = await viewer.GetFromJsonAsync<TenderRequirementsDto>($"/api/v1/tenders/{demo.TenderId}/requirements");
        requirements!.Mandatory.Should().OnlyContain(r => r.SourceDocumentId == demo.DocumentId && r.SourcePage == 2);
        requirements.Functional.Should().OnlyContain(r => r.SourcePage == 3);
        requirements.NeedsVerification.Should().Be(1, "the years-of-experience requirement has low confidence");

        var pdf = await viewer.GetByteArrayAsync($"/api/v1/tenders/{demo.TenderId}/documents/{demo.DocumentId}/download");
        using var document = PdfDocument.Open(pdf);
        document.NumberOfPages.Should().Be(3);
        document.GetPage(2).Text.Should().Contain("Central Supplier Database");
    }

    [Fact]
    public async Task Seeding_again_resets_the_match_and_adds_no_duplicates_or_outbox_events()
    {
        var first = await SeedAsync();
        using var admin = await factory.CreateAdminClientAsync();
        (await admin.PatchAsJsonAsync($"/api/v1/matches/{first.MatchId}", new { status = (int)MatchStatus.Dismissed, dismissReason = "test", notes = "x" })).EnsureSuccessStatusCode();

        var second = await SeedAsync();

        second.Should().Be(first with { ProfileCreated = false });
        using var scope = factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var match = await context.TenderMatchSet.AsNoTracking().SingleAsync(m => m.Id == first.MatchId);
        match.Status.Should().Be(MatchStatus.New);
        match.Notes.Should().BeNull();
        (await context.TenderRequirementSet.CountAsync(r => r.TenderId == first.TenderId)).Should().Be(9);
        (await context.QualificationReportSet.CountAsync(r => r.TenderId == first.TenderId)).Should().Be(1);
        var readDb = scope.ServiceProvider.GetRequiredService<IReadDb>();
        (await readDb.ExecuteScalarAsync<long>("select count(*) from tendersync.outbox_messages where payload_json::text like @id", new { id = $"%{first.TenderId}%" })).Should().Be(0);
    }
}
