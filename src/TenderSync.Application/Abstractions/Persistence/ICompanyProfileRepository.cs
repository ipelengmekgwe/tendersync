using TenderSync.Domain.Companies;

namespace TenderSync.Application.Abstractions.Persistence;

public interface ICompanyProfileRepository : IRepository<CompanyProfile>
{
    Task<IReadOnlyList<CompanyProfile>> ListActiveAsync(CancellationToken cancellationToken = default);

    Task<IReadOnlyList<CompanyDocument>> GetDocumentsAsync(Guid companyProfileId, bool includeDeleted = false, CancellationToken cancellationToken = default);
}
