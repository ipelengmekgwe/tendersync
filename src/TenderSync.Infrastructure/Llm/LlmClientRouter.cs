using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using TenderSync.Application.Abstractions.Llm;
using TenderSync.Application.Features.Llm;

namespace TenderSync.Infrastructure.Llm;

/// <summary>Resolves the keyed adapter named by Llm:Routing:{purpose} (or Llm:DefaultProvider) and wraps it with the recorder when Llm:RecordingPath is set.</summary>
public sealed class LlmClientRouter(IServiceProvider services, IOptions<LlmOptions> options, ILogger<RecordingLlmClient> recordingLogger) : ILlmClientRouter
{
    public static IReadOnlyList<string> KnownProviders { get; } = [LlmOptions.AnthropicProvider, LlmOptions.OpenAiProvider, LlmOptions.FakeProvider];

    public IReadOnlyCollection<string> Providers => KnownProviders;

    public ILlmClient Resolve(string purpose)
    {
        var provider = options.Value.ProviderFor(purpose);
        var client = services.GetKeyedService<ILlmClient>(provider)
            ?? throw new LlmException($"Llm routing for '{purpose}' names provider '{provider}', which is not registered. Known: {string.Join(", ", KnownProviders)}.");

        var recordingPath = options.Value.RecordingPath;
        return string.IsNullOrWhiteSpace(recordingPath) ? client : new RecordingLlmClient(client, recordingPath, recordingLogger);
    }
}

/// <summary>
/// Writes every request and answer as JSON under {RecordingPath}/{purpose}/ so golden fixtures for the evals can be built
/// from real calls (PLAN.md §8.2). Document bytes are summarised (title, media type, size), not copied.
/// </summary>
public sealed class RecordingLlmClient(ILlmClient inner, string recordingPath, ILogger<RecordingLlmClient> logger) : ILlmClient
{
    private static readonly JsonSerializerOptions Pretty = new(JsonSerializerDefaults.Web) { WriteIndented = true };

    public string Provider => inner.Provider;

    public async Task<LlmResult<T>> CompleteAsync<T>(LlmRequest request, CancellationToken cancellationToken = default)
        where T : class
    {
        ArgumentNullException.ThrowIfNull(request);
        object? response = null;
        string? error = null;
        try
        {
            var result = await inner.CompleteAsync<T>(request, cancellationToken);
            response = new { result.RawText, result.Citations, result.Usage, result.CostUsd, result.Provider, result.Model, durationMs = (int)result.Duration.TotalMilliseconds };
            return result;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            error = ex.Message;
            throw;
        }
        finally
        {
            Write(request, response, error);
        }
    }

    private void Write(LlmRequest request, object? response, string? error)
    {
        try
        {
            var folder = Path.Combine(recordingPath, Sanitise(request.Purpose));
            Directory.CreateDirectory(folder);
            var file = Path.Combine(folder, $"{DateTime.UtcNow:yyyyMMdd-HHmmss-fff}.json");
            var record = new
            {
                request.Purpose,
                request.PromptVersion,
                request.Tier,
                request.Effort,
                request.EnableCitations,
                request.TenderId,
                request.SystemPrompt,
                parts = request.UserParts.Select(RecordedPart.From),
                response,
                error,
            };
            File.WriteAllText(file, JsonSerializer.Serialize(record, Pretty));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            logger.LogWarning(ex, "Could not write LLM recording for {Purpose}", request.Purpose);
        }
    }

    private sealed record RecordedPart(string Type, string? Text, string? Title, string? MediaType, int Bytes, string? ExtractedText)
    {
        public static RecordedPart From(LlmPart part) => part switch
        {
            LlmTextPart t => new("text", t.Text, null, null, 0, null),
            LlmDocumentPart d => new("document", null, d.Title, d.MediaType, d.Content.Length, d.ExtractedText),
            _ => new(part.GetType().Name, null, null, null, 0, null),
        };
    }

    private static string Sanitise(string purpose) => new(purpose.Select(c => char.IsAsciiLetterOrDigit(c) || c is '-' or '_' ? c : '_').ToArray());
}
