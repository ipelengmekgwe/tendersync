namespace TenderSync.Domain.Enums;

/// <summary>Section of the bid pack a requirement came from.</summary>
public enum RequirementKind
{
    Mandatory = 0,
    Functional = 1,
    Pricing = 2,
    Document = 3,
    Briefing = 4,
    Submission = 5,
    Evaluation = 6,
    Contract = 7,
    Other = 8,
}
