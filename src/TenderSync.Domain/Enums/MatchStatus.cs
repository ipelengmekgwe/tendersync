namespace TenderSync.Domain.Enums;

/// <summary>Workflow state of a tender match for a company profile.</summary>
public enum MatchStatus
{
    New = 0,
    Reviewing = 1,
    Dismissed = 2,
    Applying = 3,
    Applied = 4,
    Won = 5,
    Lost = 6,
    Expired = 7,
}
