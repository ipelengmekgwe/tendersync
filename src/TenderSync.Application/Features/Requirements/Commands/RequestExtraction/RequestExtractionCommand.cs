using MediatR;
using TenderSync.Application.Abstractions;
using TenderSync.Application.Abstractions.Persistence;
using TenderSync.Application.Common.Exceptions;
using TenderSync.Application.Common.Messaging;
using TenderSync.Application.Security;
using TenderSync.Domain.Enums;
using TenderSync.Domain.Tenders;

namespace TenderSync.Application.Features.Requirements.Commands.RequestExtraction;

/// <summary>
/// POST /tenders/{id}/extract (Reviewer): queues requirement extraction through the outbox. Documents that are not
/// downloaded yet are queued for download first; the pipeline then chains extraction once their text is ready.
/// </summary>
[Authorize(Roles.Reviewer)]
public sealed record RequestExtractionCommand(Guid TenderId) : ICommand<RequestExtractionResponse>;

public sealed record RequestExtractionResponse(Guid TenderId, int DocumentsWithText, int DocumentsQueuedForDownload, bool ExtractionQueued);

public sealed class RequestExtractionHandler(IAppDbContext db, IDateTimeProvider clock) : IRequestHandler<RequestExtractionCommand, RequestExtractionResponse>
{
    public async Task<RequestExtractionResponse> Handle(RequestExtractionCommand request, CancellationToken cancellationToken)
    {
        var tender = await db.Tenders.GetWithDetailsAsync(request.TenderId, cancellationToken)
            ?? throw new NotFoundException(nameof(Tender), request.TenderId);

        var withText = tender.Documents.Count(d => d.Status == DocumentStatus.Extracted);
        var now = clock.UtcNow;
        var queuedDownloads = 0;
        if (tender.Documents.Any(d => d.Status is DocumentStatus.Pending or DocumentStatus.Failed))
        {
            queuedDownloads = tender.RequestDocumentDownload(now);
        }

        var queued = withText > 0;
        if (queued)
        {
            tender.RequestRequirementExtraction(now);
        }

        if (!queued && queuedDownloads == 0)
        {
            throw new ConflictException($"Tender '{tender.BidNumber}' has no document with extracted text and nothing to download.");
        }

        return new RequestExtractionResponse(tender.Id, withText, queuedDownloads, queued);
    }
}
