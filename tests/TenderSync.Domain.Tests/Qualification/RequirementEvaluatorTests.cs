using TenderSync.Domain.Companies;
using TenderSync.Domain.Enums;
using TenderSync.Domain.Qualification;
using TenderSync.Domain.Tenders;

namespace TenderSync.Domain.Tests.Qualification;

/// <summary>The pass/fail/unknown/expired matrix of every evaluator (WP09 acceptance).</summary>
public class RequirementEvaluatorTests
{
    private static readonly DateTimeOffset Now = TestSnapshots.Now;
    private static readonly DateTimeOffset Closing = Now.AddDays(21);
    private static readonly Guid TenderId = Guid.CreateVersion7();

    private static TenderRequirement Requirement(RequirementType type, string text, string? value = null) =>
        TenderRequirement.Create(TenderId, RequirementKind.Mandatory, type, text, true, 0.9m, null, "fake", "v1", Now,
            valueJson: value is null ? null : $"{{\"value\":\"{value}\"}}");

    private static CompanyProfile Profile(Action<CompanyProfile>? configure = null)
    {
        var profile = CompanyProfile.Create("Acme Software");
        profile.UpdateDetails("Acme Software", null, "2015/123456/07", null, 8, 25, 12_000_000m);
        profile.UpdateCompliance("MAAA0001234", Closing.AddMonths(6), "PIN123", Closing.AddMonths(3), 2, Closing.AddMonths(2), "Sworn affidavit");
        configure?.Invoke(profile);
        return profile;
    }

    private static RequirementEvaluation Evaluate(RequirementType type, string text, string? value = null, Action<CompanyProfile>? configure = null, bool noClosing = false) =>
        RequirementEvaluators.Evaluate(new EvaluationContext(Requirement(type, text, value), Profile(configure), [], noClosing ? null : Closing, Now));

    [Fact]
    public void Csd_passes_when_registered_and_valid_at_closing()
    {
        var result = Evaluate(RequirementType.CsdRegistration, "Registered on the CSD");

        result.Outcome.Should().Be(FindingOutcome.Pass);
        result.Detail.Should().Contain("MAAA0001234");
    }

    [Fact]
    public void Csd_fails_when_missing_and_names_the_missing_report()
    {
        var result = Evaluate(RequirementType.CsdRegistration, "Registered on the CSD", configure: p => p.UpdateCompliance(null, null, "PIN", null, 2, null, null));

        result.Outcome.Should().Be(FindingOutcome.Fail);
        result.MissingDocument.Should().Be(CompanyDocumentType.CsdReport);
    }

    [Fact]
    public void Csd_that_lapses_before_closing_is_expired_even_though_valid_today()
    {
        var result = Evaluate(RequirementType.CsdRegistration, "Registered on the CSD", configure: p => p.UpdateCompliance("MAAA1", Now.AddDays(5), "PIN", null, 2, null, null));

        result.Outcome.Should().Be(FindingOutcome.Expired);
        result.Detail.Should().Contain("before the bid closes");
    }

    [Fact]
    public void Csd_validity_is_checked_against_today_when_the_closing_date_is_unknown()
    {
        var result = Evaluate(RequirementType.CsdRegistration, "CSD", configure: p => p.UpdateCompliance("MAAA1", Now.AddDays(5), "PIN", null, 2, null, null), noClosing: true);

        result.Outcome.Should().Be(FindingOutcome.Pass);
    }

    [Theory]
    [InlineData(true, false, FindingOutcome.Pass)]
    [InlineData(false, false, FindingOutcome.Fail)]
    [InlineData(true, true, FindingOutcome.Expired)]
    public void Tax_pin_matrix(bool present, bool expired, FindingOutcome expected)
    {
        var result = Evaluate(RequirementType.TaxCompliance, "Valid tax compliance status",
            configure: p => p.UpdateCompliance("MAAA1", null, present ? "PIN" : null, expired ? Now.AddDays(-1) : null, 2, null, null));

        result.Outcome.Should().Be(expected);
    }

    [Theory]
    [InlineData(2, "level 4", FindingOutcome.Pass)]
    [InlineData(4, "level 4", FindingOutcome.Pass)]
    [InlineData(5, "level 4", FindingOutcome.Fail)]
    [InlineData(2, "a valid certificate", FindingOutcome.Unknown)]
    [InlineData(null, "level 4", FindingOutcome.Fail)]
    public void Bbbee_matrix(int? level, string value, FindingOutcome expected)
    {
        var result = Evaluate(RequirementType.BbbeeCertificate, "B-BBEE certificate", value, p => p.UpdateCompliance("MAAA1", null, "PIN", null, level, null, null));

        result.Outcome.Should().Be(expected);
    }

    [Fact]
    public void Bbbee_certificate_that_expires_before_closing_is_expired()
    {
        var result = Evaluate(RequirementType.BbbeeCertificate, "B-BBEE level 4 or better", configure: p => p.UpdateCompliance("MAAA1", null, "PIN", null, 2, Closing.AddDays(-1), null));

        result.Outcome.Should().Be(FindingOutcome.Expired);
        result.MissingDocument.Should().Be(CompanyDocumentType.BbbeeCertificate);
    }

