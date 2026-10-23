using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using TenderSync.Application.Abstractions;
using TenderSync.Domain.Common;
using TenderSync.Domain.Notifications;

namespace TenderSync.Infrastructure.Persistence.Interceptors;

/// <summary>
/// On every save: converts domain events raised by tracked entities into outbox rows (same transaction, ADR-005)
/// and stamps created_at / updated_at on added and modified entities.
/// </summary>
public sealed class AuditAndOutboxInterceptor(IDateTimeProvider clock) : SaveChangesInterceptor
{
    public override InterceptionResult<int> SavingChanges(DbContextEventData eventData, InterceptionResult<int> result)
    {
        Apply(eventData.Context);
        return base.SavingChanges(eventData, result);
    }

    public override ValueTask<InterceptionResult<int>> SavingChangesAsync(DbContextEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default)
    {
        Apply(eventData.Context);
        return base.SavingChangesAsync(eventData, result, cancellationToken);
    }

    private void Apply(DbContext? context)
    {
        if (context is null)
        {
            return;
        }

        var now = clock.UtcNow;

        var entitiesWithEvents = context.ChangeTracker.Entries<Entity>()
            .Select(e => e.Entity)
            .Where(e => e.DomainEvents.Count > 0)
            .ToList();

        foreach (var entity in entitiesWithEvents)
        {
            var messages = entity.DomainEvents.Select(OutboxMessage.FromEvent).ToList();
            entity.ClearDomainEvents();
            context.Set<OutboxMessage>().AddRange(messages);
        }

        foreach (var entry in context.ChangeTracker.Entries<Entity>())
        {
            if (entry.State is EntityState.Added or EntityState.Modified)
            {
                entry.Entity.Touch(now);
            }
        }
    }
}
