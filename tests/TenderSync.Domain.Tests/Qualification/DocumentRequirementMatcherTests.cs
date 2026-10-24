using TenderSync.Domain.Companies;
using TenderSync.Domain.Enums;
using TenderSync.Domain.Qualification;

namespace TenderSync.Domain.Tests.Qualification;

public class DocumentRequirementMatcherTests
{
    private static readonly DateTimeOffset Now = TestSnapshots.Now;
    private static readonly Guid ProfileId = Guid.CreateVersion7();

    private static CompanyDocument Doc(CompanyDocumentType type, string title, DateTimeOffset? expires = null) =>
        CompanyDocument.Create(ProfileId, type, title, $"companies/{ProfileId}/x.enc", "v1:env:abc", new string('a', 64), 10, "application/pdf", expires);

    [Theory]
    [InlineData("Preference points claim form", "SBD 6.1", null, true)]
    [InlineData("Declaration of interest", "SBD4", null, true)]
    [InlineData("Pricing schedule", null, null, true)]
    [InlineData("Technical proposal and methodology", null, null, true)]
    [InlineData("CSD registration report", null, CompanyDocumentType.CsdReport, false)]
    [InlineData("Valid tax clearance certificate / PIN", null, CompanyDocumentType.TaxPin, false)]
    [InlineData("B-BBEE certificate or sworn affidavit", null, CompanyDocumentType.BbbeeCertificate, false)]
    [InlineData("CIPC company registration documents", null, CompanyDocumentType.CompanyRegistration, false)]
    [InlineData("Company profile", null, CompanyDocumentType.CompanyProfile, false)]
    [InlineData("CVs of key personnel", null, CompanyDocumentType.Cv, false)]
    [InlineData("Three contactable reference letters", null, CompanyDocumentType.Reference, false)]
    [InlineData("ISO 9001 certificate", null, CompanyDocumentType.Certification, false)]
    [InlineData("Proof of physical address", null, null, false)]
    public void Wording_maps_to_a_document_type_or_a_form(string name, string? code, CompanyDocumentType? expected, bool form)
    {
        var type = DocumentRequirementMatcher.MapType(name, code, out var isForm);

        type.Should().Be(expected);
        isForm.Should().Be(form);
    }

    [Fact]
    public void Library_check_reports_present_missing_expired_forms_and_unmapped()
    {
        var documents = new List<CompanyDocument>
        {
            Doc(CompanyDocumentType.CsdReport, "CSD report 2026", Now.AddMonths(6)),
            Doc(CompanyDocumentType.TaxPin, "Tax PIN 2025", Now.AddDays(-10)),
            Doc(CompanyDocumentType.Cv, "CV Jane"),
        };

        DocumentRequirementMatcher.Check("CSD report", null, null, documents, Now).Outcome.Should().Be(DocumentOutcome.Present);
        var expired = DocumentRequirementMatcher.Check("Tax compliance PIN", null, null, documents, Now);
        expired.Outcome.Should().Be(DocumentOutcome.Expired);
        expired.Detail.Should().Contain("renew");
        DocumentRequirementMatcher.Check("B-BBEE certificate", null, null, documents, Now).Outcome.Should().Be(DocumentOutcome.Missing);
        DocumentRequirementMatcher.Check("CVs of proposed team", null, null, documents, Now).Outcome.Should().Be(DocumentOutcome.Present);
        DocumentRequirementMatcher.Check("SBD 1 Invitation to bid", "SBD1", null, documents, Now).Outcome.Should().Be(DocumentOutcome.ToComplete);
        DocumentRequirementMatcher.Check("Bank confirmation letter", null, null, documents, Now).Outcome.Should().Be(DocumentOutcome.Unmapped);
    }

    [Fact]
    public void A_document_that_lapses_before_closing_counts_as_expired_and_deleted_copies_are_ignored()
    {
        var closing = Now.AddDays(30);
        var deleted = Doc(CompanyDocumentType.CsdReport, "CSD current", closing.AddYears(1));
        deleted.SoftDelete(Now);
        var lapsing = Doc(CompanyDocumentType.CsdReport, "CSD lapsing", Now.AddDays(10));

        var check = DocumentRequirementMatcher.Check("CSD report", null, null, [deleted, lapsing], closing);

        check.Outcome.Should().Be(DocumentOutcome.Expired);
        check.Detail.Should().Contain("CSD lapsing");
    }
}
