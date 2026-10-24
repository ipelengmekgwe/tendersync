using TenderSync.Domain.Enums;
using TenderSync.Domain.Qualification;

namespace TenderSync.Domain.Tests.Qualification;

public class VerdictRulesTests
{
    private static QualificationFinding Finding(FindingOutcome outcome) =>
        new(Guid.CreateVersion7(), RequirementType.CsdRegistration, "CSD", outcome, "detail", 0.9m);

    private static DocumentCheck Document(DocumentOutcome outcome) => new("Doc", null, outcome, null, "detail", null);

    [Fact]
    public void Any_fail_is_not_qualify_regardless_of_the_rest() =>
        VerdictRules.Decide([Finding(FindingOutcome.Pass), Finding(FindingOutcome.Fail), Finding(FindingOutcome.Unknown)], 90, 70).Should().Be(QualificationVerdict.NotQualify);

    [Theory]
    [InlineData(FindingOutcome.Unknown)]
    [InlineData(FindingOutcome.Expired)]
    public void Unknown_or_expired_items_make_it_partial(FindingOutcome outcome) =>
        VerdictRules.Decide([Finding(FindingOutcome.Pass), Finding(outcome)], 90, 70).Should().Be(QualificationVerdict.Partial);

    [Theory]
    [InlineData(65, 70, QualificationVerdict.Partial)]
    [InlineData(null, 70, QualificationVerdict.Partial)]
    [InlineData(70, 70, QualificationVerdict.Qualify)]
    [InlineData(null, null, QualificationVerdict.Qualify)]
    [InlineData(10, null, QualificationVerdict.Qualify)]
    public void Functionality_below_the_threshold_is_partial_and_no_threshold_cannot_block(int? estimate, int? threshold, QualificationVerdict expected) =>
        VerdictRules.Decide([Finding(FindingOutcome.Pass)], estimate, threshold).Should().Be(expected);

    [Fact]
    public void No_mandatory_items_and_no_threshold_qualifies() =>
        VerdictRules.Decide([], null, null).Should().Be(QualificationVerdict.Qualify);

    [Fact]
    public void Score_combines_the_three_shares_by_weight()
    {
        var mandatory = new[] { Finding(FindingOutcome.Pass), Finding(FindingOutcome.Unknown), Finding(FindingOutcome.Fail), Finding(FindingOutcome.Expired) }; // (1 + .5 + 0 + .25) / 4 = 0.4375
        var documents = new[] { Document(DocumentOutcome.Present), Document(DocumentOutcome.Missing), Document(DocumentOutcome.ToComplete), Document(DocumentOutcome.Unmapped) }; // 1 / 2 = 0.5

        var score = VerdictRules.Score(mandatory, 80, documents, QualificationWeights.Default);

        // 60 × 0.4375 + 30 × 0.8 + 10 × 0.5 = 26.25 + 24 + 5 = 55.25 → 55
        score.Should().Be(55);
    }

    [Fact]
    public void Score_treats_nothing_to_check_as_full_marks_and_no_estimate_as_half()
    {
        VerdictRules.Score([], null, [], QualificationWeights.Default).Should().Be(85, "60 + 15 + 10");
        VerdictRules.Score([Finding(FindingOutcome.Pass)], 100, [Document(DocumentOutcome.Present)], new QualificationWeights(1, 1, 1)).Should().Be(100);
        VerdictRules.Score([Finding(FindingOutcome.Pass)], 100, [], new QualificationWeights(0, 0, 0)).Should().Be(0, "zero weights cannot divide");
    }
}
