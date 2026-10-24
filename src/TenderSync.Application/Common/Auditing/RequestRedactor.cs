using System.Collections.Concurrent;
using System.Reflection;
using System.Text.Json;
using TenderSync.Domain.Common;

namespace TenderSync.Application.Common.Auditing;

/// <summary>
/// Serialises a request for logs and audit rows with [Sensitive] properties replaced by a placeholder. Nested input objects
/// (a command wrapping a body record, or a list of them) are walked the same way, so a sensitive field two levels down is
/// still redacted; streams are never serialised.
/// </summary>
public static class RequestRedactor
{
    public const string Redacted = "***";
    private const int MaxDepth = 3;

    private static readonly ConcurrentDictionary<Type, PropertyInfo[]> Cache = new();

    public static string ToJson(object request)
    {
        ArgumentNullException.ThrowIfNull(request);
        return DomainJson.Serialize(Redact(request, 0));
    }

    private static object? Redact(object? value, int depth)
    {
        if (value is null || depth >= MaxDepth)
        {
            return value;
        }

        var type = value.GetType();
        if (value is Stream)
        {
            return Redacted;
        }

        if (type.IsPrimitive || type.IsEnum || value is string || value is decimal || value is DateTimeOffset || value is DateTime || value is Guid || value is TimeSpan || value is System.Collections.IDictionary)
        {
            return value;
        }

        if (value is System.Collections.IEnumerable items)
        {
            // Arrays of primitives (byte[], int[]) stay as they are; lists of inputs are walked item by item.
            if (type.IsArray && (type.GetElementType()?.IsPrimitive ?? false))
            {
                return value;
            }

            var list = new List<object?>();
            foreach (var item in items)
            {
                list.Add(Redact(item, depth + 1));
            }

            return list;
        }

        var properties = Cache.GetOrAdd(type, t => t.GetProperties(BindingFlags.Public | BindingFlags.Instance).Where(p => p.CanRead && p.GetIndexParameters().Length == 0).ToArray());
        var map = new Dictionary<string, object?>(StringComparer.Ordinal);
        foreach (var property in properties)
        {
            var isSensitive = property.GetCustomAttribute<SensitiveAttribute>() is not null;
            map[JsonNamingPolicy.CamelCase.ConvertName(property.Name)] = isSensitive ? Redacted : Redact(property.GetValue(value), depth + 1);
        }

        return map;
    }
}
