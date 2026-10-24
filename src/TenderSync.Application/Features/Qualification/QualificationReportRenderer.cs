using System.Globalization;
using System.Text;
using TenderSync.Application.Common;
using TenderSync.Domain.Companies;
using TenderSync.Domain.Enums;
using TenderSync.Domain.Qualification;
using TenderSync.Domain.Tenders;

namespace TenderSync.Application.Features.Qualification;

/// <summary>Everything the markdown report shows.</summary>
public sealed record QualificationReportContext(
    Tender Tender,
    CompanyProfile Profile,
    IReadOnlyList<TenderRequirement> Requirements,
    IReadOnlyList<QualificationFinding> Findings,
    IReadOnlyList<DocumentCheck> Documents,
    IReadOnlyList<FunctionalityScore> Functionality,
    int? EstimatedFunctionality,
    decimal? FunctionalityThreshold,
    string? FunctionalityAssessment,
    IReadOnlyList<HumanQuestion> Questions,
    IReadOnlyList<MissingItem> MissingMandatory,
    IReadOnlyList<MissingItem> MissingDocuments,
    QualificationVerdict Verdict,
    int Score,
    string Model,
    DateTimeOffset GeneratedAt);

/// <summary>
/// Renders the stored markdown (PLAN.md §7.5 step 6): disclaimer banner first, verdict, then every finding with its source
/// (document title, page and the API path of the stored copy) so a reviewer can check each line against the bid pack.
/// </summary>
public static class QualificationReportRenderer
{
    public const string Disclaimer =
        "This report was generated automatically from the bid documents published on the eTenders portal and from the company profile stored in TenderSync. " +
        "It is an aid to a human decision, not advice: verify every item against the source document before bidding, and treat every question below as unanswered until a person has answered it.";

