using System.Text.Json;
using TenderSync.Domain.Common;

namespace TenderSync.Application.Common.Events;

/// <summary>Resolves the type name stored in outbox_messages back to the Domain event type and deserialises the payload.</summary>
public static class DomainEventTypeRegistry
{
    private static readonly Dictionary<string, Type> Types = typeof(IDomainEvent).Assembly
        .GetTypes()
        .Where(t => !t.IsAbstract && typeof(IDomainEvent).IsAssignableFrom(t) && t.FullName is not null)
        .ToDictionary(t => t.FullName!, t => t, StringComparer.Ordinal);

    public static IReadOnlyCollection<string> KnownTypeNames => Types.Keys;

    public static Type? Resolve(string typeName) => Types.GetValueOrDefault(typeName);

    public static IDomainEvent? Deserialize(string typeName, string payloadJson)
    {
        var type = Resolve(typeName);
        return type is null ? null : JsonSerializer.Deserialize(payloadJson, type, DomainJson.Options) as IDomainEvent;
    }
}
