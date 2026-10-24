using MediatR;
using TenderSync.Application.Abstractions.Persistence;
using TenderSync.Application.Abstractions.Security;
using TenderSync.Application.Abstractions.Storage;
using TenderSync.Application.Common.Auditing;
using TenderSync.Application.Common.Exceptions;
using TenderSync.Application.Common.Messaging;
using TenderSync.Application.Features.Documents.Queries.GetTenderDocumentFile;
using TenderSync.Application.Security;
using TenderSync.Domain.Companies;

namespace TenderSync.Application.Features.Companies.Commands.DownloadCompanyDocument;

/// <summary>
/// GET /company-profiles/{id}/documents/{docId}/download (Reviewer). A command rather than a query so every download lands
/// in audit_logs (PLAN.md §12: downloads audited); the content is decrypted while it streams to the response.
/// </summary>
[Authorize(Roles.Reviewer)]
public sealed record DownloadCompanyDocumentCommand(Guid CompanyProfileId, Guid DocumentId) : ICommand<DocumentFile>, IAuditedEntityReference
{
    public string AuditEntityType => nameof(CompanyDocument);

    public string? AuditEntityId => DocumentId.ToString();
}

public sealed class DownloadCompanyDocumentHandler(IAppDbContext db, IDocumentStore store, IEncryptionService encryption) : IRequestHandler<DownloadCompanyDocumentCommand, DocumentFile>
{
    public async Task<DocumentFile> Handle(DownloadCompanyDocumentCommand request, CancellationToken cancellationToken)
    {
        var document = await db.CompanyDocuments.GetByIdAsync(request.DocumentId, cancellationToken);
        if (document is null || document.CompanyProfileId != request.CompanyProfileId || document.IsDeleted)
        {
            throw new NotFoundException(nameof(CompanyDocument), request.DocumentId);
        }

        var cipher = await store.OpenReadAsync(document.StoragePath, cancellationToken);
        var plain = await encryption.DecryptAsync(cipher, document.EncryptedDataKey, cancellationToken);
        var fileName = Path.GetExtension(document.Title).Length > 1 ? document.Title : document.Title + ExtensionFor(document.ContentType);
        return new DocumentFile(plain, document.ContentType ?? "application/octet-stream", fileName, document.SizeBytes);
    }

    private static string ExtensionFor(string? contentType) => contentType switch
    {
        "application/pdf" => ".pdf",
        "application/vnd.openxmlformats-officedocument.wordprocessingml.document" => ".docx",
        "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet" => ".xlsx",
        "image/png" => ".png",
        "image/jpeg" => ".jpg",
        _ => string.Empty,
    };
}
