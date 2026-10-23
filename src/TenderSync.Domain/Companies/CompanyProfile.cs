using TenderSync.Domain.Common;
using TenderSync.Domain.Enums;

namespace TenderSync.Domain.Companies;

/// <summary>
/// The bidding company: registrations, compliance status and capabilities the qualification engine compares against.
/// Plural by design (ADR-016) although v1 runs with one profile.
/// </summary>
public sealed class CompanyProfile : Entity
{
    private CompanyProfile()
    {
    }

    public string Name { get; private set; } = null!;

    public string? TradingName { get; private set; }

    public string? RegistrationNumber { get; private set; }

    public string? VatNumber { get; private set; }

    public string? CsdNumber { get; private set; }

    public DateTimeOffset? CsdExpiry { get; private set; }

    public string? TaxPin { get; private set; }

    public DateTimeOffset? TaxPinExpiry { get; private set; }

    /// <summary>B-BBEE level 1 (best) to 8, or null when not certified.</summary>
    public int? BbbeeLevel { get; private set; }

    public DateTimeOffset? BbbeeExpiry { get; private set; }

    public string? BbbeeCertificateType { get; private set; }

    /// <summary>CIDB grade 1–9, or null when not registered.</summary>
    public int? CidbGrade { get; private set; }

    public string[] CidbClasses { get; private set; } = [];

    public Province[] Provinces { get; private set; } = [];

    public int YearsTrading { get; private set; }

    public int? EmployeeCount { get; private set; }

    public decimal? AnnualTurnover { get; private set; }

    /// <summary>Capabilities as JSON (jsonb). Use <see cref="Capabilities"/> for the typed view.</summary>
    public string CapabilitiesJson { get; private set; } = "{}";

    public string? ContactJson { get; private set; }

    public string? AddressJson { get; private set; }

    public bool IsActive { get; private set; } = true;

    public CompanyCapabilities Capabilities
    {
        get => DomainJson.Deserialize<CompanyCapabilities>(CapabilitiesJson) ?? CompanyCapabilities.Empty;
        private set => CapabilitiesJson = DomainJson.Serialize(value);
    }

    public static CompanyProfile Create(string name)
    {
        return new CompanyProfile { Name = Guard.NotEmpty(name, nameof(name)) };
    }

    public void UpdateDetails(string name, string? tradingName, string? registrationNumber, string? vatNumber, int yearsTrading, int? employeeCount, decimal? annualTurnover)
    {
        Name = Guard.NotEmpty(name, nameof(name));
        TradingName = Guard.Trimmed(tradingName);
        RegistrationNumber = Guard.Trimmed(registrationNumber);
        VatNumber = Guard.Trimmed(vatNumber);
        YearsTrading = Guard.InRange(yearsTrading, 0, 200, nameof(yearsTrading));
        EmployeeCount = employeeCount;
        AnnualTurnover = annualTurnover;
    }

    public void UpdateCompliance(string? csdNumber, DateTimeOffset? csdExpiry, string? taxPin, DateTimeOffset? taxPinExpiry, int? bbbeeLevel, DateTimeOffset? bbbeeExpiry, string? bbbeeCertificateType)
    {
        if (bbbeeLevel is not null)
        {
            Guard.InRange(bbbeeLevel.Value, 1, 8, nameof(bbbeeLevel));
        }

        CsdNumber = Guard.Trimmed(csdNumber);
        CsdExpiry = csdExpiry;
        TaxPin = Guard.Trimmed(taxPin);
        TaxPinExpiry = taxPinExpiry;
        BbbeeLevel = bbbeeLevel;
        BbbeeExpiry = bbbeeExpiry;
        BbbeeCertificateType = Guard.Trimmed(bbbeeCertificateType);
    }

    public void UpdateCidb(int? grade, IEnumerable<string> classes)
    {
        if (grade is not null)
        {
            Guard.InRange(grade.Value, 1, 9, nameof(grade));
        }

        CidbGrade = grade;
        CidbClasses = [.. classes.Select(c => c.Trim().ToUpperInvariant()).Where(c => c.Length > 0).Distinct(StringComparer.Ordinal)];
    }

    public void UpdateProvinces(IEnumerable<Province> provinces) =>
        Provinces = [.. provinces.Where(p => p != Province.Unknown).Distinct()];

    public void UpdateCapabilities(CompanyCapabilities capabilities) => Capabilities = capabilities ?? CompanyCapabilities.Empty;

    public void UpdateContact(string? contactJson, string? addressJson)
    {
        ContactJson = Guard.Trimmed(contactJson);
        AddressJson = Guard.Trimmed(addressJson);
    }

    public void SetActive(bool active) => IsActive = active;

    public bool HasValidCsd(DateTimeOffset asOf) => CsdNumber is not null && (CsdExpiry is null || CsdExpiry > asOf);

    public bool HasValidTaxPin(DateTimeOffset asOf) => TaxPin is not null && (TaxPinExpiry is null || TaxPinExpiry > asOf);

    public bool HasValidBbbee(DateTimeOffset asOf) => BbbeeLevel is not null && (BbbeeExpiry is null || BbbeeExpiry > asOf);
}
