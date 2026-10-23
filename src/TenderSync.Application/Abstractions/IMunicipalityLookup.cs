using TenderSync.Domain.Enums;

namespace TenderSync.Application.Abstractions;

/// <summary>Cached municipality → province lookup used by the province resolver (PLAN.md Appendix C).</summary>
public interface IMunicipalityLookup
{
    /// <summary>Resolves a normalised municipality name (see ProvinceResolver.NormaliseName) to its province.</summary>
    Province? TryResolve(string normalisedName);
}
