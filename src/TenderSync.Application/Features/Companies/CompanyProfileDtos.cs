using FluentValidation;
using TenderSync.Application.Common.Auditing;
using TenderSync.Domain.Companies;
using TenderSync.Domain.Enums;

namespace TenderSync.Application.Features.Companies;

/// <summary>Body of POST/PUT /company-profiles: everything the qualification engine compares against (PLAN.md §4.5).</summary>
public sealed record CompanyProfileInput(
    string Name,
    string? TradingName = null,
    string? RegistrationNumber = null,
    string? VatNumber = null,
    int YearsTrading = 0,
    int? EmployeeCount = null,
    decimal? AnnualTurnover = null,
    string? CsdNumber = null,
    DateTimeOffset? CsdExpiry = null,
    [property: Sensitive] string? TaxPin = null,
    DateTimeOffset? TaxPinExpiry = null,
    int? BbbeeLevel = null,
    DateTimeOffset? BbbeeExpiry = null,
    string? BbbeeCertificateType = null,
    int? CidbGrade = null,
    IReadOnlyList<string>? CidbClasses = null,
    IReadOnlyList<Province>? Provinces = null,
    CompanyCapabilities? Capabilities = null,
    string? ContactJson = null,
    string? AddressJson = null,
    bool ShareKeyPersonnelWithModel = false)
{
    /// <summary>
    /// Applies every section to the entity; the entity enforces the domain ranges. Responses only carry the tax PIN masked,
    /// so a null <see cref="TaxPin"/> keeps the stored PIN (an editor that never saw it can still save) and an empty
    /// string clears it.
    /// </summary>
    public void ApplyTo(CompanyProfile profile)
    {
        ArgumentNullException.ThrowIfNull(profile);
        profile.UpdateDetails(Name, TradingName, RegistrationNumber, VatNumber, YearsTrading, EmployeeCount, AnnualTurnover);
        profile.UpdateCompliance(CsdNumber, CsdExpiry, TaxPin ?? profile.TaxPin, TaxPinExpiry, BbbeeLevel, BbbeeExpiry, BbbeeCertificateType);
        profile.UpdateCidb(CidbGrade, CidbClasses ?? []);
        profile.UpdateProvinces(Provinces ?? []);
        profile.UpdateCapabilities(Capabilities ?? CompanyCapabilities.Empty);
        profile.UpdateContact(ContactJson, AddressJson);
        profile.SetModelSharing(ShareKeyPersonnelWithModel);
    }
}

public sealed class CompanyProfileInputValidator : AbstractValidator<CompanyProfileInput>
{
    public CompanyProfileInputValidator()
    {
        RuleFor(p => p.Name).NotEmpty().MaximumLength(256);
        RuleFor(p => p.TradingName).MaximumLength(256);
        RuleFor(p => p.RegistrationNumber).MaximumLength(64);
        RuleFor(p => p.VatNumber).MaximumLength(32);
        RuleFor(p => p.CsdNumber).MaximumLength(32);
        RuleFor(p => p.TaxPin).MaximumLength(32);
        RuleFor(p => p.BbbeeCertificateType).MaximumLength(64);
        RuleFor(p => p.YearsTrading).InclusiveBetween(0, 200);
        RuleFor(p => p.EmployeeCount).GreaterThanOrEqualTo(0).When(p => p.EmployeeCount is not null);
        RuleFor(p => p.AnnualTurnover).GreaterThanOrEqualTo(0).When(p => p.AnnualTurnover is not null);
        RuleFor(p => p.BbbeeLevel).InclusiveBetween(1, 8).When(p => p.BbbeeLevel is not null);
        RuleFor(p => p.CidbGrade).InclusiveBetween(1, 9).When(p => p.CidbGrade is not null);
        RuleForEach(p => p.CidbClasses).Matches("^[A-Za-z]{2}$").WithMessage("CIDB classes are two-letter designations such as GB or CE.");
        RuleFor(p => p.ContactJson).Must(BeJsonOrNull).WithMessage("contactJson must be a JSON object.");
        RuleFor(p => p.AddressJson).Must(BeJsonOrNull).WithMessage("addressJson must be a JSON object.");
        When(p => p.Capabilities is not null, () =>
        {
            RuleForEach(p => p.Capabilities!.CategoryCodes).NotEmpty().MaximumLength(32);
            RuleForEach(p => p.Capabilities!.Certifications).ChildRules(c => c.RuleFor(x => x.Name).NotEmpty().MaximumLength(256));
            RuleForEach(p => p.Capabilities!.KeyPersonnel).ChildRules(k =>
            {
                k.RuleFor(x => x.Name).NotEmpty().MaximumLength(256);
                k.RuleFor(x => x.Role).NotEmpty().MaximumLength(256);
            });
            RuleForEach(p => p.Capabilities!.ReferenceProjects).ChildRules(r =>
            {
                r.RuleFor(x => x.Client).NotEmpty().MaximumLength(256);
                r.RuleFor(x => x.Scope).NotEmpty().MaximumLength(2000);
                r.RuleFor(x => x.Year).InclusiveBetween(1990, 2100).When(x => x.Year is not null);
            });
        });
    }

