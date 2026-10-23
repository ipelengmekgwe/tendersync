using TenderSync.Domain.Common;
using TenderSync.Domain.Enums;

namespace TenderSync.Domain.Applications;

/// <summary>
/// A bid being prepared for a tender (Phase 2+). Submission is recorded by a human; the system never submits (ADR-015).
/// Named BidApplication to avoid clashing with the TenderSync.Application namespace; the table is still "applications".
/// </summary>
public sealed class BidApplication : Entity
{
    private BidApplication()
    {
    }

    public Guid TenderId { get; private set; }

    public Guid CompanyProfileId { get; private set; }

    public ApplicationStatus Status { get; private set; }

    public string ChecklistJson { get; private set; } = "[]";

    public DateTimeOffset? SubmittedAt { get; private set; }

    public string? SubmittedBy { get; private set; }

    public string? SubmissionReference { get; private set; }

    public static BidApplication Create(Guid tenderId, Guid companyProfileId)
    {
        return new BidApplication { TenderId = tenderId, CompanyProfileId = companyProfileId, Status = ApplicationStatus.Draft };
    }

    public void SetChecklist(string checklistJson) => ChecklistJson = Guard.NotEmpty(checklistJson, nameof(checklistJson));

    public void Transition(ApplicationStatus status)
    {
        if (status == ApplicationStatus.Submitted)
        {
            throw new DomainException("Use RecordSubmission to mark an application as submitted by a human.");
        }

        Status = status;
    }

    public void RecordSubmission(string submittedBy, string? reference, DateTimeOffset now)
    {
        if (Status != ApplicationStatus.Packaged)
        {
            throw new DomainException("Only a packaged application can be recorded as submitted.");
        }

        Status = ApplicationStatus.Submitted;
        SubmittedBy = Guard.NotEmpty(submittedBy, nameof(submittedBy));
        SubmissionReference = Guard.Trimmed(reference);
        SubmittedAt = now;
    }
}
