using System.Collections.Concurrent;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Schema;
using System.Text.Json.Serialization;

namespace TenderSync.Application.Features.Llm;

/// <summary>
/// JSON schema for a response type, shaped for structured outputs: every object is closed (additionalProperties false),
/// every property is required, nullable members allow null. Generated once per type from the same serializer options
/// used to read the model's answer, so the schema and the deserialiser never disagree.
/// </summary>
public static class LlmJsonSchema
{
    public static JsonSerializerOptions SerializerOptions { get; } = new(JsonSerializerDefaults.Web)
    {
        TypeInfoResolver = new System.Text.Json.Serialization.Metadata.DefaultJsonTypeInfoResolver(),
        Converters = { new JsonStringEnumConverter() },
        NumberHandling = JsonNumberHandling.AllowReadingFromString,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
    };

    private static readonly ConcurrentDictionary<Type, string> Cache = new();

    private static readonly JsonSchemaExporterOptions ExporterOptions = new()
    {
        TreatNullObliviousAsNonNullable = true,
        TransformSchemaNode = (context, schema) =>
        {
            if (schema is not JsonObject obj)
            {
                return schema;
            }

            if (obj["properties"] is JsonObject properties)
            {
                obj["additionalProperties"] = false;
                obj["required"] = new JsonArray(properties.Select(p => (JsonNode)p.Key).ToArray());
            }

            // Attach the member's description from [Description] so the model sees field meanings.
            if (context.PropertyInfo?.AttributeProvider is { } provider
                && provider.GetCustomAttributes(typeof(System.ComponentModel.DescriptionAttribute), true).FirstOrDefault() is System.ComponentModel.DescriptionAttribute description
                && obj["description"] is null)
            {
                obj["description"] = description.Description;
            }

            return obj;
        },
    };

    /// <summary>Schema JSON text for <typeparamref name="T"/>; cached.</summary>
    public static string For<T>() => For(typeof(T));

    public static string For(Type type) => Cache.GetOrAdd(type, t => SerializerOptions.GetJsonSchemaAsNode(t, ExporterOptions).ToJsonString());

    public static T Parse<T>(string json)
        where T : class
    {
        try
        {
            return JsonSerializer.Deserialize<T>(json, SerializerOptions)
                ?? throw new Abstractions.Llm.LlmOutputException("The model returned JSON null.");
        }
        catch (JsonException ex)
        {
            throw new Abstractions.Llm.LlmOutputException($"The model's answer is not valid {typeof(T).Name} JSON: {ex.Message}", ex);
        }
    }
}
