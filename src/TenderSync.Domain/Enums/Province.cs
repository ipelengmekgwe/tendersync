namespace TenderSync.Domain.Enums;

/// <summary>South African provinces plus National for organs of state that span the country.</summary>
public enum Province
{
    Unknown = 0,
    EasternCape = 1,
    FreeState = 2,
    Gauteng = 3,
    KwaZuluNatal = 4,
    Limpopo = 5,
    Mpumalanga = 6,
    NorthWest = 7,
    NorthernCape = 8,
    WesternCape = 9,
    /// <summary>National department or state-owned entity with no single province.</summary>
    National = 10,
}
