using MediatR;
using TenderSync.Application.Abstractions.Persistence;
using TenderSync.Application.Common.Auditing;
using TenderSync.Application.Common.Exceptions;
using TenderSync.Application.Common.Messaging;
using TenderSync.Application.Security;
using TenderSync.Domain.Companies;

namespace TenderSync.Application.Features.Companies.Commands.SetCompanyProfileActive;

/// <summary>DELETE /company-profiles/{id} deactivates (profiles are never hard-deleted: matches and reports reference them); POST .../activate restores.</summary>
[Authorize(Roles.Admin)]
public sealed record SetCompanyProfileActiveCommand(Guid Id, bool Active) : ICommand<CompanyProfileDto>, IAuditedEntityReference
{
    public string AuditEntityType => nameof(CompanyProfile);

    public string? AuditEntityId => Id.ToString();
}

public sealed class SetCompanyProfileActiveHandler(IAppDbContext db) : IRequestHandler<SetCompanyProfileActiveCommand, CompanyProfileDto>
{
    public async Task<CompanyProfileDto> Handle(SetCompanyProfileActiveCommand request, CancellationToken cancellationToken)
    {
        var profile = await db.CompanyProfiles.GetByIdAsync(request.Id, cancellationToken)
            ?? throw new NotFoundException(nameof(CompanyProfile), request.Id);
        profile.SetActive(request.Active);
        return CompanyProfileDto.From(profile, await db.CompanyProfiles.CountDocumentsAsync(profile.Id, cancellationToken));
    }
}
