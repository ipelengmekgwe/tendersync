using TenderSync.Application.Features.Qualification;
using TenderSync.Application.Tests.Documents;
using TenderSync.Domain.Companies;
using TenderSync.Domain.Enums;
using TenderSync.Domain.Qualification;
using TenderSync.Domain.Tenders;

namespace TenderSync.Application.Tests.Qualification;

public class QualificationReportRendererTests
{
    private static readonly DateTimeOffset Now = DocumentTestData.Now;

    [Fact]
    public void The_report_opens_with_the_disclaimer_and_links_every_finding_to_its_source()
    {
        var tender = DocumentTestData.TenderWith(DocumentTestData.Document("d0", "Bid Document.pdf", "pdf"));
        var doc = tender.Documents.Single();
        var sourced = TenderRequirement.Create(tender.Id, RequirementKind.Mandatory, RequirementType.CsdRegistration, "Registered on the CSD", true, 0.9m, new RequirementSource(doc.Id, 3, "CSD"), "m", "v1", Now);
        var unsourced = TenderRequirement.Create(tender.Id, RequirementKind.Mandatory, RequirementType.Other, "Local content | threshold", true, 0.4m, null, "m", "v1", Now);
        var profile = CompanyProfile.Create("Acme Software");

        var markdown = QualificationReportRenderer.Render(new QualificationReportContext(
            tender, profile, [sourced, unsourced],
            [new(sourced.Id, RequirementType.CsdRegistration, sourced.Text, FindingOutcome.Pass, "CSD supplier number MAAA1.", 0.95m), new(unsourced.Id, RequirementType.Other, unsourced.Text, FindingOutcome.Unknown, "Confirm by hand.", 0.5m)],
            [new("Preference points claim form", "SBD 6.1", DocumentOutcome.ToComplete, null, "Bid form.", null), new("CSD report", null, DocumentOutcome.Missing, CompanyDocumentType.CsdReport, "No CSD report in the library.", null)],
            [new("Experience", 60, 45, "One reference.", 0.7m)], 75, 70, "Solid experience.",
            [new("Does the company meet: Local content?", "Confirm by hand.", unsourced.Id)],
            [], [new("CSD report", null, "No CSD report in the library.", null)],
            QualificationVerdict.Partial, 72, "claude-opus-5-5", Now));

        markdown.Should().StartWith("# Can we bid? ");
        markdown.Should().Contain("> **Disclaimer.** " + QualificationReportRenderer.Disclaimer);
        markdown.Should().Contain($"> Source: [{tender.SourceUrl}]({tender.SourceUrl})");
        markdown.Should().Contain("**Verdict: Partial: open questions** (score 72/100)");
        markdown.Should().Contain($"| ✅ Pass | Registered on the CSD | CSD supplier number MAAA1. | [Bid Document.pdf p.3](/api/v1/tenders/{tender.Id}/documents/{doc.Id}/download) |");
        markdown.Should().Contain("| ❓ Unknown | Local content \\| threshold | Confirm by hand. | unknown (verify) |", "pipes are escaped and a missing source is visible");
        markdown.Should().Contain("| 📝 To complete | SBD 6.1: Preference points claim form |");
        markdown.Should().Contain("| ❌ Missing | CSD report |");
        markdown.Should().Contain("Estimated **75/100** against a threshold of 70.");
        markdown.Should().Contain("| Experience | 60 | 45 | 70% | One reference. |");
        markdown.Should().Contain("1. Does the company meet: Local content? _Confirm by hand._");
        markdown.Should().Contain("- Document: CSD report — No CSD report in the library.");
        markdown.Should().Contain("Sat 24 Oct 2026 11:00 SAST", "times are shown in SAST");
    }
}
