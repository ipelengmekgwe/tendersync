using TenderSync.Domain.Common;
using TenderSync.Domain.Enums;
using TenderSync.Domain.Events;

namespace TenderSync.Domain.Tenders;

/// <summary>A tender that is relevant to a company profile, and where it is in the bidding workflow.</summary>
public sealed class TenderMatch : Entity
{
    private TenderMatch()
    {
    }

    public Guid TenderId { get; private set; }

    public Guid CompanyProfileId { get; private set; }

    public int Relevance { get; private set; }

    public MatchStatus Status { get; private set; }

    public DateTimeOffset? NotifiedAt { get; private set; }

    public DateTimeOffset? DismissedAt { get; private set; }

    public string? DismissReason { get; private set; }

    public string? Notes { get; private set; }

    public static TenderMatch Create(Guid tenderId, Guid companyProfileId, int relevance, DateTimeOffset now)
    {
        var match = new TenderMatch
        {
            TenderId = tenderId,
            CompanyProfileId = companyProfileId,
            Relevance = Guard.InRange(relevance, 0, 100, nameof(relevance)),
            Status = MatchStatus.New,
        };
        match.Raise(new TenderMatched(tenderId, companyProfileId, match.Id, relevance, now));
        return match;
    }

    public void UpdateRelevance(int relevance) => Relevance = Guard.InRange(relevance, 0, 100, nameof(relevance));

    public void MarkNotified(DateTimeOffset now) => NotifiedAt ??= now;

    public void Dismiss(string reason, DateTimeOffset now)
    {
        Status = MatchStatus.Dismissed;
        DismissReason = Guard.NotEmpty(reason, nameof(reason));
        DismissedAt = now;
    }

    public void SetStatus(MatchStatus status)
    {
        if (status == MatchStatus.Dismissed)
        {
            throw new DomainException("Use Dismiss(reason) to dismiss a match.");
        }

        Status = status;
        DismissedAt = null;
        DismissReason = null;
    }

    public void SetNotes(string? notes) => Notes = Guard.Trimmed(notes);
}
