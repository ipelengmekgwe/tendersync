using System.Globalization;
using TenderSync.Domain.Companies;
using TenderSync.Domain.Enums;

namespace TenderSync.Domain.Qualification;

/// <summary>The evaluator per requirement type (PLAN.md §7.5 step 1). Types without a profile field answer Unknown.</summary>
public static class RequirementEvaluators
{
    public static IReadOnlyList<IRequirementEvaluator> All { get; } =
    [
        new CsdEvaluator(),
        new TaxComplianceEvaluator(),
        new BbbeeEvaluator(),
        new CidbEvaluator(),
        new CompanyRegistrationEvaluator(),
        new YearsInBusinessEvaluator(),
        new MinimumTurnoverEvaluator(),
        new ReferencesEvaluator(),
        new CertificationEvaluator(),
        new ProfileSilentEvaluator(RequirementType.Insurance, "insurance cover"),
        new ProfileSilentEvaluator(RequirementType.Accreditation, "accreditations"),
        new ProfileSilentEvaluator(RequirementType.LocalContent, "local content"),
        new ProfileSilentEvaluator(RequirementType.JvSubcontractingRule, "joint-venture or subcontracting arrangements"),
        new ProfileSilentEvaluator(RequirementType.Other, "this kind of requirement"),
    ];

    public static IRequirementEvaluator For(RequirementType type) => All.FirstOrDefault(e => e.Type == type) ?? All[^1];

    public static RequirementEvaluation Evaluate(EvaluationContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        return For(context.Requirement.Type).Evaluate(context);
    }

    public static string Date(DateTimeOffset value) => value.ToString("d MMM yyyy", CultureInfo.InvariantCulture);

    public static string Rand(decimal value) => value >= 1_000_000m
        ? string.Create(CultureInfo.InvariantCulture, $"R{value / 1_000_000m:0.##} million")
        : string.Create(CultureInfo.InvariantCulture, $"R{value:N0}");
}

/// <summary>CSD registration: number present and not expired at closing → Pass; missing → Fail (CSD report missing).</summary>
public sealed class CsdEvaluator : IRequirementEvaluator
{
    public RequirementType Type => RequirementType.CsdRegistration;

    public RequirementEvaluation Evaluate(EvaluationContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        var profile = context.Profile;
        if (profile.CsdNumber is null)
        {
            return RequirementEvaluation.Fail("The profile has no CSD supplier number.", CompanyDocumentType.CsdReport);
        }

        if (profile.CsdExpiry is { } expiry && expiry <= context.ValidityPoint)
        {
            return RequirementEvaluation.Expired($"CSD registration {profile.CsdNumber} expires {RequirementEvaluators.Date(expiry)}, before the bid closes.", CompanyDocumentType.CsdReport);
        }

        return RequirementEvaluation.Pass($"CSD supplier number {profile.CsdNumber}{(profile.CsdExpiry is { } e ? $", valid to {RequirementEvaluators.Date(e)}" : string.Empty)}.");
    }
}

/// <summary>Tax compliance: PIN present and unexpired → Pass.</summary>
public sealed class TaxComplianceEvaluator : IRequirementEvaluator
{
    public RequirementType Type => RequirementType.TaxCompliance;

    public RequirementEvaluation Evaluate(EvaluationContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        var profile = context.Profile;
        if (profile.TaxPin is null)
        {
            return RequirementEvaluation.Fail("The profile has no tax compliance PIN.", CompanyDocumentType.TaxPin);
        }

        if (profile.TaxPinExpiry is { } expiry && expiry <= context.ValidityPoint)
        {
            return RequirementEvaluation.Expired($"The tax compliance PIN expires {RequirementEvaluators.Date(expiry)}, before the bid closes.", CompanyDocumentType.TaxPin);
        }

        return RequirementEvaluation.Pass($"Tax compliance PIN on file{(profile.TaxPinExpiry is { } e ? $", valid to {RequirementEvaluators.Date(e)}" : string.Empty)}.");
    }
}

/// <summary>B-BBEE: certified at the required level or better and unexpired → Pass; worse → Fail; none → Fail.</summary>
public sealed class BbbeeEvaluator : IRequirementEvaluator
{
    public RequirementType Type => RequirementType.BbbeeCertificate;

