using MediatR;
using TenderSync.Application.Abstractions.Persistence;
using TenderSync.Application.Common.Exceptions;
using TenderSync.Application.Common.Messaging;
using TenderSync.Application.Security;
using TenderSync.Domain.Companies;

namespace TenderSync.Application.Features.Companies.Queries.GetCompanyProfile;

[Authorize(Roles.Viewer)]
public sealed record GetCompanyProfileQuery(Guid Id) : IQuery<CompanyProfileDto>;

public sealed class GetCompanyProfileHandler(IAppDbContext db) : IRequestHandler<GetCompanyProfileQuery, CompanyProfileDto>
{
    public async Task<CompanyProfileDto> Handle(GetCompanyProfileQuery request, CancellationToken cancellationToken)
    {
        var profile = await db.CompanyProfiles.GetByIdAsync(request.Id, cancellationToken)
            ?? throw new NotFoundException(nameof(CompanyProfile), request.Id);
        return CompanyProfileDto.From(profile, await db.CompanyProfiles.CountDocumentsAsync(profile.Id, cancellationToken));
    }
}
