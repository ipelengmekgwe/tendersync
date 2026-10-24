using MediatR;
using TenderSync.Application.Abstractions.Persistence;
using TenderSync.Application.Common.Exceptions;
using TenderSync.Application.Common.Messaging;
using TenderSync.Application.Security;
using TenderSync.Domain.Enums;
using TenderSync.Domain.Tenders;

namespace TenderSync.Application.Features.Documents.Queries.GetDocumentText;

/// <summary>GET /tenders/{id}/documents/{docId}/text: the extracted pages, for the dashboard's page viewer and for checking extraction quality.</summary>
[Authorize(Roles.Viewer)]
public sealed record GetDocumentTextQuery(Guid TenderId, Guid DocumentId) : IQuery<DocumentTextDto>;

public sealed record DocumentPageTextDto(int Page, int CharCount, string Text);

public sealed record DocumentTextDto(Guid DocumentId, DocumentStatus Status, bool NeedsOcr, int PageCount, int CharCount, IReadOnlyList<DocumentPageTextDto> Pages);

public sealed class GetDocumentTextHandler(IAppDbContext db) : IRequestHandler<GetDocumentTextQuery, DocumentTextDto>
{
    public async Task<DocumentTextDto> Handle(GetDocumentTextQuery request, CancellationToken cancellationToken)
    {
        var document = await db.Tenders.GetDocumentAsync(request.DocumentId, cancellationToken);
        if (document is null || document.TenderId != request.TenderId)
        {
            throw new NotFoundException(nameof(TenderDocument), request.DocumentId);
        }

        var pages = await db.Tenders.GetDocumentTextsAsync(document.Id, cancellationToken);
        return new DocumentTextDto(
            document.Id,
            document.Status,
            document.NeedsOcr,
            pages.Count,
            pages.Sum(p => p.CharCount),
            pages.Select(p => new DocumentPageTextDto(p.Page, p.CharCount, p.Text)).ToList());
    }
}
