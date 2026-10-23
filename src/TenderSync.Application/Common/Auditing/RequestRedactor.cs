using System.Collections.Concurrent;
using System.Reflection;
using System.Text.Json;
using TenderSync.Domain.Common;

namespace TenderSync.Application.Common.Auditing;

/// <summary>Serialises a request for logs and audit rows with [Sensitive] properties replaced by a placeholder.</summary>
public static class RequestRedactor
{
    public const string Redacted = "***";

    private static readonly ConcurrentDictionary<Type, PropertyInfo[]> Cache = new();

    public static string ToJson(object request)
    {
        ArgumentNullException.ThrowIfNull(request);

        var type = request.GetType();
        var properties = Cache.GetOrAdd(type, t => t.GetProperties(BindingFlags.Public | BindingFlags.Instance).Where(p => p.CanRead && p.GetIndexParameters().Length == 0).ToArray());

        var map = new Dictionary<string, object?>(StringComparer.Ordinal);
        foreach (var property in properties)
        {
            var isSensitive = property.GetCustomAttribute<SensitiveAttribute>() is not null;
            map[JsonNamingPolicy.CamelCase.ConvertName(property.Name)] = isSensitive ? Redacted : property.GetValue(request);
        }

        return DomainJson.Serialize(map);
    }
}
