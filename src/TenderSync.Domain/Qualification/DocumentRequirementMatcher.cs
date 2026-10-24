using System.Text.RegularExpressions;
using TenderSync.Domain.Companies;
using TenderSync.Domain.Enums;

namespace TenderSync.Domain.Qualification;

/// <summary>How a required document was resolved against the company's document library (PLAN.md §7.5 step 2).</summary>
public enum DocumentOutcome
{
    /// <summary>The requirement could not be mapped to a company document type; a human checks it.</summary>
    Unmapped = 0,
    Present = 1,
    Missing = 2,
    Expired = 3,

    /// <summary>A bid form (SBD, MBD, pricing schedule, proposal) that is completed per bid, never a missing document.</summary>
    ToComplete = 4,
}

public sealed record DocumentCheck(string Name, string? FormCode, DocumentOutcome Outcome, CompanyDocumentType? Type, string Detail, Guid? RequirementId);

/// <summary>Maps required-document wording to <see cref="CompanyDocumentType"/> and checks the library for a current copy.</summary>
public static partial class DocumentRequirementMatcher
{
    private static readonly (Regex Pattern, CompanyDocumentType Type)[] TypeRules =
    [
        (CsdPattern(), CompanyDocumentType.CsdReport),
        (TaxPattern(), CompanyDocumentType.TaxPin),
        (BbbeePattern(), CompanyDocumentType.BbbeeCertificate),
        (RegistrationPattern(), CompanyDocumentType.CompanyRegistration),
        (ProfilePattern(), CompanyDocumentType.CompanyProfile),
        (CvPattern(), CompanyDocumentType.Cv),
        (ReferencePattern(), CompanyDocumentType.Reference),
        (CertificationPattern(), CompanyDocumentType.Certification),
    ];

    /// <summary>Resolves the wording to a type, or null for forms (<paramref name="isForm"/> true) and unmapped items.</summary>
    public static CompanyDocumentType? MapType(string name, string? formCode, out bool isForm)
    {
        var text = $"{formCode} {name}".Trim();
        isForm = FormPattern().IsMatch(text);
        if (isForm)
        {
            return null;
        }

        foreach (var (pattern, type) in TypeRules)
        {
            if (pattern.IsMatch(text))
            {
                return type;
            }
        }

        return null;
    }

    public static DocumentCheck Check(string name, string? formCode, Guid? requirementId, IReadOnlyList<CompanyDocument> documents, DateTimeOffset validityPoint)
    {
        ArgumentNullException.ThrowIfNull(documents);
        var type = MapType(name, formCode, out var isForm);
        if (isForm)
        {
            return new DocumentCheck(name, formCode, DocumentOutcome.ToComplete, null, "Bid form to complete and sign for this submission.", requirementId);
        }

        if (type is null)
        {
            return new DocumentCheck(name, formCode, DocumentOutcome.Unmapped, null, "Not matched to a company document type; check the library by hand.", requirementId);
        }

        var candidates = documents.Where(d => d.Type == type && !d.IsDeleted).ToList();
        if (candidates.Count == 0)
        {
            return new DocumentCheck(name, formCode, DocumentOutcome.Missing, type, $"No {Describe(type.Value)} in the company library.", requirementId);
        }

        var current = candidates.Where(d => !d.IsExpired(validityPoint)).OrderByDescending(d => d.ExpiresAt ?? DateTimeOffset.MaxValue).FirstOrDefault();
        if (current is not null)
        {
            return new DocumentCheck(name, formCode, DocumentOutcome.Present, type, $"{current.Title}{(current.ExpiresAt is { } e ? $" (valid to {RequirementEvaluators.Date(e)})" : string.Empty)}", requirementId);
        }

        var latest = candidates.OrderByDescending(d => d.ExpiresAt).First();
        return new DocumentCheck(name, formCode, DocumentOutcome.Expired, type, $"{latest.Title} expired {RequirementEvaluators.Date(latest.ExpiresAt!.Value)}; renew before the bid closes.", requirementId);
    }

    public static string Describe(CompanyDocumentType type) => type switch
    {
        CompanyDocumentType.CsdReport => "CSD registration report",
        CompanyDocumentType.TaxPin => "tax compliance PIN letter",
        CompanyDocumentType.BbbeeCertificate => "B-BBEE certificate or sworn affidavit",
        CompanyDocumentType.CompanyRegistration => "company registration document",
        CompanyDocumentType.CompanyProfile => "company profile",
        CompanyDocumentType.Cv => "CV",
        CompanyDocumentType.Reference => "reference letter",
        CompanyDocumentType.Certification => "certification",
        CompanyDocumentType.PricingTemplate => "pricing template",
        CompanyDocumentType.ProposalTemplate => "proposal template",
        CompanyDocumentType.Sbd4 => "SBD 4 declaration",
        CompanyDocumentType.Sbd6Point1 => "SBD 6.1 preference points claim",
        _ => "document",
    };

    [GeneratedRegex(@"\b(?:sbd|mbd|wcbd|sdb)\s*\d|pricing\s+schedule|price\s+schedule|bill\s+of\s+quantit|schedule\s+of\s+(?:rates|prices)|proposal|methodolog|technical\s+approach|declaration\s+of\s+interest|preference\s+points?\s+claim|invitation\s+to\s+bid|bid\s+form|tender\s+form|form\s+of\s+offer", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex FormPattern();

    [GeneratedRegex(@"\bcsd\b|central\s+supplier", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex CsdPattern();

    [GeneratedRegex(@"\btax\b|\bsars\b|\bpin\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex TaxPattern();

    [GeneratedRegex(@"b-?bbee|\bbee\b|broad[- ]based|sworn\s+affidavit|affidavit", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex BbbeePattern();

    [GeneratedRegex(@"\bcipc\b|company\s+registration|registration\s+(?:document|certificate)|certificate\s+of\s+incorporation|\bck\s?1\b|\bcor\s?14|founding\s+statement|\bcm\s?29\b|memorandum\s+of\s+incorporation", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex RegistrationPattern();

    [GeneratedRegex(@"company\s+profile|corporate\s+profile", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex ProfilePattern();

    [GeneratedRegex(@"\bcvs?\b|curriculum|r[eé]sum[eé]", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex CvPattern();

    [GeneratedRegex(@"reference|testimonial|letter\s+of\s+(?:good\s+standing|recommendation)|track\s+record", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex ReferencePattern();

    [GeneratedRegex(@"certif|accredit|\biso\b|partner\s+(?:status|letter)|\boem\b|licen[cs]e", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex CertificationPattern();
}
