using TenderSync.Domain.Qualification;

namespace TenderSync.Application.Abstractions.Persistence;

public interface IQualificationReportRepository : IRepository<QualificationReport>
{
    /// <summary>The newest report for the tender, optionally for one company profile.</summary>
    Task<QualificationReport?> GetLatestAsync(Guid tenderId, Guid? companyProfileId = null, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<QualificationReport>> ListByTenderAsync(Guid tenderId, CancellationToken cancellationToken = default);
}
