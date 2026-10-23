using TenderSync.Domain.Common;
using TenderSync.Domain.Enums;

namespace TenderSync.Domain.Geography;

/// <summary>Lookup row for resolving a municipal buyer to its province (PLAN.md Appendix C). Seeded from the MDB list.</summary>
public sealed class Municipality : Entity
{
    private Municipality()
    {
    }

    public string Code { get; private set; } = null!;

    public string Name { get; private set; } = null!;

    /// <summary>Lower-case name without the municipality suffix, diacritics or punctuation. See <see cref="ProvinceResolver.NormaliseName"/>.</summary>
    public string NormalisedName { get; private set; } = null!;

    public MunicipalityType Type { get; private set; }

    public Province Province { get; private set; }

    public string? DistrictName { get; private set; }

    public static Municipality Create(string code, string name, MunicipalityType type, Province province, string? districtName)
    {
        name = Guard.NotEmpty(name, nameof(name));
        return new Municipality
        {
            Code = Guard.NotEmpty(code, nameof(code)).ToUpperInvariant(),
            Name = name,
            NormalisedName = ProvinceResolver.NormaliseName(name),
            Type = type,
            Province = province,
            DistrictName = Guard.Trimmed(districtName),
        };
    }
}

public enum MunicipalityType
{
    Local = 0,
    District = 1,
    Metropolitan = 2,
}
