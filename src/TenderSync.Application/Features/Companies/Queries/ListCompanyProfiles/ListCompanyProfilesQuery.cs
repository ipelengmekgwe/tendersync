using MediatR;
using TenderSync.Application.Abstractions.Persistence;
using TenderSync.Application.Common.Messaging;
using TenderSync.Application.Security;

namespace TenderSync.Application.Features.Companies.Queries.ListCompanyProfiles;

/// <summary>GET /company-profiles: active profiles, or every profile with includeInactive.</summary>
[Authorize(Roles.Viewer)]
public sealed record ListCompanyProfilesQuery(bool IncludeInactive = false) : IQuery<IReadOnlyList<CompanyProfileListItemDto>>;

public sealed class ListCompanyProfilesHandler(IAppDbContext db) : IRequestHandler<ListCompanyProfilesQuery, IReadOnlyList<CompanyProfileListItemDto>>
{
    public async Task<IReadOnlyList<CompanyProfileListItemDto>> Handle(ListCompanyProfilesQuery request, CancellationToken cancellationToken)
    {
        var profiles = request.IncludeInactive
            ? await db.CompanyProfiles.ListAllAsync(cancellationToken)
            : await db.CompanyProfiles.ListActiveAsync(cancellationToken);
        return profiles.Select(p => new CompanyProfileListItemDto(p.Id, p.Name, p.RegistrationNumber, p.IsActive, p.YearsTrading, p.Capabilities.CategoryCodes, p.UpdatedAt)).ToList();
    }
}
