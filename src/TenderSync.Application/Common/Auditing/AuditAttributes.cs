namespace TenderSync.Application.Common.Auditing;

/// <summary>Excludes a command from the audit log (use sparingly; prefer redacting fields with <see cref="SensitiveAttribute"/>).</summary>
[AttributeUsage(AttributeTargets.Class, AllowMultiple = false, Inherited = true)]
public sealed class SkipAuditAttribute : Attribute;

/// <summary>Redacts a request property in the audit log and in logging (passwords, tokens, keys).</summary>
[AttributeUsage(AttributeTargets.Property, AllowMultiple = false, Inherited = true)]
public sealed class SensitiveAttribute : Attribute;

/// <summary>Names the entity a command acts on, so the audit row can be filtered by entity.</summary>
public interface IAuditedEntityReference
{
    string AuditEntityType { get; }

    string? AuditEntityId { get; }
}
