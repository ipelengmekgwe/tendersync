using System.Globalization;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using TenderSync.Application.Abstractions;
using TenderSync.Application.Abstractions.Llm;
using TenderSync.Application.Abstractions.Persistence;
using TenderSync.Application.Common;
using TenderSync.Application.Common.Exceptions;
using TenderSync.Domain.Companies;
using TenderSync.Domain.Enums;
using TenderSync.Domain.Qualification;
using TenderSync.Domain.Tenders;

namespace TenderSync.Application.Features.Qualification;

public sealed record ProfileQualificationOutcome(Guid CompanyProfileId, string CompanyName, Guid ReportId, QualificationVerdict Verdict, int Score, int? EstimatedFunctionality, int Questions, decimal CostUsd);

public sealed record QualificationOutcome(Guid TenderId, string Ocid, IReadOnlyList<ProfileQualificationOutcome> Profiles, decimal CostUsd, IReadOnlyList<string> Errors);

/// <summary>
/// PLAN.md §7.5, "deterministic rules first, LLM second, never the reverse for hard facts": the Domain evaluators decide every
/// mandatory item from the profile; the fast model only resolves certification wording the rules could not match; the strong
/// model estimates functionality points from the profile facts. The verdict and score come from <see cref="VerdictRules"/>,
/// the markdown from <see cref="QualificationReportRenderer"/>, and one <see cref="QualificationReport"/> row per profile is
/// added (its creation raises QualificationCompleted through the outbox).
/// </summary>
public sealed class TenderQualifier(
    IAppDbContext db,
    ILlmClient llm,
    IPromptRegistry prompts,
    IOptions<QualificationOptions> options,
    IDateTimeProvider clock,
    ILogger<TenderQualifier> logger)
{
    public const string EstimatePurpose = "estimate-functionality";
    public const string FuzzyPurpose = "fuzzy-match-certification";
    public const string RulesOnlyModel = "rules-only";

    public async Task<QualificationOutcome> QualifyAsync(Guid tenderId, Guid? companyProfileId = null, CancellationToken cancellationToken = default)
    {
        var tender = await db.Tenders.GetWithDetailsAsync(tenderId, cancellationToken)
            ?? throw new NotFoundException(nameof(Tender), tenderId);
        var requirements = await db.Tenders.GetRequirementsAsync(tender.Id, cancellationToken);
        if (requirements.Count == 0)
        {
            throw new ConflictException($"Tender '{tender.BidNumber}' has no extracted requirements; run requirement extraction first.");
        }

        var profiles = await ResolveProfilesAsync(tender.Id, companyProfileId, cancellationToken);
        var outcomes = new List<ProfileQualificationOutcome>();
        var errors = new List<string>();
        foreach (var profile in profiles)
        {
            try
            {
                outcomes.Add(await QualifyProfileAsync(tender, requirements, profile, cancellationToken));
            }
            catch (LlmException ex)
            {
                errors.Add($"{profile.Name}: {ex.Message}");
                logger.LogError(ex, "Qualification of {Ocid} for {Company} failed at the model", tender.Ocid, profile.Name);
            }
        }

        if (outcomes.Count == 0 && errors.Count > 0)
        {
            throw new LlmException($"Qualification of {tender.Ocid} failed: {string.Join("; ", errors)}");
        }

        return new QualificationOutcome(tender.Id, tender.Ocid, outcomes, outcomes.Sum(o => o.CostUsd), errors);
    }

    /// <summary>The named profile; otherwise the active profiles matched to the tender; otherwise every active profile (an explicit request on an unmatched tender).</summary>
    private async Task<IReadOnlyList<CompanyProfile>> ResolveProfilesAsync(Guid tenderId, Guid? companyProfileId, CancellationToken cancellationToken)
    {
        if (companyProfileId is { } id)
        {
            var profile = await db.CompanyProfiles.GetByIdAsync(id, cancellationToken) ?? throw new NotFoundException(nameof(CompanyProfile), id);
            return [profile];
        }

        var active = await db.CompanyProfiles.ListActiveAsync(cancellationToken);
        if (active.Count == 0)
        {
            throw new ConflictException("No active company profile exists; create one before qualifying tenders.");
        }

        var matched = (await db.TenderMatches.ListByTenderAsync(tenderId, cancellationToken)).Select(m => m.CompanyProfileId).ToHashSet();
        var matchedProfiles = active.Where(p => matched.Contains(p.Id)).ToList();
        return matchedProfiles.Count > 0 ? matchedProfiles : active;
    }

    private async Task<ProfileQualificationOutcome> QualifyProfileAsync(Tender tender, IReadOnlyList<TenderRequirement> requirements, CompanyProfile profile, CancellationToken cancellationToken)
    {
        var settings = options.Value;
        var now = clock.UtcNow;
        var documents = await db.CompanyProfiles.GetDocumentsAsync(profile.Id, includeDeleted: false, cancellationToken);
        var validityPoint = tender.ClosingAt is { } closing && closing > now ? closing : now;
        var cost = 0m;
        string? model = null;
        string? promptVersion = null;
        var questions = new List<HumanQuestion>();

        // Step 1: rules.
        var findings = new List<QualificationFinding>();
        var fuzzyCandidates = new List<int>();
        var impliedMissing = new List<(CompanyDocumentType Type, TenderRequirement Row)>();
        foreach (var row in requirements.Where(r => r.Kind == RequirementKind.Mandatory))
        {
            var evaluation = RequirementEvaluators.Evaluate(new EvaluationContext(row, profile, documents, tender.ClosingAt, now));
            findings.Add(new QualificationFinding(row.Id, row.Type, row.Text, evaluation.Outcome, evaluation.Detail, evaluation.Confidence));
            if (evaluation.NeedsFuzzyMatch)
            {
                fuzzyCandidates.Add(findings.Count - 1);
            }

            if (evaluation.MissingDocument is { } missing)
            {
                impliedMissing.Add((missing, row));
            }
        }

        // Step 1b: certification wording the rules could not match goes to the fast model.
        if (fuzzyCandidates.Count > 0)
        {
            var fuzzy = await FuzzyMatchAsync(tender, profile, findings, fuzzyCandidates, validityPoint, settings, cancellationToken);
            cost += fuzzy.CostUsd;
            model ??= fuzzy.Model;
            questions.AddRange(fuzzy.Questions);
        }

        // Step 2: required documents against the library.
        var checks = requirements.Where(r => r.Kind == RequirementKind.Document)
            .Select(row =>
            {
                var (name, formCode) = DocumentNameOf(row);
                return DocumentRequirementMatcher.Check(name, formCode, row.Id, documents, validityPoint);
            })
            .ToList();

        // Step 3: functionality estimate.
        var functional = requirements.Where(r => r.Kind == RequirementKind.Functional).ToList();
        var threshold = requirements.FirstOrDefault(r => r.Kind == RequirementKind.Evaluation)?.Threshold ?? functional.Select(f => f.Threshold).FirstOrDefault(t => t is not null);
        var breakdown = new List<FunctionalityScore>();
        int? estimated = null;
        string? assessment = null;
        if (functional.Count > 0)
        {
            var estimate = await EstimateAsync(tender, profile, functional, settings, cancellationToken);
            cost += estimate.CostUsd;
            model = estimate.Model;
            promptVersion = estimate.PromptVersion;
            breakdown = estimate.Breakdown;
            estimated = estimate.Score;
            assessment = estimate.Assessment;
            questions.AddRange(estimate.Questions);
        }

        // Step 4: verdict and score.
        var verdict = VerdictRules.Decide(findings, estimated, threshold);
        var score = VerdictRules.Score(findings, estimated, checks, settings.Weights);

        // Step 5: questions for a human.
        questions.InsertRange(0, QuestionsFrom(findings, checks, tender, now));

        // Missing lists.
        var missingMandatory = findings.Where(f => f.Outcome is FindingOutcome.Fail or FindingOutcome.Expired)
            .Select(f => new MissingItem(f.Requirement, null, f.Detail, f.RequirementId)).ToList();
        var missingDocuments = checks.Where(c => c.Outcome is DocumentOutcome.Missing or DocumentOutcome.Expired)
            .Select(c => new MissingItem(c.Name, c.FormCode, c.Detail, c.RequirementId)).ToList();
        foreach (var (type, row) in impliedMissing)
        {
            if (!checks.Any(c => c.Type == type))
            {
                missingDocuments.Add(new MissingItem(DocumentRequirementMatcher.Describe(type), null, $"Needed for: {row.Text}", row.Id));
            }
        }

        var modelName = model ?? RulesOnlyModel;
        var version = promptVersion ?? prompts.Get(EstimatePurpose).VersionTag;
        var markdown = QualificationReportRenderer.Render(new QualificationReportContext(
            tender, profile, requirements, findings, checks, breakdown, estimated, threshold, assessment, questions, missingMandatory, missingDocuments, verdict, score, modelName, now));

        var report = QualificationReport.Create(tender.Id, profile.Id, verdict, score, findings, missingMandatory, missingDocuments, estimated, breakdown, questions, markdown, modelName, version, now);
        db.QualificationReports.Add(report);
        await db.SaveChangesAsync(cancellationToken);

        logger.LogInformation("Qualified {Ocid} for {Company}: {Verdict}, score {Score}, functionality {Estimate}, {Questions} question(s), ${Cost}",
            tender.Ocid, profile.Name, verdict, score, estimated, questions.Count, cost);
        return new ProfileQualificationOutcome(profile.Id, profile.Name, report.Id, verdict, score, estimated, questions.Count, cost);
    }

    private sealed record FuzzyResult(decimal CostUsd, string? Model, IReadOnlyList<HumanQuestion> Questions);

    private async Task<FuzzyResult> FuzzyMatchAsync(Tender tender, CompanyProfile profile, List<QualificationFinding> findings, List<int> candidates, DateTimeOffset validityPoint, QualificationOptions settings, CancellationToken cancellationToken)
    {
        var prompt = prompts.Get(FuzzyPurpose);
        var sb = new StringBuilder();
        sb.AppendLine("Company certifications:");
        foreach (var certification in profile.Capabilities.Certifications)
        {
            sb.Append("- ").Append(certification.Name);
            if (certification.Issuer is not null)
            {
                sb.Append(" (").Append(certification.Issuer).Append(')');
            }

            sb.AppendLine();
        }

        sb.AppendLine();
        sb.AppendLine("Requirements:");
        for (var i = 0; i < candidates.Count; i++)
        {
            sb.Append(i.ToString(CultureInfo.InvariantCulture)).Append(". ").AppendLine(findings[candidates[i]].Requirement);
        }

        var request = new LlmRequest(FuzzyPurpose, prompt.VersionTag, prompt.Body, [new LlmTextPart(sb.ToString())], prompt.Tier, LlmEffort.Low, settings.FuzzyMaxOutputTokens, EnableCitations: false, TenderId: tender.Id);
        var result = await llm.CompleteAsync<CertificationMatchV1>(request, cancellationToken);

        var questions = new List<HumanQuestion>();
        foreach (var match in result.Value.Matches)
        {
            if (match.RequirementIndex < 0 || match.RequirementIndex >= candidates.Count)
            {
                continue;
            }

            var index = candidates[match.RequirementIndex];
            var finding = findings[index];
            var confidence = (decimal)Math.Clamp(match.Confidence, 0, 1);
            if (match.MatchedCertification is { } suggested && match.Confidence >= settings.FuzzyMatchMinConfidence)
            {
                // The model only judges whether two wordings name the same certification. Whether the company holds it,
                // and whether it is valid at closing, stays a fact read from the profile: a name the profile does not
                // list (a hallucination, or wording planted in the bid documents) leaves the requirement Unknown.
                var certification = FindCertification(profile, suggested);
                if (certification is null)
                {
                    findings[index] = finding with { Detail = $"{finding.Detail} The model named \"{suggested}\", which is not a certification on the profile." };
                }
                else if (certification.ExpiresAt is { } expires && expires <= validityPoint)
                {
                    findings[index] = finding with { Outcome = FindingOutcome.Expired, Detail = $"Model matched \"{certification.Name}\", which expires {RequirementEvaluators.Date(expires)}, before the bid closes: {match.Reasoning}", Confidence = confidence };
                }
                else
                {
                    findings[index] = finding with { Outcome = FindingOutcome.Pass, Detail = $"Model matched \"{certification.Name}\": {match.Reasoning}", Confidence = confidence };
                    questions.Add(new HumanQuestion($"Confirm that \"{certification.Name}\" satisfies: {finding.Requirement}", $"Matched by the model at {match.Confidence.ToString("0%", CultureInfo.InvariantCulture)} confidence, not by name.", finding.RequirementId));
                }
            }
            else
            {
                findings[index] = finding with { Detail = $"{finding.Detail} Model: {match.Reasoning}" };
            }
        }

        return new FuzzyResult(result.CostUsd, result.Model, questions);
    }

    /// <summary>The profile certification the model named, compared after normalising; the model may append the issuer.</summary>
    private static CompanyCertification? FindCertification(CompanyProfile profile, string name)
    {
        var wanted = CertificationEvaluator.Normalise(name);
        return profile.Capabilities.Certifications.FirstOrDefault(c =>
            CertificationEvaluator.Normalise(c.Name) == wanted
            || (c.Issuer is { } issuer && CertificationEvaluator.Normalise($"{c.Name} {issuer}") == wanted));
    }

    private sealed record EstimateResult(decimal CostUsd, string Model, string PromptVersion, List<FunctionalityScore> Breakdown, int Score, string Assessment, IReadOnlyList<HumanQuestion> Questions);

    private async Task<EstimateResult> EstimateAsync(Tender tender, CompanyProfile profile, List<TenderRequirement> functional, QualificationOptions settings, CancellationToken cancellationToken)
    {
        var prompt = prompts.Get(EstimatePurpose);
        var request = new LlmRequest(EstimatePurpose, prompt.VersionTag, prompt.Body, [new LlmTextPart(BuildEstimateText(tender, profile, functional, settings))],
            settings.EstimateTier, settings.EstimateEffort, settings.EstimateMaxOutputTokens, EnableCitations: false, TenderId: tender.Id);
        var result = await llm.CompleteAsync<FunctionalityEstimateV1>(request, cancellationToken);

        var breakdown = new List<FunctionalityScore>();
        for (var i = 0; i < result.Value.Criteria.Count; i++)
        {
            var item = result.Value.Criteria[i];
            var row = i < functional.Count ? functional[i] : null;
            var weight = row?.Weight ?? (decimal)Math.Max(item.MaxPoints, 0);
            if (weight <= 0)
            {
                weight = 100;
            }

            var points = (decimal)Math.Clamp(double.IsNaN(item.EstimatedPoints) ? 0 : item.EstimatedPoints, 0, (double)weight);
            breakdown.Add(new FunctionalityScore(row?.Text ?? item.Criterion, weight, points, item.Reasoning, (decimal)Math.Clamp(item.Confidence, 0, 1)));
        }

        var totalWeight = breakdown.Sum(b => b.Weight);
        var score = totalWeight <= 0 ? 0 : (int)Math.Round(breakdown.Sum(b => b.Estimated) / totalWeight * 100m, MidpointRounding.AwayFromZero);
        var questions = result.Value.QuestionsForBidder.Where(q => !string.IsNullOrWhiteSpace(q)).Select(q => new HumanQuestion(q.Trim(), "Raised by the functionality estimate.", null)).ToList();
        return new EstimateResult(result.CostUsd, result.Model, prompt.VersionTag, breakdown, Math.Clamp(score, 0, 100), result.Value.OverallAssessment, questions);
    }

    /// <summary>The varying part of the estimate prompt: criteria, then the profile facts. Key personnel only with the owner's consent (PLAN.md §12), and never by name.</summary>
    public static string BuildEstimateText(Tender tender, CompanyProfile profile, IReadOnlyList<TenderRequirement> functional, QualificationOptions settings)
    {
        ArgumentNullException.ThrowIfNull(tender);
        ArgumentNullException.ThrowIfNull(profile);
        ArgumentNullException.ThrowIfNull(functional);
        ArgumentNullException.ThrowIfNull(settings);
        var sb = new StringBuilder();
        sb.Append("Tender ").Append(tender.BidNumber).Append(": ").AppendLine(tender.Title);
        if (!string.IsNullOrWhiteSpace(tender.Summary))
        {
            sb.Append("Scope: ").AppendLine(tender.Summary);
        }

        sb.AppendLine();
        sb.AppendLine("Functionality criteria:");
        foreach (var row in functional)
        {
            sb.Append("- ").Append(row.Text);
            sb.Append(row.Weight is { } w ? $" [{w.ToString("0.#", CultureInfo.InvariantCulture)} points]" : " [no weight stated]");
            var notes = ScoringNotesOf(row);
            if (notes is not null)
            {
                sb.Append(" Scoring: ").Append(notes);
            }

            sb.AppendLine();
        }

        var capabilities = profile.Capabilities;
        sb.AppendLine();
        sb.AppendLine("Company summary:");
        sb.Append("- Name: ").AppendLine(profile.Name);
        sb.Append("- Years trading: ").AppendLine(profile.YearsTrading > 0 ? profile.YearsTrading.ToString(CultureInfo.InvariantCulture) : "not stated");
        sb.Append("- Employees: ").AppendLine(profile.EmployeeCount?.ToString(CultureInfo.InvariantCulture) ?? "not stated");
        sb.Append("- B-BBEE level: ").AppendLine(profile.BbbeeLevel?.ToString(CultureInfo.InvariantCulture) ?? "not stated");
        sb.Append("- Provinces: ").AppendLine(profile.Provinces.Length == 0 ? "not stated" : string.Join(", ", profile.Provinces));
        sb.Append("- Technologies: ").AppendLine(capabilities.Technologies.Count == 0 ? "none listed" : string.Join(", ", capabilities.Technologies));
        sb.Append("- Industries: ").AppendLine(capabilities.Industries.Count == 0 ? "none listed" : string.Join(", ", capabilities.Industries));
        sb.Append("- Certifications: ").AppendLine(capabilities.Certifications.Count == 0 ? "none listed" : string.Join(", ", capabilities.Certifications.Select(c => c.Issuer is null ? c.Name : $"{c.Name} ({c.Issuer})")));

        sb.AppendLine("- Reference projects:");
        if (capabilities.ReferenceProjects.Count == 0)
        {
            sb.AppendLine("  none listed");
        }

        foreach (var reference in capabilities.ReferenceProjects.OrderByDescending(r => r.Year ?? 0).Take(settings.MaxReferenceProjects))
        {
            sb.Append("  - ").Append(reference.Client).Append(": ").Append(reference.Scope);
            if (reference.Value is { } value)
            {
                sb.Append(", ").Append(RequirementEvaluators.Rand(value));
            }

            if (reference.Year is { } year)
            {
                sb.Append(", ").Append(year.ToString(CultureInfo.InvariantCulture));
            }

            sb.AppendLine(reference.Contactable ? ", contactable" : string.Empty);
        }

        if (profile.ShareKeyPersonnelWithModel && capabilities.KeyPersonnel.Count > 0)
        {
            sb.AppendLine("- Key personnel (roles only):");
            foreach (var person in capabilities.KeyPersonnel)
            {
                sb.Append("  - ").Append(person.Role);
                if (person.YearsExperience is { } years)
                {
                    sb.Append(", ").Append(years.ToString(CultureInfo.InvariantCulture)).Append(" years");
                }

                if (!string.IsNullOrWhiteSpace(person.Summary))
                {
                    sb.Append(": ").Append(person.Summary);
                }

                sb.AppendLine();
            }
        }
        else
        {
            sb.AppendLine("- Key personnel: not shared");
        }

        return sb.ToString();
    }

    private static IEnumerable<HumanQuestion> QuestionsFrom(IReadOnlyList<QualificationFinding> findings, IReadOnlyList<DocumentCheck> checks, Tender tender, DateTimeOffset now)
    {
        foreach (var finding in findings)
        {
            switch (finding.Outcome)
            {
                case FindingOutcome.Unknown:
                    yield return new HumanQuestion($"Does the company meet: {finding.Requirement}?", finding.Detail, finding.RequirementId);
                    break;
                case FindingOutcome.Expired:
                    yield return new HumanQuestion($"Can this be renewed before the bid closes: {finding.Requirement}?", finding.Detail, finding.RequirementId);
                    break;
            }
        }

        foreach (var check in checks.Where(c => c.Outcome == DocumentOutcome.Expired))
        {
            yield return new HumanQuestion($"Renew and upload: {check.Name}?", check.Detail, check.RequirementId);
        }

        var unmapped = checks.Where(c => c.Outcome == DocumentOutcome.Unmapped).ToList();
        if (unmapped.Count > 0)
        {
            yield return new HumanQuestion(
                $"Check the library for {unmapped.Count} document(s) the rules could not place: {string.Join("; ", unmapped.Select(c => c.Name))}.",
                "Not matched to a company document type.", unmapped[0].RequirementId);
        }

        if (tender.BriefingAt is { } briefing && briefing > now)
        {
            var when = SouthAfricaTime.ToLocal(briefing).ToString("ddd d MMM yyyy HH:mm", CultureInfo.InvariantCulture);
            yield return new HumanQuestion(
                tender.BriefingCompulsory == true ? $"Who attends the compulsory briefing on {when} SAST?" : $"Will someone attend the briefing on {when} SAST?",
                tender.BriefingCompulsory == true ? "Attendance is a condition of bidding." : "Attendance is optional but informative.", null);
        }
    }

    private static (string Name, string? FormCode) DocumentNameOf(TenderRequirement row)
    {
        if (!string.IsNullOrWhiteSpace(row.ValueJson))
        {
            try
            {
                using var document = JsonDocument.Parse(row.ValueJson);
                var root = document.RootElement;
                var name = root.TryGetProperty("name", out var n) && n.ValueKind == JsonValueKind.String ? n.GetString() : null;
                var code = root.TryGetProperty("formCode", out var c) && c.ValueKind == JsonValueKind.String ? c.GetString() : null;
                if (!string.IsNullOrWhiteSpace(name))
                {
                    return (name, string.IsNullOrWhiteSpace(code) ? null : code);
                }
            }
            catch (JsonException)
            {
                // fall through to the text
            }
        }

        return (row.Text, null);
    }

    private static string? ScoringNotesOf(TenderRequirement row)
    {
        if (string.IsNullOrWhiteSpace(row.ValueJson))
        {
            return null;
        }

        try
        {
            using var document = JsonDocument.Parse(row.ValueJson);
            return document.RootElement.TryGetProperty("scoringNotes", out var notes) && notes.ValueKind == JsonValueKind.String ? notes.GetString() : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }
}
