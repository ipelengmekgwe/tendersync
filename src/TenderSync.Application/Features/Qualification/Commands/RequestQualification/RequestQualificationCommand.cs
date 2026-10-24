using MediatR;
using TenderSync.Application.Abstractions;
using TenderSync.Application.Abstractions.Persistence;
using TenderSync.Application.Common.Exceptions;
using TenderSync.Application.Common.Messaging;
using TenderSync.Application.Security;
using TenderSync.Domain.Companies;
using TenderSync.Domain.Tenders;

namespace TenderSync.Application.Features.Qualification.Commands.RequestQualification;

/// <summary>POST /tenders/{id}/qualify (Reviewer): queues a qualification run through the outbox for one profile or every matched one.</summary>
[Authorize(Roles.Reviewer)]
public sealed record RequestQualificationCommand(Guid TenderId, Guid? CompanyProfileId = null) : ICommand<RequestQualificationResponse>;

public sealed record RequestQualificationResponse(Guid TenderId, Guid? CompanyProfileId, int RequirementCount, bool Queued);

public sealed class RequestQualificationHandler(IAppDbContext db, IDateTimeProvider clock) : IRequestHandler<RequestQualificationCommand, RequestQualificationResponse>
{
    public async Task<RequestQualificationResponse> Handle(RequestQualificationCommand request, CancellationToken cancellationToken)
    {
        var tender = await db.Tenders.GetWithDetailsAsync(request.TenderId, cancellationToken)
            ?? throw new NotFoundException(nameof(Tender), request.TenderId);
        var requirements = await db.Tenders.GetRequirementsAsync(tender.Id, cancellationToken);
        if (requirements.Count == 0)
        {
            throw new ConflictException($"Tender '{tender.BidNumber}' has no extracted requirements; run POST /tenders/{tender.Id}/extract first.");
        }

        if (request.CompanyProfileId is { } profileId && await db.CompanyProfiles.GetByIdAsync(profileId, cancellationToken) is null)
        {
            throw new NotFoundException(nameof(CompanyProfile), profileId);
        }

        tender.RequestQualification(request.CompanyProfileId, clock.UtcNow);
        return new RequestQualificationResponse(tender.Id, request.CompanyProfileId, requirements.Count, true);
    }
}
