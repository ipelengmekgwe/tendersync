using TenderSync.Domain.Companies;

namespace TenderSync.Application.Abstractions.Persistence;

public interface ICompanyProfileRepository : IRepository<CompanyProfile>
{
    Task<IReadOnlyList<CompanyProfile>> ListActiveAsync(CancellationToken cancellationToken = default);

    /// <summary>Every profile, inactive ones included (admin listing and key rotation).</summary>
    Task<IReadOnlyList<CompanyProfile>> ListAllAsync(CancellationToken cancellationToken = default);

    Task<IReadOnlyList<CompanyDocument>> GetDocumentsAsync(Guid companyProfileId, bool includeDeleted = false, CancellationToken cancellationToken = default);

    /// <summary>Documents of every profile, deleted ones included: key rotation re-wraps them all.</summary>
    Task<IReadOnlyList<CompanyDocument>> ListAllDocumentsAsync(CancellationToken cancellationToken = default);

    Task<int> CountDocumentsAsync(Guid companyProfileId, CancellationToken cancellationToken = default);
}
