using TenderSync.Domain.Common;
using TenderSync.Domain.Enums;

namespace TenderSync.Domain.Tenders;

/// <summary>One status transition of a tender.</summary>
public sealed class TenderStatusHistory : Entity
{
    private TenderStatusHistory()
    {
    }

    internal TenderStatusHistory(Guid tenderId, TenderStatus from, TenderStatus to, string? reason, DateTimeOffset at)
    {
        TenderId = tenderId;
        FromStatus = from;
        ToStatus = to;
        Reason = reason;
        At = at;
    }

    public Guid TenderId { get; private set; }

    public TenderStatus FromStatus { get; private set; }

    public TenderStatus ToStatus { get; private set; }

    public string? Reason { get; private set; }

    public DateTimeOffset At { get; private set; }
}
