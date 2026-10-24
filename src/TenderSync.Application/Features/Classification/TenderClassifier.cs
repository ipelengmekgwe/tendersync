using System.Globalization;
using System.Text;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using TenderSync.Application.Abstractions;
using TenderSync.Application.Abstractions.Llm;
using TenderSync.Application.Abstractions.Persistence;
using TenderSync.Application.Common.Exceptions;
using TenderSync.Domain.Categories;
using TenderSync.Domain.Enums;
using TenderSync.Domain.Tenders;

namespace TenderSync.Application.Features.Classification;

public sealed record ClassificationOutcome(
    Guid TenderId,
    string Ocid,
    ClassificationStage Stage,
    bool Candidate,
    IReadOnlyList<string> CategoryCodes,
    int Relevance,
    bool IsNearMiss,
    int MatchesCreated,
    int MatchesUpdated,
    decimal CostUsd,
    string? Reason,
    IReadOnlyList<string> RedFlags);

/// <summary>
/// PLAN.md §7.2 end to end: keyword prefilter, then the classify prompt for candidates, persisted in
/// tender_classifications; a TenderMatch per active company profile whose capability categories overlap when relevance
/// reaches the threshold (TenderMatched goes through the outbox to notifications and document download). Near misses are
/// stored as classification only. Safe to re-run: the row is updated in place and existing matches keep their workflow state.
/// </summary>
public sealed class TenderClassifier(
    IAppDbContext db,
    KeywordPrefilter prefilter,
    ILlmClient llm,
    IPromptRegistry prompts,
    IOptions<ClassificationOptions> options,
    IDateTimeProvider clock,
    ILogger<TenderClassifier> logger)
{
    public const string Purpose = "classify";

    public async Task<ClassificationOutcome> ClassifyAsync(Guid tenderId, bool prefilterOnly = false, CancellationToken cancellationToken = default)
    {
        var tender = await db.Tenders.GetWithDetailsAsync(tenderId, cancellationToken)
            ?? throw new NotFoundException(nameof(Tender), tenderId);
        var categories = await db.InterestCategories.ListAsync(enabledOnly: true, cancellationToken);
        var settings = options.Value;
        var now = clock.UtcNow;

        var text = TenderText.From(tender, settings.MaxDocumentTitles);
        var stage1 = prefilter.Evaluate(text, categories);

        var classification = await db.Tenders.GetClassificationAsync(tender.Id, cancellationToken);
        if (!stage1.IsCandidate || prefilterOnly)
        {
            var reason = prefilterOnly && stage1.IsCandidate ? $"Stage 2 pending. {stage1.Reason}" : stage1.Reason;
            if (classification is null)
            {
                classification = TenderClassification.Keyword(tender.Id, stage1.CandidateCodes, 0, reason, now);
                db.Tenders.AddClassification(classification);
            }
            else
            {
                classification.ApplyKeywordResult(stage1.CandidateCodes, 0, reason, now);
            }

            await db.SaveChangesAsync(cancellationToken);
            return new ClassificationOutcome(tender.Id, tender.Ocid, ClassificationStage.Keyword, stage1.IsCandidate, stage1.CandidateCodes, 0, false, 0, 0, 0m, reason, []);
        }

        var prompt = prompts.Get(Purpose);
        var request = new LlmRequest(
            Purpose, prompt.VersionTag, prompt.Body,
            [new LlmTextPart(BuildUserText(text, categories, settings))],
            prompt.Tier, LlmEffort.Low, 1024, EnableCitations: false, TenderId: tender.Id);

        var result = await llm.CompleteAsync<ClassificationAnswer>(request, cancellationToken);
        var answer = result.Value;

        var known = categories.ToDictionary(c => c.Code, StringComparer.OrdinalIgnoreCase);
        var codes = answer.Categories
            .Where(c => c.Confidence >= settings.MinCategoryConfidence && known.ContainsKey(c.Code))
            .OrderByDescending(c => c.Confidence)
            .Select(c => known[c.Code].Code)
            .Distinct(StringComparer.Ordinal)
            .ToList();
        var relevance = Math.Clamp(answer.Relevance, 0, 100);
        var rationale = BuildRationale(answer, stage1);

        if (classification is null)
        {
            classification = TenderClassification.Keyword(tender.Id, stage1.CandidateCodes, 0, stage1.Reason, now);
            db.Tenders.AddClassification(classification);
        }

        classification.ApplyLlmResult(codes, relevance, rationale, result.Model, prompt.VersionTag, result.Usage.InputTokens, result.Usage.OutputTokens, now);

        var (created, updated) = await MatchAsync(tender, codes, relevance, settings, now, cancellationToken);
        await db.SaveChangesAsync(cancellationToken);

        var nearMiss = relevance < settings.MatchThreshold && relevance >= settings.NearMissThreshold;
        logger.LogInformation(
            "Classified {Ocid}: relevance {Relevance}, categories {Categories}, matches +{Created}/~{Updated}, ${Cost}",
            tender.Ocid, relevance, string.Join(",", codes), created, updated, result.CostUsd);

        return new ClassificationOutcome(tender.Id, tender.Ocid, ClassificationStage.Llm, true, codes, relevance, nearMiss, created, updated, result.CostUsd, rationale, answer.RedFlags);
    }

    /// <summary>The varying part of the prompt: tender fields and the category list. Stable system text stays in the prompt file (cache prefix).</summary>
    public static string BuildUserText(TenderText text, IReadOnlyList<InterestCategory> categories, ClassificationOptions settings)
    {
        ArgumentNullException.ThrowIfNull(text);
        ArgumentNullException.ThrowIfNull(categories);
        ArgumentNullException.ThrowIfNull(settings);
        var sb = new StringBuilder();
        sb.AppendLine("Interest categories:");
        foreach (var category in categories)
        {
            sb.Append("- ").Append(category.Code).Append(": ").Append(category.Name);
            if (!string.IsNullOrWhiteSpace(category.Description))
            {
                sb.Append(" — ").Append(category.Description);
            }

            sb.AppendLine();
        }

        sb.AppendLine();
        sb.AppendLine("Tender:");
        sb.Append("Bid number: ").AppendLine(text.BidNumber);
        sb.Append("Title: ").AppendLine(text.Title);
        if (!string.IsNullOrWhiteSpace(text.BuyerName))
        {
            sb.Append("Buyer: ").AppendLine(text.BuyerName);
        }

        sb.Append("Procurement category: ").AppendLine(text.MainCategory == ProcurementCategory.Unknown ? "not specified" : text.MainCategory.ToString().ToLowerInvariant());
        sb.Append("Procurement method: ").AppendLine(string.IsNullOrWhiteSpace(text.ProcurementMethodDetails) ? "not specified" : text.ProcurementMethodDetails);
        var description = text.Description ?? string.Empty;
        sb.Append("Description: ").AppendLine(description.Length <= settings.MaxDescriptionChars ? description : description[..settings.MaxDescriptionChars] + " […]");
        sb.AppendLine("Bid documents:");
        if (text.DocumentTitles.Count == 0)
        {
            sb.AppendLine("- (none listed)");
        }

        foreach (var title in text.DocumentTitles.Take(settings.MaxDocumentTitles))
        {
            sb.Append("- ").AppendLine(title);
        }

        return sb.ToString();
    }

    private async Task<(int Created, int Updated)> MatchAsync(Tender tender, IReadOnlyList<string> codes, int relevance, ClassificationOptions settings, DateTimeOffset now, CancellationToken cancellationToken)
    {
        if (relevance < settings.MatchThreshold)
        {
            return (0, 0);
        }

        int created = 0, updated = 0;
        foreach (var profile in await db.CompanyProfiles.ListActiveAsync(cancellationToken))
        {
            var wanted = profile.Capabilities.CategoryCodes;
            var overlaps = wanted.Count == 0 || wanted.Any(w => codes.Contains(w, StringComparer.OrdinalIgnoreCase));
            if (!overlaps)
            {
                continue;
            }

            var existing = await db.TenderMatches.FindAsync(tender.Id, profile.Id, cancellationToken);
            if (existing is null)
            {
                db.TenderMatches.Add(TenderMatch.Create(tender.Id, profile.Id, relevance, now));
                created++;
            }
            else if (existing.Relevance != relevance)
            {
                existing.UpdateRelevance(relevance);
                updated++;
            }
        }

        return (created, updated);
    }

    private static string BuildRationale(ClassificationAnswer answer, PrefilterResult stage1)
    {
        var sb = new StringBuilder(answer.Rationale?.Trim() ?? string.Empty);
        if (answer.RedFlags.Count > 0)
        {
            sb.Append(sb.Length > 0 ? " " : string.Empty).Append("Red flags: ").Append(string.Join("; ", answer.RedFlags)).Append('.');
        }

        if (!answer.IsItServices)
        {
            sb.Append(" Not primarily IT services.");
        }

        sb.Append(CultureInfo.InvariantCulture, $" [stage 1: {stage1.Reason}]");
        var text = sb.ToString().Trim();
        return text.Length <= 4000 ? text : text[..4000];
    }
}
