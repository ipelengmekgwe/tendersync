namespace TenderSync.Domain.Enums;

/// <summary>Derived status (the feed sends an empty status). See Tender.DeriveStatus.</summary>
public enum TenderStatus
{
    Unknown = 0,
    Open = 1,
    ClosingSoon = 2,
    Closed = 3,
    Awarded = 4,
    Cancelled = 5,
}
