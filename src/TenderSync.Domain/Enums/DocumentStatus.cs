namespace TenderSync.Domain.Enums;

/// <summary>Download and text-extraction state of a tender document.</summary>
public enum DocumentStatus
{
    Pending = 0,
    Downloaded = 1,
    Failed = 2,
    Skipped = 3,
    Extracted = 4,
}
