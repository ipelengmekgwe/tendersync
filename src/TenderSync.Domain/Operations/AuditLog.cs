using TenderSync.Domain.Common;

namespace TenderSync.Domain.Operations;

/// <summary>Who did what to which entity. Written by the audit pipeline behaviour for every command (PLAN.md §12).</summary>
public sealed class AuditLog : Entity
{
    private AuditLog()
    {
    }

    public Guid? ActorUserId { get; private set; }

    public string ActorName { get; private set; } = null!;

    public string Action { get; private set; } = null!;

    public string EntityType { get; private set; } = null!;

    public string? EntityId { get; private set; }

    public string? BeforeJson { get; private set; }

    public string? AfterJson { get; private set; }

    public string? Ip { get; private set; }

    public DateTimeOffset At { get; private set; }

    public static AuditLog Record(Guid? actorUserId, string actorName, string action, string entityType, string? entityId, string? beforeJson, string? afterJson, string? ip, DateTimeOffset at)
    {
        return new AuditLog
        {
            ActorUserId = actorUserId,
            ActorName = Guard.NotEmpty(actorName, nameof(actorName)),
            Action = Guard.NotEmpty(action, nameof(action)),
            EntityType = Guard.NotEmpty(entityType, nameof(entityType)),
            EntityId = Guard.Trimmed(entityId),
            BeforeJson = Guard.Trimmed(beforeJson),
            AfterJson = Guard.Trimmed(afterJson),
            Ip = Guard.Trimmed(ip),
            At = at,
        };
    }
}
