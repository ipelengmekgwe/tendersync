namespace TenderSync.Domain.Enums;

/// <summary>Kind of procuring entity, inferred from its name and seeded lookups.</summary>
public enum BuyerType
{
    Unknown = 0,
    National = 1,
    Provincial = 2,
    Municipal = 3,
    StateOwnedEntity = 4,
    Other = 5,
}
