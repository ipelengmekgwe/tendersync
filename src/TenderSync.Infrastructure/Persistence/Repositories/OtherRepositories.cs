using Microsoft.EntityFrameworkCore;
using TenderSync.Application.Abstractions.Persistence;
using TenderSync.Domain.Categories;
using TenderSync.Domain.Companies;
using TenderSync.Domain.Geography;
using TenderSync.Domain.Notifications;
using TenderSync.Domain.Qualification;
using TenderSync.Domain.Tenders;

namespace TenderSync.Infrastructure.Persistence.Repositories;

internal sealed class BuyerRepository(AppDbContext context) : EfRepository<Buyer>(context), IBuyerRepository
{
    public Task<Buyer?> GetByExternalIdAsync(string externalId, CancellationToken cancellationToken = default) =>
        Set.FirstOrDefaultAsync(b => b.ExternalId == externalId, cancellationToken);

    public async Task<IReadOnlyDictionary<string, Buyer>> GetByExternalIdsAsync(IEnumerable<string> externalIds, CancellationToken cancellationToken = default)
    {
        var list = externalIds.Distinct(StringComparer.Ordinal).ToArray();
        if (list.Length == 0)
        {
            return new Dictionary<string, Buyer>(StringComparer.Ordinal);
        }

        return await Set.Where(b => list.Contains(b.ExternalId)).ToDictionaryAsync(b => b.ExternalId, StringComparer.Ordinal, cancellationToken);
    }
}

internal sealed class InterestCategoryRepository(AppDbContext context) : EfRepository<InterestCategory>(context), IInterestCategoryRepository
{
    public async Task<IReadOnlyList<InterestCategory>> ListAsync(bool enabledOnly, CancellationToken cancellationToken = default)
    {
        var query = enabledOnly ? Set.Where(c => c.Enabled) : Set;
        return await query.OrderBy(c => c.Code).ToListAsync(cancellationToken);
    }

    public Task<InterestCategory?> GetByCodeAsync(string code, CancellationToken cancellationToken = default)
    {
        var upper = code.ToUpperInvariant();
        return Set.FirstOrDefaultAsync(c => c.Code == upper, cancellationToken);
    }
}

internal sealed class CompanyProfileRepository(AppDbContext context) : EfRepository<CompanyProfile>(context), ICompanyProfileRepository
{
    public async Task<IReadOnlyList<CompanyProfile>> ListActiveAsync(CancellationToken cancellationToken = default) =>
        await Set.Where(p => p.IsActive).OrderBy(p => p.Name).ToListAsync(cancellationToken);

    public async Task<IReadOnlyList<CompanyProfile>> ListAllAsync(CancellationToken cancellationToken = default) =>
        await Set.OrderBy(p => p.Name).ToListAsync(cancellationToken);

    public async Task<IReadOnlyList<CompanyDocument>> ListAllDocumentsAsync(CancellationToken cancellationToken = default) =>
        await Context.CompanyDocumentSet.IgnoreQueryFilters().OrderBy(d => d.CreatedAt).ToListAsync(cancellationToken);

    public Task<int> CountDocumentsAsync(Guid companyProfileId, CancellationToken cancellationToken = default) =>
        Context.CompanyDocumentSet.CountAsync(d => d.CompanyProfileId == companyProfileId, cancellationToken);

    public async Task<IReadOnlyList<CompanyDocument>> GetDocumentsAsync(Guid companyProfileId, bool includeDeleted = false, CancellationToken cancellationToken = default)
    {
        IQueryable<CompanyDocument> query = Context.CompanyDocumentSet;
        if (includeDeleted)
        {
            query = query.IgnoreQueryFilters();
        }

        return await query.Where(d => d.CompanyProfileId == companyProfileId).OrderBy(d => d.Type).ThenBy(d => d.Title).ToListAsync(cancellationToken);
    }
}

