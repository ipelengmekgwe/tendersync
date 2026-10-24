using TenderSync.Domain.Applications;
using TenderSync.Domain.Companies;
using TenderSync.Domain.Notifications;
using TenderSync.Domain.Operations;
using TenderSync.Domain.Qualification;
using TenderSync.Domain.Tenders;

namespace TenderSync.Application.Abstractions.Persistence;

/// <summary>
/// Unit of work over the write model. Handlers mutate aggregates through the repositories and call
/// <see cref="SaveChangesAsync"/> once; domain events raised by tracked entities are written to the outbox in the same transaction.
/// </summary>
public interface IAppDbContext
{
    ITenderRepository Tenders { get; }

    IBuyerRepository Buyers { get; }

    IInterestCategoryRepository InterestCategories { get; }

    ICompanyProfileRepository CompanyProfiles { get; }

    IRepository<CompanyDocument> CompanyDocuments { get; }

    ITenderMatchRepository TenderMatches { get; }

    IRepository<TenderRequirement> TenderRequirements { get; }

    IQualificationReportRepository QualificationReports { get; }

    INotificationRepository Notifications { get; }

    IRepository<NotificationPreference> NotificationPreferences { get; }

    IOutboxRepository Outbox { get; }

    IRepository<BidApplication> Applications { get; }

    IRepository<ApplicationDocument> ApplicationDocuments { get; }

    IRepository<LlmCall> LlmCalls { get; }

    IRepository<JobRun> JobRuns { get; }

    IRepository<SourceHealthCheck> SourceHealthChecks { get; }

    IRepository<AuditLog> AuditLogs { get; }

    IMunicipalityRepository Municipalities { get; }

    Task<int> SaveChangesAsync(CancellationToken cancellationToken = default);
}
