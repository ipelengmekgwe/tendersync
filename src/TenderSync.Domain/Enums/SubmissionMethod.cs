namespace TenderSync.Domain.Enums;

/// <summary>How a bid must be submitted, as extracted from the bid document.</summary>
public enum SubmissionMethod
{
    Unknown = 0,
    ETendersOnline = 1,
    Physical = 2,
    Email = 3,
    Other = 4,
}
