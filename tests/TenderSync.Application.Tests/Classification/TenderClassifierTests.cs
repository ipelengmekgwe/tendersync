using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NSubstitute;
using TenderSync.Application.Abstractions;
using TenderSync.Application.Abstractions.Llm;
using TenderSync.Application.Abstractions.Persistence;
using TenderSync.Application.Features.Classification;
using TenderSync.Application.Features.Llm;
using TenderSync.Application.Tests.Documents;
using TenderSync.Domain.Categories;
using TenderSync.Domain.Companies;
using TenderSync.Domain.Enums;
using TenderSync.Domain.Events;
using TenderSync.Domain.Tenders;

namespace TenderSync.Application.Tests.Classification;

/// <summary>Both stages with the fake model: persistence of the classification row, category filtering, matching and near misses.</summary>
public class TenderClassifierTests
{
    private static readonly DateTimeOffset Now = DocumentTestData.Now;

    private static readonly IReadOnlyList<InterestCategory> Categories =
    [
        InterestCategory.Create("SW_DEV", "Software Development", "Bespoke software.", ["software development", "custom software"], []),
        InterestCategory.Create("CLOUD", "Cloud Services", "Cloud.", ["cloud", "azure"], []),
        InterestCategory.Create("APP_SUPPORT", "Application Support", "Support.", ["application support"], ["ups"]),
    ];

    private readonly IAppDbContext _db = Substitute.For<IAppDbContext>();
    private readonly FakeLlmClient _llm = new();
    private readonly List<TenderClassification> _classifications = [];
    private readonly List<TenderMatch> _matches = [];
    private readonly List<CompanyProfile> _profiles = [];
    private TenderClassification? _existing;

    private TenderClassifier Sut(Tender tender, ClassificationOptions? options = null)
    {
        var settings = options ?? new ClassificationOptions();
        _db.Tenders.GetWithDetailsAsync(tender.Id, Arg.Any<CancellationToken>()).Returns(tender);
        _db.Tenders.GetClassificationAsync(tender.Id, Arg.Any<CancellationToken>()).Returns(_ => _existing);
        _db.Tenders.When(r => r.AddClassification(Arg.Any<TenderClassification>())).Do(c => _classifications.Add(c.Arg<TenderClassification>()));
        _db.InterestCategories.ListAsync(true, Arg.Any<CancellationToken>()).Returns(Categories);
        _db.CompanyProfiles.ListActiveAsync(Arg.Any<CancellationToken>()).Returns(_ => _profiles);
        _db.TenderMatches.FindAsync(tender.Id, Arg.Any<Guid>(), Arg.Any<CancellationToken>()).Returns(call => _matches.FirstOrDefault(m => m.CompanyProfileId == call.ArgAt<Guid>(1)));
        _db.TenderMatches.When(r => r.Add(Arg.Any<TenderMatch>())).Do(c => _matches.Add(c.Arg<TenderMatch>()));

        var prompts = Substitute.For<IPromptRegistry>();
        prompts.Get("classify", null).Returns(new PromptTemplate("classify", 1, ModelTier.Fast, "ClassificationAnswer", null, "You are an analyst."));
        var clock = Substitute.For<IDateTimeProvider>();
        clock.UtcNow.Returns(Now);
        return new TenderClassifier(_db, new KeywordPrefilter(Options.Create(settings)), _llm, prompts, Options.Create(settings), clock, NullLogger<TenderClassifier>.Instance);
    }

    private static Tender TenderWith(string title, string description = "Description.", string[]? documents = null)
    {
        var docs = (documents ?? []).Select((d, i) => DocumentTestData.Document($"d{i}", d, "pdf")).ToArray();
        var snapshot = new TenderSnapshot(
            DocumentTestData.Ocid, "175445", "BID-1", title, description, "723", "Western Cape - Health",
            ProcurementCategory.Services, ["services"], "open", "Request for Quotation", null, "ZAR", Now.AddDays(-1), Now.AddDays(20),
            "https://www.etenders.gov.za/Home/TenderDetails?Id=175445", "release-1", Now.AddDays(-1), "compiled", "{}", docs, false);
        var tender = Tender.CreateFromSnapshot(snapshot, Guid.CreateVersion7(), Province.WesternCape, TenderSourceKind.ETendersOcds, Now);
        tender.ClearDomainEvents();
        return tender;
    }

    private CompanyProfile Profile(params string[] codes)
    {
        var profile = CompanyProfile.Create("Acme Software");
        profile.UpdateCapabilities(new CompanyCapabilities { CategoryCodes = codes });
        _profiles.Add(profile);
        return profile;
    }