internal sealed class QualificationReportRepository(AppDbContext context) : EfRepository<QualificationReport>(context), IQualificationReportRepository
{
    public Task<QualificationReport?> GetLatestAsync(Guid tenderId, Guid? companyProfileId = null, CancellationToken cancellationToken = default)
    {
        var query = Set.Where(r => r.TenderId == tenderId);
        if (companyProfileId is { } profileId)
        {
            query = query.Where(r => r.CompanyProfileId == profileId);
        }

        return query.OrderByDescending(r => r.CreatedAt).ThenByDescending(r => r.Id).FirstOrDefaultAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<QualificationReport>> ListByTenderAsync(Guid tenderId, CancellationToken cancellationToken = default) =>
        await Set.Where(r => r.TenderId == tenderId).OrderByDescending(r => r.CreatedAt).ThenByDescending(r => r.Id).ToListAsync(cancellationToken);
}

internal sealed class TenderMatchRepository(AppDbContext context) : EfRepository<TenderMatch>(context), ITenderMatchRepository
{
    public Task<TenderMatch?> FindAsync(Guid tenderId, Guid companyProfileId, CancellationToken cancellationToken = default) =>
        Set.FirstOrDefaultAsync(m => m.TenderId == tenderId && m.CompanyProfileId == companyProfileId, cancellationToken);

    public async Task<IReadOnlyList<TenderMatch>> ListByTenderAsync(Guid tenderId, CancellationToken cancellationToken = default) =>
        await Set.Where(m => m.TenderId == tenderId).ToListAsync(cancellationToken);
}

internal sealed class NotificationRepository(AppDbContext context) : EfRepository<Notification>(context), INotificationRepository
{
    public Task<bool> ExistsAsync(string dedupeKey, CancellationToken cancellationToken = default) =>
        Set.AnyAsync(n => n.DedupeKey == dedupeKey, cancellationToken);

    public async Task<IReadOnlyList<Notification>> GetPendingAsync(int batchSize, CancellationToken cancellationToken = default) =>
        await Set.Where(n => n.Status == Domain.Enums.NotificationStatus.Pending)
            .OrderBy(n => n.CreatedAt)
            .Take(batchSize)
            .ToListAsync(cancellationToken);

    public async Task<IReadOnlyList<Notification>> GetDueAsync(DateTimeOffset now, DateTimeOffset createdBefore, int batchSize, CancellationToken cancellationToken = default) =>
        await Set.Where(n => n.Status == Domain.Enums.NotificationStatus.Pending && (n.NotBefore == null || n.NotBefore <= now) && n.CreatedAt <= createdBefore)
            .OrderBy(n => n.CreatedAt)
            .Take(batchSize)
            .ToListAsync(cancellationToken);
}

internal sealed class NotificationPreferenceRepository(AppDbContext context) : EfRepository<NotificationPreference>(context), INotificationPreferenceRepository
{
    public async Task<IReadOnlyList<NotificationPreference>> ListByUserAsync(Guid userId, CancellationToken cancellationToken = default) =>
        await Set.Where(p => p.UserId == userId).OrderBy(p => p.Channel).ToListAsync(cancellationToken);

    public async Task<IReadOnlyList<NotificationPreference>> ListEnabledAsync(CancellationToken cancellationToken = default) =>
        await Set.Where(p => p.Enabled).OrderBy(p => p.UserId).ThenBy(p => p.Channel).ToListAsync(cancellationToken);
}

internal sealed class OutboxRepository(AppDbContext context) : EfRepository<OutboxMessage>(context), IOutboxRepository
{
    public async Task<IReadOnlyList<OutboxMessage>> GetUnprocessedAsync(int batchSize, CancellationToken cancellationToken = default) =>
        await Set.Where(m => m.ProcessedAt == null && m.Attempts < OutboxMessage.MaxAttempts)
            .OrderBy(m => m.OccurredAt)
            .Take(batchSize)
            .ToListAsync(cancellationToken);
}

internal sealed class MunicipalityRepository(AppDbContext context) : EfRepository<Municipality>(context), IMunicipalityRepository
{
    public async Task<IReadOnlyList<Municipality>> ListAsync(CancellationToken cancellationToken = default) =>
        await Set.OrderBy(m => m.Name).ToListAsync(cancellationToken);

    public Task<int> CountAsync(CancellationToken cancellationToken = default) => Set.CountAsync(cancellationToken);
}