    public RequirementEvaluation Evaluate(EvaluationContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        var profile = context.Profile;
        if (profile.BbbeeLevel is not { } level)
        {
            return RequirementEvaluation.Fail("The profile has no B-BBEE certificate or sworn affidavit.", CompanyDocumentType.BbbeeCertificate);
        }

        if (profile.BbbeeExpiry is { } expiry && expiry <= context.ValidityPoint)
        {
            return RequirementEvaluation.Expired($"The B-BBEE level {level} certificate expires {RequirementEvaluators.Date(expiry)}, before the bid closes.", CompanyDocumentType.BbbeeCertificate);
        }

        var required = RequirementValueParser.BbbeeLevel(RequirementValueParser.TextToParse(context.Requirement));
        if (required is null)
        {
            return RequirementEvaluation.Unknown($"The profile is B-BBEE level {level}; the required level could not be read from the requirement.");
        }

        return level <= required
            ? RequirementEvaluation.Pass($"B-BBEE level {level} meets the required level {required}.")
            : RequirementEvaluation.Fail($"B-BBEE level {level} is worse than the required level {required}.");
    }
}

/// <summary>CIDB: registered at the required grade or higher in the required class → Pass; lower or wrong class → Fail; unregistered → Fail.</summary>
public sealed class CidbEvaluator : IRequirementEvaluator
{
    public RequirementType Type => RequirementType.CidbGrade;

    public RequirementEvaluation Evaluate(EvaluationContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        var profile = context.Profile;
        if (profile.CidbGrade is not { } grade)
        {
            return RequirementEvaluation.Fail("The company is not CIDB registered.");
        }

        var required = RequirementValueParser.CidbGrade(RequirementValueParser.TextToParse(context.Requirement));
        if (required is null)
        {
            return RequirementEvaluation.Unknown($"CIDB grade {grade} {string.Join("/", profile.CidbClasses)}; the required grading could not be read from the requirement.");
        }

        var (requiredGrade, requiredClass) = required.Value;
        if (grade < requiredGrade)
        {
            return RequirementEvaluation.Fail($"CIDB grade {grade} is below the required grade {requiredGrade}{requiredClass}.");
        }

        if (requiredClass is not null && !profile.CidbClasses.Contains(requiredClass, StringComparer.OrdinalIgnoreCase))
        {
            return RequirementEvaluation.Fail($"CIDB class {requiredClass} is required; the company holds {(profile.CidbClasses.Length == 0 ? "no class" : string.Join("/", profile.CidbClasses))}.");
        }

        return RequirementEvaluation.Pass($"CIDB grade {grade}{requiredClass} meets the required {requiredGrade}{requiredClass}.");
    }
}

/// <summary>Company registration (CIPC): a registration number on the profile → Pass.</summary>
public sealed class CompanyRegistrationEvaluator : IRequirementEvaluator
{
    public RequirementType Type => RequirementType.CompanyRegistration;

    public RequirementEvaluation Evaluate(EvaluationContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        return context.Profile.RegistrationNumber is { } number
            ? RequirementEvaluation.Pass($"Registration number {number}.")
            : RequirementEvaluation.Fail("The profile has no company registration number.", CompanyDocumentType.CompanyRegistration);
    }
}

/// <summary>Years in business: compared with YearsTrading when the profile states it and the requirement is readable.</summary>
public sealed class YearsInBusinessEvaluator : IRequirementEvaluator
{
    public RequirementType Type => RequirementType.YearsInBusiness;

    public RequirementEvaluation Evaluate(EvaluationContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        var trading = context.Profile.YearsTrading;
        if (trading <= 0)
        {
            return RequirementEvaluation.Unknown("The profile does not state how many years the company has traded.");
        }

        var required = RequirementValueParser.Years(RequirementValueParser.TextToParse(context.Requirement));
        if (required is null)
        {
            return RequirementEvaluation.Unknown($"The company has traded {trading} years; the required period could not be read from the requirement.");
        }

        return trading >= required
            ? RequirementEvaluation.Pass($"{trading} years trading meets the required {required}.")
            : RequirementEvaluation.Fail($"{trading} years trading is below the required {required}.");
    }
}

/// <summary>Minimum annual turnover against the profile's AnnualTurnover.</summary>
public sealed class MinimumTurnoverEvaluator : IRequirementEvaluator
{
    public RequirementType Type => RequirementType.MinimumTurnover;

