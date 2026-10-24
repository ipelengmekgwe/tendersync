using System.Globalization;
using System.Text;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using TenderSync.Application.Abstractions;
using TenderSync.Application.Abstractions.Llm;
using TenderSync.Application.Abstractions.Persistence;
using TenderSync.Application.Abstractions.Storage;
using TenderSync.Application.Common.Exceptions;
using TenderSync.Domain.Common;
using TenderSync.Domain.Enums;
using TenderSync.Domain.Tenders;

namespace TenderSync.Application.Features.Requirements;

public sealed record ExtractionOutcome(
    Guid TenderId,
    string Ocid,
    int DocumentsProcessed,
    int DocumentsSkipped,
    int Requirements,
    int NeedsVerification,
    decimal CostUsd,
    string? Model,
    IReadOnlyList<string> Errors);

/// <summary>
/// PLAN.md §7.4: each usable document goes to the model (native PDF with citations when small enough, otherwise extracted
/// text with [page N] markers), the per-document results are merged with the main bid document winning conflicts, and
/// the result replaces tender_requirements and updates the tender's summary, briefing and submission columns.
/// Items below 0.5 confidence or without a page are flagged for verification by the entity.
/// </summary>
public sealed class RequirementExtractor(
    IAppDbContext db,
    IDocumentStore store,
    ILlmClient llm,
    IPromptRegistry prompts,
    IOptions<ExtractionOptions> options,
    IDateTimeProvider clock,
    ILogger<RequirementExtractor> logger)
{
    public const string Purpose = "extract-requirements";

    public async Task<ExtractionOutcome> ExtractAsync(Guid tenderId, CancellationToken cancellationToken = default)
    {
        var tender = await db.Tenders.GetWithDetailsAsync(tenderId, cancellationToken)
            ?? throw new NotFoundException(nameof(Tender), tenderId);
        var settings = options.Value;
        var prompt = prompts.Get(Purpose);
        var now = clock.UtcNow;

        var ordered = OrderDocuments(tender, settings);
        var extractions = new List<DocumentExtraction>();
        var errors = new List<string>();
        var cost = 0m;
        string? model = null;
        var skipped = tender.Documents.Count - ordered.Count;

        foreach (var document in ordered)
        {
            try
            {
                var part = await BuildPartAsync(document, settings, cancellationToken);
                if (part is null)
                {
                    skipped++;
                    continue;
                }

                var request = new LlmRequest(
                    Purpose, prompt.VersionTag, prompt.Body,
                    [part, new LlmTextPart(UserText(tender, document))],
                    settings.Tier, settings.Effort, settings.MaxOutputTokens, EnableCitations: true, TenderId: tender.Id);

                var result = await llm.CompleteAsync<TenderRequirementsV1>(request, cancellationToken);
                cost += result.CostUsd;
                model ??= result.Model;
                extractions.Add(new DocumentExtraction(document.Id, document.Title, result.Value, result.Citations));
            }
            catch (LlmException ex)
            {
                errors.Add($"{document.Title}: {ex.Message}");
                logger.LogError(ex, "Requirement extraction failed for {Ocid} document {Title}", tender.Ocid, document.Title);
            }
        }

        if (extractions.Count == 0)
        {
            throw new LlmException(errors.Count > 0
                ? $"No document of {tender.Ocid} could be extracted: {string.Join("; ", errors)}"
                : $"Tender {tender.Ocid} has no document with extracted text; run the document pipeline first.");
        }

        var merged = RequirementsMerger.Merge(extractions);
        var rows = ToRequirements(tender.Id, merged, model ?? "unknown", prompt.VersionTag, now);
        await db.Tenders.ReplaceRequirementsAsync(tender.Id, rows, cancellationToken);
        ApplyToTender(tender, merged);
        tender.MarkRequirementsExtracted(rows.Count, now);
        await db.SaveChangesAsync(cancellationToken);

        if (cost > settings.WarnCostPerTenderUsd)
        {
            logger.LogWarning("Requirement extraction for {Ocid} cost ${Cost} over {Documents} document(s)", tender.Ocid, cost, extractions.Count);
        }

        var verify = rows.Count(r => r.NeedsVerification);
        logger.LogInformation("Extracted {Count} requirement(s) for {Ocid} from {Documents} document(s), {Verify} to verify, ${Cost}", rows.Count, tender.Ocid, extractions.Count, verify, cost);
        return new ExtractionOutcome(tender.Id, tender.Ocid, extractions.Count, skipped, rows.Count, verify, cost, model, errors);
    }

    /// <summary>Documents with text, main bid document first, then by page count; capped by MaxDocumentsPerTender.</summary>
    public static IReadOnlyList<TenderDocument> OrderDocuments(Tender tender, ExtractionOptions settings)
    {
        ArgumentNullException.ThrowIfNull(tender);
        ArgumentNullException.ThrowIfNull(settings);
        var usable = tender.Documents.Where(d => d.Status == DocumentStatus.Extracted && d.StoragePath is not null && (d.PageCount ?? 0) > 0).ToList();
        var main = usable
            .Where(d => settings.MainDocumentWords.Any(w => d.Title.Contains(w, StringComparison.OrdinalIgnoreCase)))
            .OrderByDescending(d => d.PageCount)
            .FirstOrDefault() ?? usable.OrderByDescending(d => d.PageCount).FirstOrDefault();

        if (main is null)
        {
            return [];
        }

        return new[] { main }.Concat(usable.Where(d => d.Id != main.Id).OrderByDescending(d => d.PageCount).ThenBy(d => d.Title, StringComparer.Ordinal)).Take(settings.MaxDocumentsPerTender).ToList();
    }

    private async Task<LlmPart?> BuildPartAsync(TenderDocument document, ExtractionOptions settings, CancellationToken cancellationToken)
    {
        var pages = await db.Tenders.GetDocumentTextsAsync(document.Id, cancellationToken);
        var text = new StringBuilder();
        foreach (var page in pages)
        {
            if (page.CharCount == 0)
            {
                continue;
            }

            text.Append("[page ").Append(page.Page).Append("]\n").Append(page.Text).Append("\n\n");
            if (text.Length > settings.MaxTextCharsPerDocument)
            {
                text.Length = settings.MaxTextCharsPerDocument;
                text.Append("\n[truncated]");
                break;
            }
        }

        var extracted = text.ToString();
        var isPdf = string.Equals(Path.GetExtension(document.StoragePath), ".pdf", StringComparison.OrdinalIgnoreCase);
        var native = settings.PreferNativePdf && isPdf && !document.NeedsOcr
            && (document.SizeBytes ?? long.MaxValue) <= settings.MaxNativePdfBytes
            && (document.PageCount ?? int.MaxValue) <= settings.MaxNativePdfPages;

        if (native)
        {
            await using var stream = await store.OpenReadAsync(document.StoragePath!, cancellationToken);
            using var buffer = new MemoryStream();
            await stream.CopyToAsync(buffer, cancellationToken);
            return new LlmDocumentPart(buffer.ToArray(), "application/pdf", document.Title, extracted.Length > 0 ? extracted : null);
        }

        if (extracted.Trim().Length == 0)
        {
            return null;
        }

        return new LlmDocumentPart(Encoding.UTF8.GetBytes(extracted), "text/plain", document.Title, extracted);
    }

    private static string UserText(Tender tender, TenderDocument document)
    {
        var sb = new StringBuilder();
        sb.Append("Tender ").Append(tender.BidNumber).Append(": ").AppendLine(tender.Title);
        sb.Append("Buyer: ").AppendLine(tender.Buyer?.Name ?? "unknown");
        if (tender.ClosingAt is { } closing)
        {
            sb.Append("Closing date per the eTenders feed: ").AppendLine(closing.ToString("yyyy-MM-dd'T'HH:mm:ssK", CultureInfo.InvariantCulture));
        }

        sb.Append("Document: ").Append(document.Title);
        if (document.PageCount is { } pages)
        {
            sb.Append(" (").Append(pages).Append(" page").Append(pages == 1 ? string.Empty : "s").Append(')');
        }

        sb.AppendLine();
        sb.Append("Extract the requirements from this document only.");
        return sb.ToString();
    }

    /// <summary>Flattens the merged sections into tender_requirements rows.</summary>
    public static List<TenderRequirement> ToRequirements(Guid tenderId, MergedRequirements merged, string model, string promptVersion, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(merged);
        var rows = new List<TenderRequirement>();

        foreach (var item in merged.Mandatory)
        {
            var m = item.Value;
            rows.Add(TenderRequirement.Create(tenderId, RequirementKind.Mandatory, m.Type, m.Text, true, Clamp(m.Confidence), Source(item.DocumentId, m.Source), model, promptVersion, now,
                valueJson: m.Value is null ? null : DomainJson.Serialize(new { value = m.Value })));
        }

        foreach (var item in merged.Functional)
        {
            var f = item.Value;
            rows.Add(TenderRequirement.Create(tenderId, RequirementKind.Functional, RequirementType.Other, f.Criterion, false, Clamp(f.Confidence), Source(item.DocumentId, f.Source), model, promptVersion, now,
                valueJson: f.ScoringNotes is null ? null : DomainJson.Serialize(new { scoringNotes = f.ScoringNotes }), weight: (decimal?)f.Weight, threshold: (decimal?)f.Threshold));
        }

        if (merged.FunctionalityThreshold is not null || merged.EvaluationMethod is not null)
        {
            var text = string.Join("; ", new[]
            {
                merged.FunctionalityThreshold is null ? null : string.Create(CultureInfo.InvariantCulture, $"Functionality threshold {merged.FunctionalityThreshold.Value}/100"),
                merged.EvaluationMethod is null ? null : $"Evaluation method {merged.EvaluationMethod.Value}",
            }.Where(s => s is not null));
            var docId = merged.FunctionalityThreshold?.DocumentId ?? merged.EvaluationMethod!.DocumentId;
            rows.Add(TenderRequirement.Create(tenderId, RequirementKind.Evaluation, RequirementType.Other, text, true, 0.8m, new RequirementSource(docId, null, null), model, promptVersion, now,
                valueJson: DomainJson.Serialize(new { functionalityThreshold = merged.FunctionalityThreshold?.Value, evaluationMethod = merged.EvaluationMethod?.Value }),
                threshold: (decimal?)merged.FunctionalityThreshold?.Value));
        }

        foreach (var item in merged.RequiredDocuments)
        {
            var d = item.Value;
            var text = string.IsNullOrWhiteSpace(d.FormCode) ? d.Name : $"{d.FormCode}: {d.Name}";
            rows.Add(TenderRequirement.Create(tenderId, RequirementKind.Document, RequirementType.Other, text, d.Mandatory, Clamp(d.Confidence), Source(item.DocumentId, d.Source), model, promptVersion, now,
                valueJson: DomainJson.Serialize(new { d.Name, d.FormCode, d.Notes })));
        }

        if (merged.Briefing is { } briefing)
        {
            var b = briefing.Value;
            var text = $"{(b.Compulsory == true ? "Compulsory" : b.Compulsory == false ? "Non-compulsory" : "Briefing")} briefing{(b.DateIso is null ? string.Empty : " on " + b.DateIso)}{(b.Venue is null ? string.Empty : " at " + b.Venue)}";
            rows.Add(TenderRequirement.Create(tenderId, RequirementKind.Briefing, RequirementType.Other, text, b.Compulsory == true, Clamp(b.Confidence), Source(briefing.DocumentId, b.Source), model, promptVersion, now,
                valueJson: DomainJson.Serialize(new { date = b.DateIso, b.Compulsory, b.Venue, b.VirtualLink })));
        }

        if (merged.Submission is { } submission)
        {
            var s = submission.Value;
            var text = $"Submit via {s.Method}{(s.Address is null ? string.Empty : ": " + s.Address)}{(s.ClosingAtIso is null ? string.Empty : ", closing " + s.ClosingAtIso)}";
            rows.Add(TenderRequirement.Create(tenderId, RequirementKind.Submission, RequirementType.Other, text, true, Clamp(s.Confidence), Source(submission.DocumentId, s.Source), model, promptVersion, now,
                valueJson: DomainJson.Serialize(new { method = s.Method.ToString(), s.Address, closingAt = s.ClosingAtIso, s.FormatRules })));
        }

        if (merged.Pricing is { } pricing)
        {
            var p = pricing.Value;
            var text = $"Pricing basis {p.Basis}{(p.VatRule is null ? string.Empty : ", " + p.VatRule)}{(p.ValidityDays is null ? string.Empty : $", valid {p.ValidityDays} days")}";
            rows.Add(TenderRequirement.Create(tenderId, RequirementKind.Pricing, RequirementType.Other, text, false, Clamp(p.Confidence), Source(pricing.DocumentId, p.Source), model, promptVersion, now,
                valueJson: DomainJson.Serialize(new { basis = p.Basis.ToString(), p.Currency, p.VatRule, p.ValidityDays, p.ScheduleItems })));
        }

        if (merged.Contract is { } contract)
        {
            var c = contract.Value;
            var text = $"Contract{(c.DurationMonths is null ? string.Empty : $" of {c.DurationMonths} months")}{(c.Start is null ? string.Empty : ", starting " + c.Start)}{(c.ExtensionOptions is null ? string.Empty : ", " + c.ExtensionOptions)}";
            rows.Add(TenderRequirement.Create(tenderId, RequirementKind.Contract, RequirementType.Other, text, false, Clamp(c.Confidence), Source(contract.DocumentId, c.Source), model, promptVersion, now,
                valueJson: DomainJson.Serialize(new { c.DurationMonths, c.Start, c.ExtensionOptions })));
        }

        if (merged.Contacts is { } contacts)
        {
            // POPIA: stored for enquiries, never sent through notification channels (PLAN.md §8.4).
            rows.Add(TenderRequirement.Create(tenderId, RequirementKind.Other, RequirementType.Other, "Enquiries contact (internal use only)", false, 0.9m, new RequirementSource(contacts.DocumentId, null, null), model, promptVersion, now,
                valueJson: DomainJson.Serialize(new { email = contacts.Value.EnquiriesEmail, phone = contacts.Value.EnquiriesPhone })));
        }

        return rows;
    }

    private static void ApplyToTender(Tender tender, MergedRequirements merged)
    {
        tender.SetSummary(merged.Summary);
        if (merged.Briefing is { } briefing)
        {
            tender.SetBriefing(ParseDate(briefing.Value.DateIso), briefing.Value.Compulsory, briefing.Value.Venue);
        }

        if (merged.Submission is { } submission && submission.Value.Method != SubmissionMethod.Unknown)
        {
            tender.SetSubmissionMethod(submission.Value.Method);
        }
    }

    /// <summary>Parses the model's ISO date (written with +02:00) and normalises it to UTC, the only offset timestamptz columns accept.</summary>
    public static DateTimeOffset? ParseDate(string? iso) =>
        iso is not null && DateTimeOffset.TryParse(iso, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var value) ? value.ToUniversalTime() : null;

    private static RequirementSource Source(Guid documentId, RequirementSourceV1? source) =>
        new(documentId, source?.Page is > 0 ? source.Page : null, Truncate(source?.Quote));

    private static string? Truncate(string? quote) => quote is null ? null : quote.Length <= 500 ? quote : quote[..500];

    private static decimal Clamp(double confidence) => (decimal)Math.Clamp(double.IsNaN(confidence) ? 0 : confidence, 0, 1);
}
