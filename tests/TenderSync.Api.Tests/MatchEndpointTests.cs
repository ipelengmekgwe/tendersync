using System.Net;
using System.Net.Http.Json;
using Microsoft.Extensions.DependencyInjection;
using TenderSync.Application.Abstractions.Persistence;
using TenderSync.Application.Features.Matches;
using TenderSync.Application.Features.Overview.Queries.GetOverview;
using TenderSync.Domain.Companies;
using TenderSync.Domain.Enums;
using TenderSync.Domain.Qualification;
using TenderSync.Domain.Tenders;

namespace TenderSync.Api.Tests;

[Collection(ApiCollection.Name)]
[Trait("Category", "Integration")]
public class MatchEndpointTests(ApiFactory factory)
{
    private sealed record Seeded(Guid TenderId, Guid ProfileId, Guid MatchId);

    private async Task<Seeded> InsertMatchAsync(int closingInDays, QualificationVerdict? verdict = null, int relevance = 80)
    {
        var now = DateTimeOffset.UtcNow;
        var ocid = $"ocds-9t57fa-{Random.Shared.Next(100000, 999999)}";
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<IAppDbContext>();
        var buyer = await db.Buyers.GetByExternalIdAsync("167");
        if (buyer is null)
        {
            buyer = Buyer.Create("167", "ESKOM", BuyerType.StateOwnedEntity, Province.National);
            db.Buyers.Add(buyer);
        }

        var profile = CompanyProfile.Create($"Match Test {ocid[^6..]}");
        db.CompanyProfiles.Add(profile);
        var tender = Tender.CreateFromSnapshot(
            new TenderSnapshot(
                ocid, ocid[^6..], $"M{ocid[^6..]}", "Network support", "Network support services", "167", "ESKOM",
                ProcurementCategory.Services, ["services"], "open", "Request for Bid", null, "ZAR", now, now.AddDays(closingInDays),
                "https://www.etenders.gov.za/", $"{ocid}-r1", now, "tender", "{}", [], false),
            buyer.Id, Province.National, TenderSourceKind.ETendersOcds, now);
        db.Tenders.Add(tender);
        var match = TenderMatch.Create(tender.Id, profile.Id, relevance, now);
        db.TenderMatches.Add(match);
        if (verdict is { } v)
        {
            db.QualificationReports.Add(QualificationReport.Create(tender.Id, profile.Id, v, 64, [], [], [], null, [], [], "report", "fake", "v1", now));
        }

        await db.SaveChangesAsync();
        return new Seeded(tender.Id, profile.Id, match.Id);
    }

    [Fact]
    public async Task Inbox_lists_matches_with_the_newest_verdict_and_counts_per_status()
    {
        var seeded = await InsertMatchAsync(closingInDays: 4, QualificationVerdict.Partial);
        using var viewer = await factory.CreateViewerClientAsync();

        var inbox = await viewer.GetFromJsonAsync<MatchInboxDto>($"/api/v1/matches?tenderId={seeded.TenderId}");

        var item = inbox!.Items.Should().ContainSingle().Subject;
        item.Should().Match<MatchListItemDto>(m =>
            m.Id == seeded.MatchId && m.Status == MatchStatus.New && m.Verdict == QualificationVerdict.Partial && m.Score == 64 && m.Relevance == 80);
        item.CompanyName.Should().StartWith("Match Test");
        inbox.Counts.Should().HaveCount(Enum.GetValues<MatchStatus>().Length);
        inbox.Counts.Single(c => c.Status == MatchStatus.New).Count.Should().Be(1);
        inbox.Counts.Single(c => c.Status == MatchStatus.Reviewing).Count.Should().Be(0);
    }

    [Fact]
    public async Task Inbox_filters_by_status_and_closing_window()
    {
        var soon = await InsertMatchAsync(closingInDays: 2);
        var later = await InsertMatchAsync(closingInDays: 40);
        using var viewer = await factory.CreateViewerClientAsync();

        var closingSoon = await viewer.GetFromJsonAsync<MatchInboxDto>($"/api/v1/matches?closingWithinDays=7&activeOnly=true&pageSize=200");
        closingSoon!.Items.Select(i => i.Id).Should().Contain(soon.MatchId).And.NotContain(later.MatchId);

        var reviewing = await viewer.GetFromJsonAsync<MatchInboxDto>($"/api/v1/matches?status={(int)MatchStatus.Reviewing}&companyProfileId={later.ProfileId}");
        reviewing!.Items.Should().NotContain(i => i.Id == later.MatchId);

        (await viewer.GetAsync("/api/v1/matches?closingWithinDays=0")).StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Reviewer_moves_a_match_through_the_workflow_and_dismissing_needs_a_reason()
    {
        var seeded = await InsertMatchAsync(closingInDays: 10);
        using var admin = await factory.CreateAdminClientAsync();
        var url = $"/api/v1/matches/{seeded.MatchId}";

        var reviewing = await (await admin.PatchAsJsonAsync(url, new { status = (int)MatchStatus.Reviewing, notes = "  Check the briefing date  " })).Content.ReadFromJsonAsync<MatchDto>();
        reviewing!.Status.Should().Be(MatchStatus.Reviewing);
        reviewing.Notes.Should().Be("Check the briefing date");

        (await admin.PatchAsJsonAsync(url, new { status = (int)MatchStatus.Dismissed })).StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await admin.PatchAsJsonAsync(url, new { status = (int)MatchStatus.Applying, dismissReason = "no" })).StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await admin.PatchAsJsonAsync(url, new { })).StatusCode.Should().Be(HttpStatusCode.BadRequest);

