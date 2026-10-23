using TenderSync.Domain.Common;
using TenderSync.Domain.Enums;
using TenderSync.Domain.Events;

namespace TenderSync.Domain.Qualification;

/// <summary>Outcome of the qualification engine for one tender and company profile (PLAN.md §7.5).</summary>
public sealed class QualificationReport : Entity
{
    private QualificationReport()
    {
    }

    public Guid TenderId { get; private set; }

    public Guid CompanyProfileId { get; private set; }

    public QualificationVerdict Verdict { get; private set; }

    /// <summary>0–100 combined score.</summary>
    public int Score { get; private set; }

    public string FindingsJson { get; private set; } = "[]";

    public string MissingMandatoryJson { get; private set; } = "[]";

    public string MissingDocumentsJson { get; private set; } = "[]";

    public int? EstimatedFunctionalityScore { get; private set; }

    public string FunctionalityBreakdownJson { get; private set; } = "[]";

    public string QuestionsJson { get; private set; } = "[]";

    public string SummaryMarkdown { get; private set; } = string.Empty;

    public string Model { get; private set; } = null!;

    public string PromptVersion { get; private set; } = null!;

    public IReadOnlyList<QualificationFinding> Findings => DomainJson.Deserialize<List<QualificationFinding>>(FindingsJson) ?? [];

    public IReadOnlyList<MissingItem> MissingMandatory => DomainJson.Deserialize<List<MissingItem>>(MissingMandatoryJson) ?? [];

    public IReadOnlyList<MissingItem> MissingDocuments => DomainJson.Deserialize<List<MissingItem>>(MissingDocumentsJson) ?? [];

    public IReadOnlyList<FunctionalityScore> FunctionalityBreakdown => DomainJson.Deserialize<List<FunctionalityScore>>(FunctionalityBreakdownJson) ?? [];

    public IReadOnlyList<HumanQuestion> Questions => DomainJson.Deserialize<List<HumanQuestion>>(QuestionsJson) ?? [];

    public static QualificationReport Create(
        Guid tenderId,
        Guid companyProfileId,
        QualificationVerdict verdict,
        int score,
        IReadOnlyList<QualificationFinding> findings,
        IReadOnlyList<MissingItem> missingMandatory,
        IReadOnlyList<MissingItem> missingDocuments,
        int? estimatedFunctionalityScore,
        IReadOnlyList<FunctionalityScore> functionalityBreakdown,
        IReadOnlyList<HumanQuestion> questions,
        string summaryMarkdown,
        string model,
        string promptVersion,
        DateTimeOffset now)
    {
        var report = new QualificationReport
        {
            TenderId = tenderId,
            CompanyProfileId = companyProfileId,
            Verdict = verdict,
            Score = Guard.InRange(score, 0, 100, nameof(score)),
            FindingsJson = DomainJson.Serialize(findings),
            MissingMandatoryJson = DomainJson.Serialize(missingMandatory),
            MissingDocumentsJson = DomainJson.Serialize(missingDocuments),
            EstimatedFunctionalityScore = estimatedFunctionalityScore,
            FunctionalityBreakdownJson = DomainJson.Serialize(functionalityBreakdown),
            QuestionsJson = DomainJson.Serialize(questions),
            SummaryMarkdown = summaryMarkdown ?? string.Empty,
            Model = Guard.NotEmpty(model, nameof(model)),
            PromptVersion = Guard.NotEmpty(promptVersion, nameof(promptVersion)),
        };
        report.Raise(new QualificationCompleted(tenderId, companyProfileId, report.Id, verdict, score, now));
        return report;
    }
}

/// <summary>Result of evaluating one requirement against the profile.</summary>
public sealed record QualificationFinding(Guid? RequirementId, RequirementType Type, string Requirement, FindingOutcome Outcome, string Detail, decimal Confidence);

public enum FindingOutcome
{
    Unknown = 0,
    Pass = 1,
    Fail = 2,
    Expired = 3,
}

/// <summary>Something the company lacks for this bid: a mandatory item or a document.</summary>
public sealed record MissingItem(string Name, string? FormCode, string Reason, Guid? RequirementId);

/// <summary>Estimated score for one functionality criterion.</summary>
public sealed record FunctionalityScore(string Criterion, decimal Weight, decimal Estimated, string Reasoning, decimal Confidence);

/// <summary>Something a human must answer before the verdict can be trusted.</summary>
public sealed record HumanQuestion(string Question, string Reason, Guid? RequirementId);
