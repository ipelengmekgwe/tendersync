namespace TenderSync.Domain.Enums;

/// <summary>State of a bid application (Phase 2+). Submission is always recorded by a human (ADR-015).</summary>
public enum ApplicationStatus
{
    Draft = 0,
    InReview = 1,
    Approved = 2,
    Packaged = 3,
    Submitted = 4,
    Withdrawn = 5,
}
