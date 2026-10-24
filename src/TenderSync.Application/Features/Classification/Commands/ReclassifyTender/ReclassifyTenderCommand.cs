using MediatR;
using TenderSync.Application.Common.Messaging;
using TenderSync.Application.Security;

namespace TenderSync.Application.Features.Classification.Commands.ReclassifyTender;

/// <summary>
/// POST /tenders/{id}/reclassify (Reviewer): runs both stages now and returns the outcome, so a reviewer sees the new score
/// without waiting for the queue. The model call happens inline (a few seconds); the audit row records who asked.
/// </summary>
[Authorize(Roles.Reviewer)]
public sealed record ReclassifyTenderCommand(Guid TenderId, bool PrefilterOnly = false) : ICommand<ClassificationOutcome>;

public sealed class ReclassifyTenderHandler(TenderClassifier classifier) : IRequestHandler<ReclassifyTenderCommand, ClassificationOutcome>
{
    public Task<ClassificationOutcome> Handle(ReclassifyTenderCommand request, CancellationToken cancellationToken) =>
        classifier.ClassifyAsync(request.TenderId, request.PrefilterOnly, cancellationToken);
}
