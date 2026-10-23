namespace TenderSync.Domain.Enums;

/// <summary>How far the classification pipeline got for a tender.</summary>
public enum ClassificationStage
{
    None = 0,
    Keyword = 1,
    Llm = 2,
}
