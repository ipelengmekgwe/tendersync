using System.Text.Json;
using System.Text.Json.Serialization;

namespace TenderSync.Domain.Common;

/// <summary>Serialisation settings for JSON stored in jsonb columns (camelCase, enums as strings, no nulls).</summary>
public static class DomainJson
{
    public static JsonSerializerOptions Options { get; } = new(JsonSerializerDefaults.Web)
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Converters = { new JsonStringEnumConverter() },
        WriteIndented = false,
    };

    public static string Serialize<T>(T value) => JsonSerializer.Serialize(value, Options);

    public static T? Deserialize<T>(string? json) =>
        string.IsNullOrWhiteSpace(json) ? default : JsonSerializer.Deserialize<T>(json, Options);
}