        var dismissed = await (await admin.PatchAsJsonAsync(url, new { status = (int)MatchStatus.Dismissed, dismissReason = "Outside our provinces" })).Content.ReadFromJsonAsync<MatchDto>();
        dismissed!.Should().Match<MatchDto>(m => m.Status == MatchStatus.Dismissed && m.DismissReason == "Outside our provinces" && m.DismissedAt != null && m.Notes == "Check the briefing date");

        var reopened = await (await admin.PatchAsJsonAsync(url, new { status = (int)MatchStatus.New, notes = "" })).Content.ReadFromJsonAsync<MatchDto>();
        reopened!.Should().Match<MatchDto>(m => m.Status == MatchStatus.New && m.DismissReason == null && m.DismissedAt == null && m.Notes == null);

        using var viewer = await factory.CreateViewerClientAsync();
        var inbox = await viewer.GetFromJsonAsync<MatchInboxDto>($"/api/v1/matches?tenderId={seeded.TenderId}");
        inbox!.Items.Single().Status.Should().Be(MatchStatus.New);
    }

    [Fact]
    public async Task Viewer_cannot_change_a_match_and_an_unknown_match_is_not_found()
    {
        var seeded = await InsertMatchAsync(closingInDays: 10);
        using var viewer = await factory.CreateViewerClientAsync();
        (await viewer.PatchAsJsonAsync($"/api/v1/matches/{seeded.MatchId}", new { status = (int)MatchStatus.Reviewing })).StatusCode.Should().Be(HttpStatusCode.Forbidden);

        using var admin = await factory.CreateAdminClientAsync();
        (await admin.PatchAsJsonAsync($"/api/v1/matches/{Guid.CreateVersion7()}", new { status = (int)MatchStatus.Reviewing })).StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Overview_shows_new_and_closing_soon_matches_and_the_cost_only_to_admins()
    {
        var seeded = await InsertMatchAsync(closingInDays: 3, QualificationVerdict.Qualify);

        using var viewer = await factory.CreateViewerClientAsync();
        var overview = await viewer.GetFromJsonAsync<OverviewDto>("/api/v1/overview");
        overview!.NewMatchCount.Should().BeGreaterThanOrEqualTo(1);
        overview.NewMatches.Should().Contain(m => m.Id == seeded.MatchId);
        overview.ClosingSoon.Should().Contain(m => m.Id == seeded.MatchId && m.Verdict == QualificationVerdict.Qualify);
        overview.ClosingSoonDays.Should().Be(7);
        overview.Pipeline.Should().NotBeNull();
        overview.Cost.Should().BeNull();

        using var admin = await factory.CreateAdminClientAsync();
        var adminOverview = await admin.GetFromJsonAsync<OverviewDto>("/api/v1/overview");
        adminOverview!.Cost.Should().NotBeNull();
        adminOverview.Cost!.MonthStart.Should().Be(GetOverviewQueryHandler.SastMonthStart(adminOverview.GeneratedAt));
    }

    [Theory]
    [InlineData("2026-10-24T17:00:00+00:00", "2026-09-30T22:00:00+00:00")]
    [InlineData("2026-10-31T22:30:00+00:00", "2026-10-31T22:00:00+00:00")]
    [InlineData("2026-01-01T00:00:00+00:00", "2025-12-31T22:00:00+00:00")]
    public void Month_start_is_the_first_of_the_month_in_south_africa(string now, string expected) =>
        GetOverviewQueryHandler.SastMonthStart(DateTimeOffset.Parse(now, System.Globalization.CultureInfo.InvariantCulture))
            .Should().Be(DateTimeOffset.Parse(expected, System.Globalization.CultureInfo.InvariantCulture));
}
