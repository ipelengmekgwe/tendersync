namespace TenderSync.Domain.Tenders;

/// <summary>Extracted text of one page (or sheet) of a tender document. Page 0 is used for formats without pages.</summary>
public sealed class DocumentText
{
    private DocumentText()
    {
    }

    public DocumentText(Guid documentId, int page, string text)
    {
        DocumentId = documentId;
        Page = page;
        Text = text ?? string.Empty;
        CharCount = Text.Length;
    }

    public Guid DocumentId { get; private set; }

    public int Page { get; private set; }

    public string Text { get; private set; } = string.Empty;

    public int CharCount { get; private set; }
}
