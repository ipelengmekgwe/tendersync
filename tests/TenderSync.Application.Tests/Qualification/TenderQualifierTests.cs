using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NSubstitute;
using TenderSync.Application.Abstractions;
using TenderSync.Application.Abstractions.Llm;
using TenderSync.Application.Abstractions.Persistence;
using TenderSync.Application.Common.Exceptions;
using TenderSync.Application.Features.Llm;
using TenderSync.Application.Features.Qualification;
using TenderSync.Application.Tests.Documents;
using TenderSync.Domain.Companies;
using TenderSync.Domain.Enums;
using TenderSync.Domain.Events;
using TenderSync.Domain.Qualification;
using TenderSync.Domain.Tenders;

namespace TenderSync.Application.Tests.Qualification;

/// <summary>The engine end to end with the fake model: rules, fuzzy match, document check, estimate, verdict, questions, report.</summary>
public class TenderQualifierTests
{
    private static readonly DateTimeOffset Now = DocumentTestData.Now;

    private readonly IAppDbContext _db = Substitute.For<IAppDbContext>();
    private readonly FakeLlmClient _llm = new();
    private readonly List<QualificationReport> _reports = [];
    private readonly List<CompanyDocument> _documents = [];
    private readonly List<TenderMatch> _matches = [];
    private readonly List<CompanyProfile> _profiles = [];

