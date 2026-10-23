namespace TenderSync.Domain.Enums;

/// <summary>Outcome of the qualification engine for a tender and company profile.</summary>
public enum QualificationVerdict
{
    Unknown = 0,
    Qualify = 1,
    Partial = 2,
    NotQualify = 3,
}