    public static string Render(QualificationReportContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        var tender = context.Tender;
        var sb = new StringBuilder();
        var requirements = context.Requirements.ToDictionary(r => r.Id);
        var titles = tender.Documents.ToDictionary(d => d.Id, d => d.Title);

        sb.Append("# Can we bid? ").Append(Escape(tender.BidNumber)).Append(": ").AppendLine(Escape(tender.Title));
        sb.AppendLine();
        sb.Append("> **Disclaimer.** ").AppendLine(Disclaimer);
        sb.Append("> Source: [").Append(Escape(tender.SourceUrl)).Append("](").Append(tender.SourceUrl).AppendLine(")");
        sb.AppendLine();

        sb.Append("**Verdict: ").Append(VerdictText(context.Verdict)).Append("** (score ").Append(context.Score.ToString(CultureInfo.InvariantCulture)).AppendLine("/100)");
        sb.AppendLine();
        sb.Append("- Company: ").AppendLine(Escape(context.Profile.Name));
        sb.Append("- Buyer: ").AppendLine(Escape(tender.Buyer?.Name ?? "unknown"));
        sb.Append("- Closing: ").AppendLine(tender.ClosingAt is { } closing ? Sast(closing) : "not stated");
        if (tender.BriefingAt is { } briefing)
        {
            sb.Append("- Briefing: ").Append(Sast(briefing)).Append(tender.BriefingCompulsory == true ? " (compulsory)" : tender.BriefingCompulsory == false ? " (optional)" : string.Empty).AppendLine(tender.BriefingVenue is null ? string.Empty : $", {Escape(tender.BriefingVenue)}");
        }

        sb.Append("- Generated: ").Append(Sast(context.GeneratedAt)).Append(" by ").AppendLine(Escape(context.Model));
        sb.AppendLine();

        sb.AppendLine("## Mandatory requirements");
        sb.AppendLine();
        if (context.Findings.Count == 0)
        {
            sb.AppendLine("No mandatory requirements were extracted from the bid pack.");
        }
        else
        {
            sb.AppendLine("| Outcome | Requirement | Finding | Source |");
            sb.AppendLine("|---|---|---|---|");
            foreach (var finding in context.Findings)
            {
                var source = finding.RequirementId is { } id && requirements.TryGetValue(id, out var row) ? SourceLink(tender.Id, row, titles) : string.Empty;
                sb.Append("| ").Append(OutcomeText(finding.Outcome)).Append(" | ").Append(Cell(finding.Requirement)).Append(" | ").Append(Cell(finding.Detail)).Append(" | ").Append(source).AppendLine(" |");
            }
        }

        sb.AppendLine();
        sb.AppendLine("## Required documents");
        sb.AppendLine();
        if (context.Documents.Count == 0)
        {
            sb.AppendLine("No required documents were extracted from the bid pack.");
        }
        else
        {
            sb.AppendLine("| Status | Document | Library | Source |");
            sb.AppendLine("|---|---|---|---|");
            foreach (var check in context.Documents)
            {
                var source = check.RequirementId is { } id && requirements.TryGetValue(id, out var row) ? SourceLink(tender.Id, row, titles) : string.Empty;
                var name = string.IsNullOrWhiteSpace(check.FormCode) ? check.Name : $"{check.FormCode}: {check.Name}";
                sb.Append("| ").Append(DocumentText(check.Outcome)).Append(" | ").Append(Cell(name)).Append(" | ").Append(Cell(check.Detail)).Append(" | ").Append(source).AppendLine(" |");
            }
        }

        sb.AppendLine();
        sb.AppendLine("## Functionality estimate");
        sb.AppendLine();
        if (context.Functionality.Count == 0)
        {
            sb.AppendLine("The bid pack lists no functionality criteria, or they were not extracted.");
        }
        else
        {
            sb.Append("Estimated **").Append((context.EstimatedFunctionality ?? 0).ToString(CultureInfo.InvariantCulture)).Append("/100**");
            sb.AppendLine(context.FunctionalityThreshold is { } threshold ? $" against a threshold of {threshold.ToString("0.#", CultureInfo.InvariantCulture)}." : "; the pack states no threshold.");
            sb.AppendLine();
            sb.AppendLine("| Criterion | Weight | Estimate | Confidence | Reasoning |");
            sb.AppendLine("|---|---|---|---|---|");
            foreach (var item in context.Functionality)
            {
                sb.Append("| ").Append(Cell(item.Criterion)).Append(" | ").Append(item.Weight.ToString("0.#", CultureInfo.InvariantCulture)).Append(" | ").Append(item.Estimated.ToString("0.#", CultureInfo.InvariantCulture))
                    .Append(" | ").Append(item.Confidence.ToString("0%", CultureInfo.InvariantCulture)).Append(" | ").Append(Cell(item.Reasoning)).AppendLine(" |");
            }

            if (!string.IsNullOrWhiteSpace(context.FunctionalityAssessment))
            {
                sb.AppendLine();
                sb.AppendLine(Escape(context.FunctionalityAssessment));
            }
        }

        sb.AppendLine();
        sb.AppendLine("## Questions for a human");
        sb.AppendLine();
        if (context.Questions.Count == 0)
        {
            sb.AppendLine("None: every item was decided from the profile.");
        }
        else
        {
            var n = 1;
            foreach (var question in context.Questions)
            {
                sb.Append(n++.ToString(CultureInfo.InvariantCulture)).Append(". ").Append(Escape(question.Question)).Append(" _").Append(Escape(question.Reason)).AppendLine("_");
            }
        }

        if (context.MissingMandatory.Count > 0 || context.MissingDocuments.Count > 0)
        {
            sb.AppendLine();
            sb.AppendLine("## Missing");
            sb.AppendLine();
            foreach (var item in context.MissingMandatory)
            {
                sb.Append("- Mandatory: ").Append(Escape(item.Name)).Append(" — ").AppendLine(Escape(item.Reason));
            }

            foreach (var item in context.MissingDocuments)
            {
                sb.Append("- Document: ").Append(Escape(string.IsNullOrWhiteSpace(item.FormCode) ? item.Name : $"{item.FormCode}: {item.Name}")).Append(" — ").AppendLine(Escape(item.Reason));
            }
        }

        return sb.ToString().TrimEnd() + "\n";
    }

    public static string VerdictText(QualificationVerdict verdict) => verdict switch
    {
        QualificationVerdict.Qualify => "Qualify",
        QualificationVerdict.Partial => "Partial: open questions",
        QualificationVerdict.NotQualify => "Do not qualify",
        _ => "Unknown",
    };

    private static string OutcomeText(FindingOutcome outcome) => outcome switch
    {
        FindingOutcome.Pass => "✅ Pass",
        FindingOutcome.Fail => "❌ Fail",
        FindingOutcome.Expired => "⏳ Expired",
        _ => "❓ Unknown",
    };

    private static string DocumentText(DocumentOutcome outcome) => outcome switch
    {
        DocumentOutcome.Present => "✅ In library",
        DocumentOutcome.Missing => "❌ Missing",
        DocumentOutcome.Expired => "⏳ Expired",
        DocumentOutcome.ToComplete => "📝 To complete",
        _ => "❓ Check",
    };

    /// <summary>"[Bid Document.pdf p.3](/api/v1/tenders/{id}/documents/{docId}/download)" or "unknown" so a missing source is visible.</summary>
    public static string SourceLink(Guid tenderId, TenderRequirement requirement, IReadOnlyDictionary<Guid, string> documentTitles)
    {
        ArgumentNullException.ThrowIfNull(requirement);
        ArgumentNullException.ThrowIfNull(documentTitles);
        if (requirement.Source is not { DocumentId: { } documentId } source)
        {
            return "unknown (verify)";
        }

        var title = documentTitles.TryGetValue(documentId, out var t) ? t : "document";
        var page = source.Page is { } p ? $" p.{p.ToString(CultureInfo.InvariantCulture)}" : " (page unknown, verify)";
        return $"[{Cell(title)}{page}](/api/v1/tenders/{tenderId}/documents/{documentId}/download)";
    }

    private static string Sast(DateTimeOffset value) => SouthAfricaTime.ToLocal(value).ToString("ddd d MMM yyyy HH:mm", CultureInfo.InvariantCulture) + " SAST";

    private static string Escape(string? text) => (text ?? string.Empty).Replace("\r", string.Empty, StringComparison.Ordinal).Replace("\n", " ", StringComparison.Ordinal).Trim();

    private static string Cell(string? text) => Escape(text).Replace("|", "\\|", StringComparison.Ordinal);
}