    private TenderQualifier Sut(Tender tender, IReadOnlyList<TenderRequirement> requirements, QualificationOptions? options = null)
    {
        _db.Tenders.GetWithDetailsAsync(tender.Id, Arg.Any<CancellationToken>()).Returns(tender);
        _db.Tenders.GetRequirementsAsync(tender.Id, Arg.Any<CancellationToken>()).Returns(requirements);
        _db.CompanyProfiles.ListActiveAsync(Arg.Any<CancellationToken>()).Returns(_ => _profiles.Where(p => p.IsActive).ToList());
        _db.CompanyProfiles.GetByIdAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>()).Returns(call => _profiles.FirstOrDefault(p => p.Id == call.Arg<Guid>()));
        _db.CompanyProfiles.GetDocumentsAsync(Arg.Any<Guid>(), Arg.Any<bool>(), Arg.Any<CancellationToken>()).Returns(call => _documents.Where(d => d.CompanyProfileId == call.Arg<Guid>()).ToList());
        _db.TenderMatches.ListByTenderAsync(tender.Id, Arg.Any<CancellationToken>()).Returns(_ => _matches.ToList());
        _db.QualificationReports.When(r => r.Add(Arg.Any<QualificationReport>())).Do(call => _reports.Add(call.Arg<QualificationReport>()));
        var prompts = Substitute.For<IPromptRegistry>();
        prompts.Get(TenderQualifier.EstimatePurpose, null).Returns(new PromptTemplate(TenderQualifier.EstimatePurpose, 1, ModelTier.Strong, "FunctionalityEstimateV1", null, "Estimate."));
        prompts.Get(TenderQualifier.FuzzyPurpose, null).Returns(new PromptTemplate(TenderQualifier.FuzzyPurpose, 1, ModelTier.Fast, "CertificationMatchV1", null, "Match."));
        var clock = Substitute.For<IDateTimeProvider>();
        clock.UtcNow.Returns(Now);
        return new TenderQualifier(_db, _llm, prompts, Options.Create(options ?? new QualificationOptions()), clock, NullLogger<TenderQualifier>.Instance);
    }

    private static Tender TenderWithDocument() => DocumentTestData.TenderWith(DocumentTestData.Document("d0", "Bid Document.pdf", "pdf"));

    private static TenderRequirement Row(Guid tenderId, RequirementKind kind, RequirementType type, string text, Guid documentId, int? page = 3, string? valueJson = null, decimal? weight = null, decimal? threshold = null, bool mandatory = true) =>
        TenderRequirement.Create(tenderId, kind, type, text, mandatory, 0.9m, new RequirementSource(documentId, page, "quote"), "claude-opus-5-5", "v1", Now, valueJson, weight, threshold);

    private CompanyProfile AddProfile(Action<CompanyProfile>? configure = null)
    {
        var profile = CompanyProfile.Create("Acme Software");
        profile.UpdateDetails("Acme Software", null, "2015/123456/07", null, 8, 25, 12_000_000m);
        profile.UpdateCompliance("MAAA0001234", Now.AddMonths(6), "PIN123", Now.AddMonths(3), 2, Now.AddMonths(2), "Sworn affidavit");
        profile.UpdateCapabilities(new CompanyCapabilities
        {
            CategoryCodes = ["SW_DEV"],
            Technologies = [".NET", "Azure"],
            Certifications = [new CompanyCertification("ISO/IEC 27001:2022", "BSI", Now.AddYears(1))],
            ReferenceProjects = [new ReferenceProject("Dept of Health", "Patient portal", 3_000_000m, Now.Year - 1, true)],
            KeyPersonnel = [new KeyPerson("Jane Doe", "Solution architect", 12, "Leads Azure migrations")],
        });
        configure?.Invoke(profile);
        _profiles.Add(profile);
        return profile;
    }

    [Fact]
    public async Task A_fully_compliant_profile_qualifies_with_every_item_sourced_and_the_estimate_above_the_threshold()
    {
        var tender = TenderWithDocument();
        var doc = tender.Documents.Single();
        var profile = AddProfile();
        _matches.Add(TenderMatch.Create(tender.Id, profile.Id, 80, Now));
        _documents.Add(CompanyDocument.Create(profile.Id, CompanyDocumentType.CsdReport, "CSD report", "companies/x/y.enc", "v1|test|abc", new string('a', 64), 10, "application/pdf", Now.AddMonths(6)));
        var rows = new List<TenderRequirement>
        {
            Row(tender.Id, RequirementKind.Mandatory, RequirementType.CsdRegistration, "Registered on the CSD", doc.Id),
            Row(tender.Id, RequirementKind.Mandatory, RequirementType.BbbeeCertificate, "B-BBEE level 4 or better", doc.Id, valueJson: "{\"value\":\"level 4\"}"),
            Row(tender.Id, RequirementKind.Functional, RequirementType.Other, "Experience in similar projects", doc.Id, weight: 60, mandatory: false),
            Row(tender.Id, RequirementKind.Functional, RequirementType.Other, "Methodology", doc.Id, weight: 40, mandatory: false),
            Row(tender.Id, RequirementKind.Evaluation, RequirementType.Other, "Functionality threshold 70/100", doc.Id, threshold: 70),
            Row(tender.Id, RequirementKind.Document, RequirementType.Other, "CSD report", doc.Id, valueJson: "{\"name\":\"CSD registration report\",\"formCode\":null}"),
            Row(tender.Id, RequirementKind.Document, RequirementType.Other, "SBD 6.1", doc.Id, valueJson: "{\"name\":\"Preference points claim form\",\"formCode\":\"SBD 6.1\"}"),
        };
        _llm.Answer(TenderQualifier.EstimatePurpose, new FunctionalityEstimateV1(
            [new("Experience in similar projects", 60, 50, "Patient portal reference", 0.8), new("Methodology", 40, 30, "No methodology evidence on file", 0.5)],
            "Strong on experience, thin on methodology.", ["Confirm a written methodology exists"]));
        _llm.Model = "claude-opus-5-5";
        _llm.CostUsd = 0.40m;

        var outcome = await Sut(tender, rows).QualifyAsync(tender.Id);

        outcome.Profiles.Should().ContainSingle();
        var result = outcome.Profiles[0];
        result.Verdict.Should().Be(QualificationVerdict.Qualify);
        result.EstimatedFunctionality.Should().Be(80, "(50 + 30) / 100");
        result.CostUsd.Should().Be(0.40m);
        outcome.Errors.Should().BeEmpty();

        var report = _reports.Should().ContainSingle().Subject;
        report.Findings.Should().AllSatisfy(f => f.Outcome.Should().Be(FindingOutcome.Pass));
        report.MissingMandatory.Should().BeEmpty();
        report.MissingDocuments.Should().BeEmpty();
        report.Questions.Select(q => q.Question).Should().ContainSingle(q => q.Contains("methodology", StringComparison.OrdinalIgnoreCase));
        report.Model.Should().Be("claude-opus-5-5");
        report.PromptVersion.Should().Be("v1");
        report.DomainEvents.Should().ContainSingle(e => e is QualificationCompleted);
        report.SummaryMarkdown.Should().Contain("**Disclaimer.**").And.Contain("Verdict: Qualify").And.Contain("[Bid Document.pdf p.3](/api/v1/tenders/").And.Contain("📝 To complete");
        await _db.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());

        // The estimate prompt carries the criteria and the profile facts but no personal names (consent not given).
        var estimateRequest = _llm.Requests.Single(r => r.Purpose == TenderQualifier.EstimatePurpose);
        var text = estimateRequest.UserParts.OfType<LlmTextPart>().Single().Text;
        text.Should().Contain("[60 points]").And.Contain("Patient portal").And.Contain("Key personnel: not shared").And.NotContain("Jane Doe");
        estimateRequest.Tier.Should().Be(ModelTier.Strong);
    }

    [Fact]
    public async Task A_failed_mandatory_item_is_not_qualify_and_an_unknown_one_is_partial_with_a_question()
    {
        var tender = TenderWithDocument();
        var doc = tender.Documents.Single();
        AddProfile(p => p.UpdateCompliance(null, null, "PIN", null, 2, null, null));
        var rows = new List<TenderRequirement>
        {
            Row(tender.Id, RequirementKind.Mandatory, RequirementType.CsdRegistration, "Registered on the CSD", doc.Id),
            Row(tender.Id, RequirementKind.Mandatory, RequirementType.Insurance, "Professional indemnity cover of R5 million", doc.Id),
        };

        var outcome = await Sut(tender, rows).QualifyAsync(tender.Id);

        outcome.Profiles[0].Verdict.Should().Be(QualificationVerdict.NotQualify);
        var report = _reports.Single();
        report.MissingMandatory.Should().ContainSingle(m => m.Name == "Registered on the CSD");
        report.MissingDocuments.Should().ContainSingle(m => m.Name == "CSD registration report", "a failed CSD check implies the report is missing");
        report.Questions.Should().ContainSingle(q => q.Question.Contains("Professional indemnity"));
        report.Model.Should().Be(TenderQualifier.RulesOnlyModel, "no model call was needed");
        _llm.Requests.Should().BeEmpty();

        _reports.Clear();
        var partial = await Sut(tender, [rows[1]]).QualifyAsync(tender.Id);
        partial.Profiles[0].Verdict.Should().Be(QualificationVerdict.Partial);
    }

    [Fact]
    public async Task Certification_wording_goes_to_the_fast_model_and_a_confident_match_passes_with_a_confirmation_question()
    {
        var tender = TenderWithDocument();
        var doc = tender.Documents.Single();
        AddProfile();
        var rows = new List<TenderRequirement>
        {
            Row(tender.Id, RequirementKind.Mandatory, RequirementType.Certification, "Bidder must be ISO 27001 certified", doc.Id),
            Row(tender.Id, RequirementKind.Mandatory, RequirementType.Certification, "Microsoft Gold Partner", doc.Id),
        };
        _llm.Answer(TenderQualifier.FuzzyPurpose, new CertificationMatchV1(
        [
            new(0, "ISO/IEC 27001:2022", 0.95, "Same standard, current revision"),
            new(1, null, 0.0, "No Microsoft partner status listed"),
        ]));

        var outcome = await Sut(tender, rows).QualifyAsync(tender.Id);

        outcome.Profiles[0].Verdict.Should().Be(QualificationVerdict.Partial, "the partner requirement stays unknown");
        var report = _reports.Single();
        report.Findings[0].Outcome.Should().Be(FindingOutcome.Pass);
        report.Findings[0].Detail.Should().Contain("Model matched");
        report.Findings[1].Outcome.Should().Be(FindingOutcome.Unknown);
        report.Findings[1].Detail.Should().Contain("No Microsoft partner");
        report.Questions.Should().Contain(q => q.Question.StartsWith("Confirm that \"ISO/IEC 27001:2022\"", StringComparison.Ordinal));
        report.Questions.Should().Contain(q => q.Question.Contains("Microsoft Gold Partner"));
        _llm.Requests.Single().Tier.Should().Be(ModelTier.Fast);
    }

    [Fact]
    public async Task Expired_items_and_documents_make_it_partial_and_become_questions()
    {
        var tender = TenderWithDocument();
        var doc = tender.Documents.Single();
        var profile = AddProfile(p => p.UpdateCompliance("MAAA1", Now.AddDays(5), "PIN", null, 2, null, null));
        _documents.Add(CompanyDocument.Create(profile.Id, CompanyDocumentType.TaxPin, "Tax PIN 2025", "companies/x/z.enc", "v1|test|abc", new string('a', 64), 10, "application/pdf", Now.AddDays(-1)));
        var rows = new List<TenderRequirement>
        {
            Row(tender.Id, RequirementKind.Mandatory, RequirementType.CsdRegistration, "Registered on the CSD", doc.Id),
            Row(tender.Id, RequirementKind.Document, RequirementType.Other, "Tax PIN", doc.Id, valueJson: "{\"name\":\"Valid tax compliance PIN\"}"),
        };

        var outcome = await Sut(tender, rows).QualifyAsync(tender.Id);

        outcome.Profiles[0].Verdict.Should().Be(QualificationVerdict.Partial);
        var report = _reports.Single();
        report.Findings.Single().Outcome.Should().Be(FindingOutcome.Expired);
        report.MissingDocuments.Should().ContainSingle(m => m.Reason.Contains("expired"));
        report.Questions.Select(q => q.Question).Should().Contain(q => q.StartsWith("Can this be renewed", StringComparison.Ordinal)).And.Contain(q => q.StartsWith("Renew and upload", StringComparison.Ordinal));
    }

    [Fact]
    public async Task A_compulsory_briefing_and_unmapped_documents_become_questions()
    {
        var tender = TenderWithDocument();
        tender.SetBriefing(Now.AddDays(3), true, "Boardroom A");
        var doc = tender.Documents.Single();
        AddProfile();
        var rows = new List<TenderRequirement>
        {
            Row(tender.Id, RequirementKind.Document, RequirementType.Other, "Bank letter", doc.Id, valueJson: "{\"name\":\"Bank confirmation letter\"}"),
        };

        await Sut(tender, rows).QualifyAsync(tender.Id);

        var questions = _reports.Single().Questions.Select(q => q.Question).ToList();
        questions.Should().Contain(q => q.StartsWith("Who attends the compulsory briefing", StringComparison.Ordinal));
        questions.Should().Contain(q => q.Contains("Bank confirmation letter"));
    }

    [Fact]
    public async Task Profiles_are_the_named_one_then_matched_ones_then_every_active_one()
    {
        var tender = TenderWithDocument();
        var doc = tender.Documents.Single();
        var acme = AddProfile();
        var other = AddProfile(p => p.UpdateDetails("Other Co", null, null, null, 1, null, null));
        var inactive = AddProfile(p => p.SetActive(false));
        var rows = new List<TenderRequirement> { Row(tender.Id, RequirementKind.Mandatory, RequirementType.CsdRegistration, "CSD", doc.Id) };

        (await Sut(tender, rows).QualifyAsync(tender.Id)).Profiles.Select(p => p.CompanyProfileId).Should().BeEquivalentTo([acme.Id, other.Id], "no match: every active profile");

        _matches.Add(TenderMatch.Create(tender.Id, other.Id, 70, Now));
        (await Sut(tender, rows).QualifyAsync(tender.Id)).Profiles.Select(p => p.CompanyProfileId).Should().Equal(other.Id);

        (await Sut(tender, rows).QualifyAsync(tender.Id, inactive.Id)).Profiles.Select(p => p.CompanyProfileId).Should().Equal([inactive.Id], "an explicit profile is used as given");
        await FluentActions.Awaiting(() => Sut(tender, rows).QualifyAsync(tender.Id, Guid.CreateVersion7())).Should().ThrowAsync<NotFoundException>();
    }

    [Fact]
    public async Task Without_requirements_or_an_active_profile_the_run_is_refused()
    {
        var tender = TenderWithDocument();
        var doc = tender.Documents.Single();

        await FluentActions.Awaiting(() => Sut(tender, []).QualifyAsync(tender.Id)).Should().ThrowAsync<ConflictException>().WithMessage("*no extracted requirements*");

        var rows = new List<TenderRequirement> { Row(tender.Id, RequirementKind.Mandatory, RequirementType.CsdRegistration, "CSD", doc.Id) };
        await FluentActions.Awaiting(() => Sut(tender, rows).QualifyAsync(tender.Id)).Should().ThrowAsync<ConflictException>().WithMessage("*No active company profile*");
    }

    [Fact]
    public async Task Key_personnel_are_shared_by_role_only_when_the_profile_opts_in()
    {
        var tender = TenderWithDocument();
        var doc = tender.Documents.Single();
        AddProfile(p => p.SetModelSharing(true));
        var rows = new List<TenderRequirement> { Row(tender.Id, RequirementKind.Functional, RequirementType.Other, "Team experience", doc.Id, weight: 100, mandatory: false) };
        _llm.Answer(TenderQualifier.EstimatePurpose, new FunctionalityEstimateV1([new("Team experience", 100, 70, "Architect with 12 years", 0.7)], "Fine.", []));

        await Sut(tender, rows).QualifyAsync(tender.Id);

        var text = _llm.Requests.Single().UserParts.OfType<LlmTextPart>().Single().Text;
        text.Should().Contain("Solution architect, 12 years: Leads Azure migrations").And.NotContain("Jane Doe");
    }
}
