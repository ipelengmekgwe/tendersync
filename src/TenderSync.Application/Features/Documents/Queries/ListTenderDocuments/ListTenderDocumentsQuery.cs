using MediatR;
using TenderSync.Application.Abstractions.Persistence;
using TenderSync.Application.Common.Exceptions;
using TenderSync.Application.Common.Messaging;
using TenderSync.Application.Features.Tenders;
using TenderSync.Application.Security;
using TenderSync.Domain.Tenders;

namespace TenderSync.Application.Features.Documents.Queries.ListTenderDocuments;

/// <summary>GET /tenders/{id}/documents: every bid-pack file with its download and extraction state.</summary>
[Authorize(Roles.Viewer)]
public sealed record ListTenderDocumentsQuery(Guid TenderId) : IQuery<IReadOnlyList<TenderDocumentDto>>;

public sealed class ListTenderDocumentsHandler(IAppDbContext db) : IRequestHandler<ListTenderDocumentsQuery, IReadOnlyList<TenderDocumentDto>>
{
    public async Task<IReadOnlyList<TenderDocumentDto>> Handle(ListTenderDocumentsQuery request, CancellationToken cancellationToken)
    {
        var tender = await db.Tenders.GetWithDetailsAsync(request.TenderId, cancellationToken)
            ?? throw new NotFoundException(nameof(Tender), request.TenderId);

        return tender.Documents
            .OrderBy(d => d.DatePublished)
            .ThenBy(d => d.Title, StringComparer.Ordinal)
            .Select(TenderDocumentDto.From)
            .ToList();
    }
}
