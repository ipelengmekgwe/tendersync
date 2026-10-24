using TenderSync.Application.Abstractions.Llm;
using TenderSync.Application.Features.Requirements;
using TenderSync.Domain.Enums;

namespace TenderSync.Application.Tests.Requirements;

public class RequirementsMergerTests
{
    private static readonly Guid MainDoc = Guid.CreateVersion7();
    private static readonly Guid Annexure = Guid.CreateVersion7();

    private static RequirementSourceV1 Src(int? page, string? quote = "quoted text") => new(page, quote);

    private static TenderRequirementsV1 Empty() => new(string.Empty, [], [], null, null, [], null, null, null, null, null);

    [Fact]
    public void The_main_document_wins_single_valued_sections_and_lists_are_unioned_without_duplicates()
    {
        var main = Empty() with
        {
            Summary = "Main summary.",
            Mandatory = [new(RequirementType.CsdRegistration, "Registered on the CSD", null, Src(3), 0.95), new(RequirementType.BbbeeCertificate, "B-BBEE level 2 or better", "level 2", Src(4), 0.9)],
            FunctionalityThreshold = 70,
            EvaluationMethod = "80/20",
            Briefing = new BriefingV1("2026-11-03T10:00:00+02:00", true, "Boardroom A", null, Src(2), 0.9),
            RequiredDocuments = [new("Preference points claim form", "SBD 6.1", true, null, Src(9), 0.9)],
        };
        var annexure = Empty() with
        {
            Summary = "Annexure summary.",
            Mandatory = [new(RequirementType.CsdRegistration, "Registered on the Central Supplier Database (CSD).", null, Src(1), 0.6), new(RequirementType.TaxCompliance, "Valid tax compliance status", null, Src(1), 0.9)],
            FunctionalityThreshold = 60,
            Briefing = new BriefingV1(null, false, null, null, Src(null), 0.3),
            RequiredDocuments = [new("SBD 6.1 Preference Points Claim", "sbd 6.1", true, null, Src(2), 0.95), new("Company registration documents", null, true, "certified", Src(2), 0.8)],
            Contacts = new ContactsV1("scm@example.gov.za", null),
        };

        var merged = RequirementsMerger.Merge([new(MainDoc, "Bid Document", main, []), new(Annexure, "Annexure", annexure, [])]);

        merged.Summary.Should().Be("Main summary.");
        merged.FunctionalityThreshold!.Value.Should().Be(70);
        merged.FunctionalityThreshold.DocumentId.Should().Be(MainDoc);
        merged.EvaluationMethod!.Value.Should().Be("80/20");
        merged.Briefing!.DocumentId.Should().Be(MainDoc);
        merged.Contacts!.Value.EnquiriesEmail.Should().Be("scm@example.gov.za", "the annexure fills sections the main document lacks");

        // Dedupe is by type and normalised text, so the two CSD wordings stay separate entries.
        merged.Mandatory.Select(m => m.Value.Type).Should().Equal(RequirementType.CsdRegistration, RequirementType.BbbeeCertificate, RequirementType.CsdRegistration, RequirementType.TaxCompliance);
        merged.Mandatory[0].DocumentId.Should().Be(MainDoc);
        merged.Mandatory[2].DocumentId.Should().Be(Annexure);

        merged.RequiredDocuments.Should().HaveCount(2, "the SBD 6.1 rows share a form code");
        merged.RequiredDocuments[0].Value.Confidence.Should().Be(0.95, "the higher-confidence copy is kept");
        merged.RequiredDocuments[0].DocumentId.Should().Be(Annexure);
    }

    [Fact]
    public void Pages_are_filled_from_citations_whose_text_overlaps_the_quote()
    {
        var result = Empty() with
        {
            Mandatory = [new(RequirementType.MinimumTurnover, "Minimum annual turnover of R10 million", "R10 million", Src(null, "an annual turnover of at least R10 million for the past two financial years"), 0.9)],
            RequiredDocuments = [new("Tax PIN", null, true, null, Src(5, "valid tax pin"), 0.9), new("Bank letter", null, true, null, Src(null, "no citation for this"), 0.9)],
        };
        var citations = new List<LlmCitation>
        {
            new(0, 12, 12, "Bidders must have an annual turnover of at least R10 million for the past two financial years."),
            new(0, 7, 7, "something else"),
        };

        var merged = RequirementsMerger.Merge([new(MainDoc, "Bid", result, citations)]);

        merged.Mandatory.Single().Value.Source.Page.Should().Be(12);
        merged.RequiredDocuments.Single(d => d.Value.Name == "Tax PIN").Value.Source.Page.Should().Be(5, "a page from the model is kept");
        merged.RequiredDocuments.Single(d => d.Value.Name == "Bank letter").Value.Source.Page.Should().BeNull("no citation overlaps, so it stays unknown and will be flagged");
    }

    [Fact]
    public void Keys_ignore_case_punctuation_and_whitespace()
    {
        RequirementsMerger.Key("  SBD 6.1 — Preference  Points! ").Should().Be("sbd 6 1 preference points");
        RequirementsMerger.Key(null).Should().BeEmpty();
    }
}
