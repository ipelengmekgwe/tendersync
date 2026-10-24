using MediatR;
using TenderSync.Application.Abstractions;
using TenderSync.Application.Abstractions.Persistence;
using TenderSync.Application.Common.Exceptions;
using TenderSync.Application.Common.Messaging;
using TenderSync.Application.Security;
using TenderSync.Domain.Tenders;

namespace TenderSync.Application.Features.Documents.Commands.RequestDocumentDownload;

/// <summary>
/// POST /tenders/{id}/documents/download. Queues every document that is not yet stored; the Worker's outbox dispatches
/// <see cref="Domain.Events.TenderDocumentsRequested"/> to <c>DownloadTenderDocumentsJob</c>. Reviewer and above (PLAN.md §10).
/// </summary>
[Authorize(Roles.Reviewer)]
public sealed record RequestDocumentDownloadCommand(Guid TenderId) : ICommand<RequestDocumentDownloadResponse>;

public sealed record RequestDocumentDownloadResponse(Guid TenderId, int QueuedDocuments, int TotalDocuments);

public sealed class RequestDocumentDownloadHandler(IAppDbContext db, IDateTimeProvider clock) : IRequestHandler<RequestDocumentDownloadCommand, RequestDocumentDownloadResponse>
{
    public async Task<RequestDocumentDownloadResponse> Handle(RequestDocumentDownloadCommand request, CancellationToken cancellationToken)
    {
        var tender = await db.Tenders.GetWithDetailsAsync(request.TenderId, cancellationToken)
            ?? throw new NotFoundException(nameof(Tender), request.TenderId);

        var queued = tender.RequestDocumentDownload(clock.UtcNow);
        return new RequestDocumentDownloadResponse(tender.Id, queued, tender.Documents.Count);
    }
}
