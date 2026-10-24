using System.Reflection;
using System.Text.Json;
using Json.Schema;

namespace TenderSync.Infrastructure.ETenders;

/// <summary>
/// Drift detection for the API (ADR-014): every page is checked against the JSON Schema generated from the verified samples.
/// A missing field or a changed type is reported as drift, recorded in source_health and raised as PipelineFailure.
/// </summary>
public sealed class ReleaseSchemaValidator
{
    public const string ResourceName = "TenderSync.Infrastructure.ETenders.ocds-release-package.schema.json";
    private const int MaxReportedErrors = 50;

    // Loaded once per process: the schema declares an $id and JsonSchema.Net refuses to register the same id twice.
    private static readonly Lazy<JsonSchema> Schema = new(Load, LazyThreadSafetyMode.ExecutionAndPublication);

    private readonly EvaluationOptions _options = new() { OutputFormat = OutputFormat.List };

    public IReadOnlyList<string> Validate(string pageJson)
    {
        JsonDocument document;
        try
        {
            document = JsonDocument.Parse(pageJson);
        }
        catch (JsonException ex)
        {
            return [$"Response is not valid JSON: {ex.Message}"];
        }

        using (document)
        {
            var result = Schema.Value.Evaluate(document.RootElement, _options);
            if (result.IsValid)
            {
                return [];
            }

            return Flatten(result)
                .Where(r => r.Errors is { Count: > 0 })
                .SelectMany(r => r.Errors!.Select(e => $"{r.InstanceLocation}: {e.Key} {e.Value}"))
                .Distinct(StringComparer.Ordinal)
                .Take(MaxReportedErrors)
                .ToList();
        }
    }

    private static IEnumerable<EvaluationResults> Flatten(EvaluationResults root)
    {
        yield return root;
        foreach (var detail in root.Details ?? [])
        {
            foreach (var nested in Flatten(detail))
            {
                yield return nested;
            }
        }
    }

    private static JsonSchema Load()
    {
        using var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream(ResourceName)
            ?? throw new InvalidOperationException($"Embedded resource {ResourceName} not found.");
        using var reader = new StreamReader(stream);
        return JsonSchema.FromText(reader.ReadToEnd());
    }
}