    private static bool BeJsonOrNull(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return true;
        }

        try
        {
            using var document = System.Text.Json.JsonDocument.Parse(json);
            return document.RootElement.ValueKind == System.Text.Json.JsonValueKind.Object;
        }
        catch (System.Text.Json.JsonException)
        {
            return false;
        }
    }
}

/// <summary>A profile as the API returns it. The tax PIN is masked to its last three characters.</summary>
public sealed record CompanyProfileDto(
    Guid Id,
    string Name,
    string? TradingName,
    string? RegistrationNumber,
    string? VatNumber,
    int YearsTrading,
    int? EmployeeCount,
    decimal? AnnualTurnover,
    string? CsdNumber,
    DateTimeOffset? CsdExpiry,
    string? TaxPinMasked,
    DateTimeOffset? TaxPinExpiry,
    int? BbbeeLevel,
    DateTimeOffset? BbbeeExpiry,
    string? BbbeeCertificateType,
    int? CidbGrade,
    IReadOnlyList<string> CidbClasses,
    IReadOnlyList<Province> Provinces,
    CompanyCapabilities Capabilities,
    string? ContactJson,
    string? AddressJson,
    bool IsActive,
    bool ShareKeyPersonnelWithModel,
    int DocumentCount,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt)
{
    public static CompanyProfileDto From(CompanyProfile p, int documentCount) => new(
        p.Id, p.Name, p.TradingName, p.RegistrationNumber, p.VatNumber, p.YearsTrading, p.EmployeeCount, p.AnnualTurnover,
        p.CsdNumber, p.CsdExpiry, Mask(p.TaxPin), p.TaxPinExpiry, p.BbbeeLevel, p.BbbeeExpiry, p.BbbeeCertificateType,
        p.CidbGrade, p.CidbClasses, p.Provinces, p.Capabilities, p.ContactJson, p.AddressJson, p.IsActive, p.ShareKeyPersonnelWithModel,
        documentCount, p.CreatedAt, p.UpdatedAt);

    public static string? Mask(string? pin) => pin is null ? null : pin.Length <= 3 ? new string('*', pin.Length) : new string('*', pin.Length - 3) + pin[^3..];
}

public sealed record CompanyProfileListItemDto(Guid Id, string Name, string? RegistrationNumber, bool IsActive, int YearsTrading, IReadOnlyList<string> CategoryCodes, DateTimeOffset UpdatedAt);

/// <summary>A company document without its content; the blob is encrypted at rest and streamed through the download endpoint.</summary>
public sealed record CompanyDocumentDto(
    Guid Id,
    Guid CompanyProfileId,
    CompanyDocumentType Type,
    string Title,
    string? ContentType,
    long SizeBytes,
    string Sha256,
    DateTimeOffset? ExpiresAt,
    bool IsExpired,
    bool ExpiresWithin30Days,
    string MasterKeyId,
    DateTimeOffset? DeletedAt,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt)
{
    public static CompanyDocumentDto From(CompanyDocument d, string masterKeyId, DateTimeOffset now) => new(
        d.Id, d.CompanyProfileId, d.Type, d.Title, d.ContentType, d.SizeBytes, d.Sha256, d.ExpiresAt,
        d.IsExpired(now), d.ExpiresWithin(TimeSpan.FromDays(30), now), masterKeyId, d.DeletedAt, d.CreatedAt, d.UpdatedAt);
}
