using MediatR;
using TenderSync.Application.Abstractions.Persistence;
using TenderSync.Application.Abstractions.Storage;
using TenderSync.Application.Common.Exceptions;
using TenderSync.Application.Common.Messaging;
using TenderSync.Application.Security;
using TenderSync.Domain.Tenders;

namespace TenderSync.Application.Features.Documents.Queries.GetTenderDocumentFile;

/// <summary>GET /tenders/{id}/documents/{docId}/download: streams the stored copy of a public bid-pack file.</summary>
[Authorize(Roles.Viewer)]
public sealed record GetTenderDocumentFileQuery(Guid TenderId, Guid DocumentId) : IQuery<DocumentFile>;

/// <summary>An open stream the endpoint copies to the response; the caller disposes it.</summary>
public sealed record DocumentFile(Stream Content, string ContentType, string FileName, long? Length);

public sealed class GetTenderDocumentFileHandler(IAppDbContext db, IDocumentStore store) : IRequestHandler<GetTenderDocumentFileQuery, DocumentFile>
{
    public async Task<DocumentFile> Handle(GetTenderDocumentFileQuery request, CancellationToken cancellationToken)
    {
        var document = await db.Tenders.GetDocumentAsync(request.DocumentId, cancellationToken);
        if (document is null || document.TenderId != request.TenderId)
        {
            throw new NotFoundException(nameof(TenderDocument), request.DocumentId);
        }

        if (document.StoragePath is null)
        {
            throw new ConflictException($"Document '{document.Title}' has not been downloaded yet (status {document.Status}). Open the original at {document.Url}.");
        }

        var format = DocumentFormats.Normalise(Path.GetExtension(document.StoragePath));
        var fileName = Path.GetExtension(document.Title).Length > 1 || format is null ? document.Title : $"{document.Title}.{format}";
        var content = await store.OpenReadAsync(document.StoragePath, cancellationToken);
        return new DocumentFile(content, DocumentFormats.ContentTypeFor(format), fileName, document.SizeBytes);
    }
}