    [Theory]
    [InlineData(7, "GB", "grade 7GB", FindingOutcome.Pass)]
    [InlineData(8, "GB", "7GB or higher", FindingOutcome.Pass)]
    [InlineData(6, "GB", "grade 7 GB", FindingOutcome.Fail)]
    [InlineData(7, "CE", "7GB", FindingOutcome.Fail)]
    [InlineData(7, "GB", "CIDB registered", FindingOutcome.Unknown)]
    [InlineData(null, null, "7GB", FindingOutcome.Fail)]
    public void Cidb_matrix(int? grade, string? cls, string value, FindingOutcome expected)
    {
        var result = Evaluate(RequirementType.CidbGrade, "CIDB grading", value, p => p.UpdateCidb(grade, cls is null ? [] : [cls]));

        result.Outcome.Should().Be(expected);
    }

    [Theory]
    [InlineData("2015/123456/07", FindingOutcome.Pass)]
    [InlineData(null, FindingOutcome.Fail)]
    public void Company_registration_needs_a_registration_number(string? number, FindingOutcome expected)
    {
        var result = Evaluate(RequirementType.CompanyRegistration, "CIPC registration", configure: p => p.UpdateDetails("Acme", null, number, null, 8, null, null));

        result.Outcome.Should().Be(expected);
    }

    [Theory]
    [InlineData(8, "5 years", FindingOutcome.Pass)]
    [InlineData(3, "5 years", FindingOutcome.Fail)]
    [InlineData(0, "5 years", FindingOutcome.Unknown)]
    [InlineData(8, "established", FindingOutcome.Unknown)]
    public void Years_in_business_matrix(int trading, string value, FindingOutcome expected)
    {
        var result = Evaluate(RequirementType.YearsInBusiness, "Years in business", value, p => p.UpdateDetails("Acme", null, null, null, trading, null, null));

        result.Outcome.Should().Be(expected);
    }

    [Theory]
    [InlineData(12_000_000, "R10 million", FindingOutcome.Pass)]
    [InlineData(8_000_000, "R10 million", FindingOutcome.Fail)]
    [InlineData(-1, "R10 million", FindingOutcome.Unknown)]
    [InlineData(12_000_000, "sound financial standing", FindingOutcome.Unknown)]
    public void Turnover_matrix(int turnover, string value, FindingOutcome expected)
    {
        var result = Evaluate(RequirementType.MinimumTurnover, "Minimum annual turnover", value, p => p.UpdateDetails("Acme", null, null, null, 8, null, turnover < 0 ? null : turnover));

        result.Outcome.Should().Be(expected);
    }

    [Fact]
    public void References_count_only_projects_that_meet_value_and_recency()
    {
        static void References(CompanyProfile p) => p.UpdateCapabilities(new CompanyCapabilities
        {
            ReferenceProjects =
            [
                new ReferenceProject("Dept A", "Portal", 3_000_000m, Now.Year - 1, true),
                new ReferenceProject("Dept B", "Support", 2_500_000m, Now.Year - 2, true),
                new ReferenceProject("Dept C", "Old", 5_000_000m, Now.Year - 9, false),
                new ReferenceProject("Dept D", "Small", 200_000m, Now.Year, true),
            ],
        });

        Evaluate(RequirementType.References, "References", "2 references of R2 million in the last 5 years", References).Outcome.Should().Be(FindingOutcome.Pass);
        var fail = Evaluate(RequirementType.References, "References", "3 references of R2 million in the last 5 years", References);
        fail.Outcome.Should().Be(FindingOutcome.Fail);
        fail.Detail.Should().Contain("Only 2 of 4");
        Evaluate(RequirementType.References, "Contactable references", configure: References).Outcome.Should().Be(FindingOutcome.Pass, "with no stated rule one reference is enough");
        Evaluate(RequirementType.References, "3 references").Outcome.Should().Be(FindingOutcome.Unknown, "the profile lists no reference projects");
    }

    [Fact]
    public void Certification_matches_profile_names_and_flags_the_rest_for_fuzzy_matching()
    {
        static void Certs(CompanyProfile p) => p.UpdateCapabilities(new CompanyCapabilities
        {
            Certifications = [new CompanyCertification("ISO 27001", "BSI", Closing.AddYears(1)), new CompanyCertification("ISO 9001", null, Closing.AddDays(-1))],
        });

        Evaluate(RequirementType.Certification, "Bidder must hold ISO-27001 certification", configure: Certs).Outcome.Should().Be(FindingOutcome.Pass, "punctuation is ignored");
        var expired = Evaluate(RequirementType.Certification, "ISO 9001 certified", configure: Certs);
        expired.Outcome.Should().Be(FindingOutcome.Expired);
        var fuzzy = Evaluate(RequirementType.Certification, "Microsoft Gold Partner status", configure: Certs);
        fuzzy.Outcome.Should().Be(FindingOutcome.Unknown);
        fuzzy.NeedsFuzzyMatch.Should().BeTrue();
        var none = Evaluate(RequirementType.Certification, "ISO 27001");
        none.Outcome.Should().Be(FindingOutcome.Unknown);
        none.NeedsFuzzyMatch.Should().BeFalse("there is nothing to compare");
    }

    [Theory]
    [InlineData(RequirementType.Insurance)]
    [InlineData(RequirementType.Accreditation)]
    [InlineData(RequirementType.LocalContent)]
    [InlineData(RequirementType.JvSubcontractingRule)]
    [InlineData(RequirementType.Other)]
    public void Types_the_profile_cannot_answer_are_unknown(RequirementType type)
    {
        var result = Evaluate(type, "Some requirement");

        result.Outcome.Should().Be(FindingOutcome.Unknown);
        result.Detail.Should().Contain("confirm by hand");
    }

    [Fact]
    public void Every_requirement_type_has_an_evaluator()
    {
        foreach (var type in Enum.GetValues<RequirementType>())
        {
            RequirementEvaluators.For(type).Type.Should().Be(type);
        }
    }
}
