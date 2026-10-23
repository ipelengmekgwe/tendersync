using MediatR;
using TenderSync.Domain.Common;

namespace TenderSync.Application.Common.Events;

/// <summary>
/// MediatR wrapper for a domain event taken from the outbox. Handlers implement
/// <c>INotificationHandler&lt;DomainEventNotification&lt;TenderChanged&gt;&gt;</c> and never touch the outbox themselves.
/// </summary>
public sealed record DomainEventNotification<TEvent>(TEvent Event) : INotification
    where TEvent : IDomainEvent;

public static class DomainEventNotification
{
    /// <summary>Builds the closed generic notification for an event whose type is only known at runtime.</summary>
    public static INotification Create(IDomainEvent domainEvent)
    {
        ArgumentNullException.ThrowIfNull(domainEvent);
        var notificationType = typeof(DomainEventNotification<>).MakeGenericType(domainEvent.GetType());
        return (INotification)Activator.CreateInstance(notificationType, domainEvent)!;
    }
}
