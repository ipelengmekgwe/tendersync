using Microsoft.Extensions.Options;
using TenderSync.Application.Features.Classification;
using TenderSync.Domain.Categories;
using TenderSync.Domain.Enums;

namespace TenderSync.Application.Tests.Classification;

/// <summary>Stage 1 on the noisy cases from PLAN.md §2: UPS, sewer network and irrigation system must not reach the model as IT work.</summary>
public class KeywordPrefilterTests
{
    private static readonly IReadOnlyList<InterestCategory> Categories =
    [
        InterestCategory.Create("SW_DEV", "Software Development", null, ["software development", "develop a system", "custom software", "application development"], []),
        InterestCategory.Create("APP_SUPPORT", "Application Support", null, ["application support", "system support", "maintenance and support of", "helpdesk"], ["fire system", "irrigation system", "pa system", "cctv", "ups", "generator"]),
        InterestCategory.Create("SYS_INT", "System Integration", null, ["integration", "api", "middleware", "sap"], []),
        InterestCategory.Create("CLOUD", "Cloud Services", null, ["cloud", "azure", "aws", "hosting"], []),
        InterestCategory.Create("CYBER", "Cyber Security", null, ["cyber security", "penetration testing", "firewall"], ["physical security", "guarding", "security guards"]),
    ];

    private static KeywordPrefilter Sut(ClassificationOptions? options = null) => new(Options.Create(options ?? new ClassificationOptions()));

    private static TenderText Text(string title, string description = "", string[]? documents = null, ProcurementCategory category = ProcurementCategory.Services, string? method = "Request for Quotation") =>
        new("BID-1", title, description, documents ?? [], category, method);

    [Theory]
    [InlineData("Replacement of UPS units at head office")]
    [InlineData("Construction of ablution facilities including sewer network at Amaoti No.3 Secondary School")]
    [InlineData("Maintenance and support of the stadium irrigation system")]
    [InlineData("Supply and delivery of cleaning materials")]
    [InlineData("Installation of a backup power supply generator")]
    public void Noisy_non_it_tenders_are_not_candidates(string title)
    {
        var result = Sut().Evaluate(Text(title, category: ProcurementCategory.Works), Categories);

        result.IsCandidate.Should().BeFalse(result.Reason);
        result.CandidateCodes.Should().BeEmpty();
    }

    [Fact]
    public void Scores_follow_the_field_weights()
    {
        var text = Text("Appointment of a service provider for custom software development", "The system must integrate with SAP via API.", ["Annexure A - application development specification.pdf"]);

        var result = Sut().Evaluate(text, Categories);

        result.IsCandidate.Should().BeTrue();
        var swDev = result.Scores.Single(s => s.Code == "SW_DEV");
        swDev.Score.Should().Be(3 + 3 + 2, "'custom software' and 'software development' in the title ×3 each, 'application development' in a document title ×2");
        var sysInt = result.Scores.Single(s => s.Code == "SYS_INT");
        sysInt.Score.Should().Be(2, "'sap' and 'api' in the description ×1 each");
        result.CandidateCodes.Should().Equal("SW_DEV", "SYS_INT");
        result.Reason.Should().Contain("SW_DEV=8");
    }

    [Fact]
    public void Negative_keywords_and_global_negatives_pull_a_category_below_the_bar()
    {
        var result = Sut().Evaluate(Text("Maintenance and support of fire system and irrigation system at the stadium"), Categories);

        result.IsCandidate.Should().BeFalse();
        var support = result.Scores.Single(s => s.Code == "APP_SUPPORT");
        support.Hits.Should().ContainSingle(h => h.Keyword == "maintenance and support of" && h.Weight == 3);
        support.NegativeHits.Select(h => h.Keyword).Should().Contain(["fire system", "irrigation system", "stadium"]);
        support.Score.Should().BeLessThan(1);
        result.GlobalNegativeHits.Should().Contain("irrigation").And.Contain("stadium");
    }

    [Fact]
    public void Matching_is_whole_word_and_case_insensitive()
    {
        KeywordPrefilter.Matches("ups", "Replacement of UPS units").Should().BeTrue();
        KeywordPrefilter.Matches("ups", "Support groups for staff").Should().BeFalse("'groups' is not 'ups'");
        KeywordPrefilter.Matches("api", "Capital projects").Should().BeFalse();
        KeywordPrefilter.Matches("api", "Expose an API for partners").Should().BeTrue();
        KeywordPrefilter.Matches("sap", "SAP support services").Should().BeTrue();
        KeywordPrefilter.Matches("sap", "Sapling nursery").Should().BeFalse();
        KeywordPrefilter.Matches("ci/cd", "Set up CI/CD pipelines").Should().BeTrue();
        KeywordPrefilter.Matches("maintenance and support of", "maintenance  and\nsupport of the system").Should().BeTrue("whitespace runs and line breaks count as a space");
    }

    [Fact]
    public void Services_rfps_and_rfis_without_keywords_still_reach_the_model()
    {
        var rfp = Sut().Evaluate(Text("Appointment of a panel of professional service providers", category: ProcurementCategory.Services, method: "Request for Proposal"), Categories);
        rfp.IsCandidate.Should().BeTrue();
        rfp.MethodRuleApplied.Should().BeTrue();
        rfp.CandidateCodes.Should().BeEmpty();

        var unspecified = Sut().Evaluate(Text("Panel of service providers", category: ProcurementCategory.Unknown, method: "RFI"), Categories);
        unspecified.IsCandidate.Should().BeTrue();

        var rfq = Sut().Evaluate(Text("Appointment of a panel of professional service providers", category: ProcurementCategory.Services, method: "Request for Quotation"), Categories);
        rfq.IsCandidate.Should().BeFalse("RFQs need a keyword hit");

        var works = Sut().Evaluate(Text("Construction of a bridge", category: ProcurementCategory.Works, method: "Request for Proposal"), Categories);
        works.IsCandidate.Should().BeFalse("the method rule is for services and unspecified categories only");

        var negative = Sut().Evaluate(Text("Catering services for the conference", category: ProcurementCategory.Services, method: "Request for Proposal"), Categories);
        negative.IsCandidate.Should().BeFalse("a global negative keyword blocks the method rule");
    }

    [Fact]
    public void Supply_and_delivery_is_negative_unless_it_is_software_or_hardware()
    {
        Sut().Evaluate(Text("Supply and delivery of comfort packs to district offices"), Categories).GlobalNegativeHits.Should().Contain("supply and delivery of");

        var software = Sut().Evaluate(Text("Supply and delivery of software licences with cloud hosting"), Categories);
        software.GlobalNegativeHits.Should().NotContain("supply and delivery of");
        software.IsCandidate.Should().BeTrue();
    }

    [Fact]
    public void Disabled_categories_are_ignored_and_weights_are_configurable()
    {
        var disabled = InterestCategory.Create("DISABLED", "x", null, ["bridge"], []);
        disabled.SetEnabled(false);

        Sut().Evaluate(Text("Construction of a bridge"), [disabled]).IsCandidate.Should().BeFalse();

        var heavy = Sut(new ClassificationOptions { TitleWeight = 10 }).Evaluate(Text("custom software"), Categories);
        heavy.Scores.Single(s => s.Code == "SW_DEV").Score.Should().Be(10);
    }
}