    [Fact]
    public async Task A_non_candidate_is_stored_at_the_keyword_stage_without_calling_the_model()
    {
        var tender = TenderWith("Replacement of UPS units at head office");

        var outcome = await Sut(tender).ClassifyAsync(tender.Id);

        outcome.Stage.Should().Be(ClassificationStage.Keyword);
        outcome.Candidate.Should().BeFalse();
        outcome.Relevance.Should().Be(0);
        _llm.Requests.Should().BeEmpty();
        var row = _classifications.Should().ContainSingle().Subject;
        row.Stage.Should().Be(ClassificationStage.Keyword);
        row.Rationale.Should().Contain("ups");
        await _db.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task A_candidate_goes_to_the_model_and_a_matching_profile_gets_a_match()
    {
        var tender = TenderWith("Development of a custom software solution hosted on Azure", documents: ["Terms of reference.pdf"]);
        var profile = Profile("SW_DEV", "DATA_ANALYTICS");
        _llm.Answer("classify", new ClassificationAnswer([new("SW_DEV", 0.95), new("CLOUD", 0.7), new("MOBILE", 0.9), new("APP_SUPPORT", 0.2)], 88, true, "Bespoke development with cloud hosting.", []));
        _llm.Model = "claude-haiku-5-5";
        _llm.CostUsd = 0.0004m;

        var outcome = await Sut(tender).ClassifyAsync(tender.Id);

        outcome.Stage.Should().Be(ClassificationStage.Llm);
        outcome.Relevance.Should().Be(88);
        outcome.CategoryCodes.Should().Equal("SW_DEV", "CLOUD");
        outcome.CategoryCodes.Should().NotContain("MOBILE", "codes outside the enabled list are dropped").And.NotContain("APP_SUPPORT", "low confidence is dropped");
        outcome.MatchesCreated.Should().Be(1);
        outcome.CostUsd.Should().Be(0.0004m);

        var request = _llm.Requests.Should().ContainSingle().Subject;
        request.Purpose.Should().Be("classify");
        request.PromptVersion.Should().Be("v1");
        request.TenderId.Should().Be(tender.Id);
        request.Tier.Should().Be(ModelTier.Fast);
        var user = request.UserParts.OfType<LlmTextPart>().Single().Text;
        user.Should().Contain("- SW_DEV: Software Development").And.Contain("Bid number: BID-1").And.Contain("Procurement method: Request for Quotation").And.Contain("- Terms of reference.pdf");

        var row = _classifications.Should().ContainSingle().Subject;
        row.Stage.Should().Be(ClassificationStage.Llm);
        row.Model.Should().Be("claude-haiku-5-5");
        row.PromptVersion.Should().Be("v1");
        row.TokensIn.Should().Be(1000);
        row.Rationale.Should().Contain("Bespoke development").And.Contain("[stage 1:");

        var match = _matches.Should().ContainSingle().Subject;
        match.CompanyProfileId.Should().Be(profile.Id);
        match.Relevance.Should().Be(88);
        match.Status.Should().Be(MatchStatus.New);
        match.DomainEvents.OfType<TenderMatched>().Should().ContainSingle();
    }

    [Fact]
    public async Task Near_misses_are_classified_but_not_matched_and_profiles_without_overlap_are_skipped()
    {
        var tender = TenderWith("Cloud migration assessment");
        Profile("SW_DEV");
        _llm.Answer("classify", new ClassificationAnswer([new("CLOUD", 0.9)], 50, true, "Cloud work.", ["licence-only resale"]));

        var nearMiss = await Sut(tender).ClassifyAsync(tender.Id);
        nearMiss.IsNearMiss.Should().BeTrue();
        nearMiss.MatchesCreated.Should().Be(0);
        nearMiss.RedFlags.Should().ContainSingle("licence-only resale");
        _classifications.Single().Rationale.Should().Contain("Red flags: licence-only resale");

        _existing = _classifications.Single();
        _llm.Answer("classify", new ClassificationAnswer([new("CLOUD", 0.9)], 90, true, "Cloud work.", []));
        var strong = await Sut(tender).ClassifyAsync(tender.Id);
        strong.MatchesCreated.Should().Be(0, "the only profile wants SW_DEV, not CLOUD");

        Profile(); // a profile with no category codes accepts every category
        var any = await Sut(tender).ClassifyAsync(tender.Id);
        any.MatchesCreated.Should().Be(1);
        _classifications.Should().ContainSingle("the existing row is updated in place, not re-added");
    }

    [Fact]
    public async Task Rerunning_updates_the_existing_match_relevance_and_keeps_its_status()
    {
        var tender = TenderWith("Custom software development");
        var profile = Profile("SW_DEV");
        _llm.Answer("classify", new ClassificationAnswer([new("SW_DEV", 0.9)], 70, true, "x", []));
        var first = await Sut(tender).ClassifyAsync(tender.Id);
        first.MatchesCreated.Should().Be(1);
        _matches.Single().SetStatus(MatchStatus.Reviewing);
        _existing = _classifications.Single();

        _llm.Answer("classify", new ClassificationAnswer([new("SW_DEV", 0.9)], 95, true, "x", []));
        var second = await Sut(tender).ClassifyAsync(tender.Id);

        second.MatchesCreated.Should().Be(0);
        second.MatchesUpdated.Should().Be(1);
        _matches.Single().Should().Match<TenderMatch>(m => m.Relevance == 95 && m.Status == MatchStatus.Reviewing && m.CompanyProfileId == profile.Id);
    }

    [Fact]
    public async Task Prefilter_only_records_stage_one_and_skips_the_model()
    {
        var tender = TenderWith("Custom software development");

        var outcome = await Sut(tender).ClassifyAsync(tender.Id, prefilterOnly: true);

        outcome.Candidate.Should().BeTrue();
        outcome.Stage.Should().Be(ClassificationStage.Keyword);
        outcome.CategoryCodes.Should().Equal("SW_DEV");
        outcome.Reason.Should().StartWith("Stage 2 pending");
        _llm.Requests.Should().BeEmpty();
    }

    [Fact]
    public async Task Model_failures_propagate_and_leave_no_llm_stage_row()
    {
        var tender = TenderWith("Custom software development");

        await FluentActions.Awaiting(() => Sut(tender).ClassifyAsync(tender.Id)).Should().ThrowAsync<LlmOutputException>("no fake answer is registered");

        _classifications.Should().BeEmpty();
        await _db.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
    }
}
