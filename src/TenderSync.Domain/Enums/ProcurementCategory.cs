namespace TenderSync.Domain.Enums;

/// <summary>OCDS mainProcurementCategory. Unknown covers the empty value the eTenders feed often sends.</summary>
public enum ProcurementCategory
{
    Unknown = 0,
    Goods = 1,
    Services = 2,
    Works = 3,
}
