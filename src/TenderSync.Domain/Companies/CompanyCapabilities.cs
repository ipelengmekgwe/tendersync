namespace TenderSync.Domain.Companies;

/// <summary>Structured capability data for a company, stored as JSON on <see cref="CompanyProfile"/>.</summary>
public sealed record CompanyCapabilities
{
    public IReadOnlyList<string> CategoryCodes { get; init; } = [];

    public IReadOnlyList<string> Technologies { get; init; } = [];

    public IReadOnlyList<string> Industries { get; init; } = [];

    public IReadOnlyList<CompanyCertification> Certifications { get; init; } = [];

    public IReadOnlyList<KeyPerson> KeyPersonnel { get; init; } = [];

    public IReadOnlyList<ReferenceProject> ReferenceProjects { get; init; } = [];

    public static CompanyCapabilities Empty { get; } = new();
}

public sealed record CompanyCertification(string Name, string? Issuer, DateTimeOffset? ExpiresAt);

public sealed record KeyPerson(string Name, string Role, int? YearsExperience, string? Summary);

public sealed record ReferenceProject(string Client, string Scope, decimal? Value, int? Year, bool Contactable);