    public RequirementEvaluation Evaluate(EvaluationContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        if (context.Profile.AnnualTurnover is not { } turnover)
        {
            return RequirementEvaluation.Unknown("The profile does not state the annual turnover.");
        }

        var required = RequirementValueParser.Rand(RequirementValueParser.TextToParse(context.Requirement));
        if (required is null)
        {
            return RequirementEvaluation.Unknown($"Annual turnover {RequirementEvaluators.Rand(turnover)}; the required minimum could not be read from the requirement.");
        }

        return turnover >= required
            ? RequirementEvaluation.Pass($"Annual turnover {RequirementEvaluators.Rand(turnover)} meets the required {RequirementEvaluators.Rand(required.Value)}.")
            : RequirementEvaluation.Fail($"Annual turnover {RequirementEvaluators.Rand(turnover)} is below the required {RequirementEvaluators.Rand(required.Value)}.");
    }
}

/// <summary>Reference projects: counts the profile's references that meet the stated value and recency.</summary>
public sealed class ReferencesEvaluator : IRequirementEvaluator
{
    public RequirementType Type => RequirementType.References;

    public RequirementEvaluation Evaluate(EvaluationContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        var references = context.Profile.Capabilities.ReferenceProjects;
        if (references.Count == 0)
        {
            return RequirementEvaluation.Unknown("The profile lists no reference projects.");
        }

        var spec = RequirementValueParser.References(RequirementValueParser.TextToParse(context.Requirement));
        var earliestYear = spec.WithinYears is { } years ? context.Now.Year - years : (int?)null;
        var qualifying = references.Count(r =>
            (spec.MinValue is not { } min || (r.Value is { } value && value >= min))
            && (earliestYear is not { } from || (r.Year is { } year && year >= from)));
        var required = spec.Count ?? 1;
        var rule = $"{required} reference(s){(spec.MinValue is { } v ? $" of at least {RequirementEvaluators.Rand(v)}" : string.Empty)}{(spec.WithinYears is { } w ? $" in the last {w} years" : string.Empty)}";

        return qualifying >= required
            ? RequirementEvaluation.Pass($"{qualifying} of {references.Count} reference project(s) on the profile meet the rule ({rule}).", 0.8m)
            : RequirementEvaluation.Fail($"Only {qualifying} of {references.Count} reference project(s) on the profile meet the rule ({rule}).", confidence: 0.8m);
    }
}

/// <summary>
/// Named certification: a profile certification whose name appears in the requirement → Pass (Expired when it has lapsed);
/// otherwise Unknown, flagged for the model's fuzzy match when the profile has certifications to compare.
/// </summary>
public sealed class CertificationEvaluator : IRequirementEvaluator
{
    public RequirementType Type => RequirementType.Certification;

    public RequirementEvaluation Evaluate(EvaluationContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        var certifications = context.Profile.Capabilities.Certifications;
        if (certifications.Count == 0)
        {
            return RequirementEvaluation.Unknown("The profile lists no certifications.");
        }

        var haystack = Normalise(RequirementValueParser.TextToParse(context.Requirement));
        var matched = certifications.Where(c => c.Name.Length >= 3 && haystack.Contains(Normalise(c.Name), StringComparison.Ordinal)).ToList();
        if (matched.Count == 0)
        {
            return RequirementEvaluation.Unknown("No profile certification is named in the requirement.", needsFuzzyMatch: true);
        }

        var valid = matched.Where(c => c.ExpiresAt is null || c.ExpiresAt > context.ValidityPoint).ToList();
        if (valid.Count > 0)
        {
            return RequirementEvaluation.Pass($"Profile certification {valid[0].Name}{(valid[0].Issuer is { } issuer ? $" ({issuer})" : string.Empty)} matches.", 0.85m);
        }

        return RequirementEvaluation.Expired($"Profile certification {matched[0].Name} expires {RequirementEvaluators.Date(matched[0].ExpiresAt!.Value)}, before the bid closes.", CompanyDocumentType.Certification);
    }

    public static string Normalise(string text) =>
        string.Join(' ', (text ?? string.Empty).ToLowerInvariant().Split([' ', '-', '_', '/', ',', '.', ':', ';', '(', ')', '\n', '\r', '\t'], StringSplitOptions.RemoveEmptyEntries));
}

/// <summary>Types the profile cannot answer (insurance, accreditation, local content, JV rules, other): always a question for a human.</summary>
public sealed class ProfileSilentEvaluator(RequirementType type, string subject) : IRequirementEvaluator
{
    public RequirementType Type => type;

    public RequirementEvaluation Evaluate(EvaluationContext context) =>
        RequirementEvaluation.Unknown($"The profile does not record {subject}; confirm by hand.");
}
