using System.Diagnostics;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Extensions.Options;
using TenderSync.Application.Abstractions.Llm;
using TenderSync.Application.Features.Llm;

namespace TenderSync.Infrastructure.Llm;

/// <summary>
/// Chat-completions adapter (PLAN.md §8.2) for Azure OpenAI, OpenRouter, Ollama and similar: response_format json_schema,
/// documents sent as extracted text with page markers (no native PDF, so no citations), base URL, key and models from config.
/// </summary>
public sealed class OpenAiCompatibleLlmClient(IHttpClientFactory httpClientFactory, IOptions<OpenAiCompatibleOptions> options, LlmPricing pricing) : ILlmClient
{
    public const string ClientName = "openai-compatible";

    public string Provider => LlmOptions.OpenAiProvider;

    public string ModelFor(ModelTier tier) => tier == ModelTier.Strong ? options.Value.StrongModel : options.Value.FastModel;

    public async Task<LlmResult<T>> CompleteAsync<T>(LlmRequest request, CancellationToken cancellationToken = default)
        where T : class
    {
        ArgumentNullException.ThrowIfNull(request);
        var settings = options.Value;
        if (string.IsNullOrWhiteSpace(settings.BaseUrl))
        {
            throw new LlmException("Llm:OpenAiCompatible:BaseUrl is not configured.");
        }

        var model = ModelFor(request.Tier);
        if (string.IsNullOrWhiteSpace(model))
        {
            throw new LlmException($"Llm:OpenAiCompatible:{(request.Tier == ModelTier.Strong ? "StrongModel" : "FastModel")} is not configured.");
        }

        var body = BuildBody<T>(request, model);
        var client = httpClientFactory.CreateClient(ClientName);
        client.Timeout = TimeSpan.FromSeconds(settings.TimeoutSeconds);
        if (!string.IsNullOrWhiteSpace(settings.ApiKey))
        {
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", settings.ApiKey);
        }

        var url = settings.BaseUrl.TrimEnd('/') + "/chat/completions";
        var watch = Stopwatch.StartNew();
        using var response = await client.PostAsJsonAsync(url, body, LlmJsonSchema.SerializerOptions, cancellationToken);
        var payload = await response.Content.ReadAsStringAsync(cancellationToken);
        watch.Stop();

        if (!response.IsSuccessStatusCode)
        {
            throw new LlmException($"OpenAI-compatible endpoint returned HTTP {(int)response.StatusCode}: {Truncate(payload)}");
        }

        JsonNode root;
        try
        {
            root = JsonNode.Parse(payload) ?? throw new LlmException("Empty chat completion response.");
        }
        catch (JsonException ex)
        {
            throw new LlmException($"Chat completion response is not JSON: {ex.Message}", ex);
        }

        var usageNode = root["usage"];
        var usage = new LlmUsage(
            usageNode?["prompt_tokens"]?.GetValue<int>() ?? 0,
            usageNode?["completion_tokens"]?.GetValue<int>() ?? 0,
            usageNode?["prompt_tokens_details"]?["cached_tokens"]?.GetValue<int>() ?? 0);
        var servedBy = root["model"]?.GetValue<string>() ?? model;
        var cost = pricing.Cost(servedBy, usage);

        var choice = root["choices"]?[0] ?? throw new LlmCallFailedException(servedBy, usage, cost, new LlmOutputException("Chat completion has no choices."));
        var finish = choice["finish_reason"]?.GetValue<string>() ?? string.Empty;
        var message = choice["message"];
        if (message?["refusal"] is { } refusal && refusal.GetValueKind() == JsonValueKind.String)
        {
            throw new LlmCallFailedException(servedBy, usage, cost, new LlmRefusalException($"Model declined the request for {request.Purpose}: {refusal.GetValue<string>()}"));
        }

        if (finish == "content_filter")
        {
            throw new LlmCallFailedException(servedBy, usage, cost, new LlmRefusalException($"Content filter stopped the request for {request.Purpose}."));
        }

        if (finish == "length")
        {
            throw new LlmCallFailedException(servedBy, usage, cost, new LlmOutputException($"Output for {request.Purpose} was cut off at {request.MaxOutputTokens} tokens; raise MaxOutputTokens."));
        }

        var raw = message?["content"]?.GetValue<string>() ?? string.Empty;
        T value;
        try
        {
            value = LlmJsonSchema.Parse<T>(raw);
        }
        catch (LlmOutputException ex)
        {
            throw new LlmCallFailedException(servedBy, usage, cost, ex);
        }

        return new LlmResult<T>(value, raw, [], usage, cost, Provider, servedBy, watch.Elapsed);
    }

    /// <summary>The chat-completions request body; public so contract tests can check it.</summary>
    public JsonObject BuildBody<T>(LlmRequest request, string model)
        where T : class
    {
        ArgumentNullException.ThrowIfNull(request);
        var user = new StringBuilder();
        var documentIndex = 0;
        foreach (var part in request.UserParts)
        {
            switch (part)
            {
                case LlmTextPart text:
                    user.Append(text.Text).Append("\n\n");
                    break;
                case LlmDocumentPart document:
                    var textContent = document.ExtractedText
                        ?? (document.MediaType.StartsWith("text/", StringComparison.OrdinalIgnoreCase) ? Encoding.UTF8.GetString(document.Content.Span) : null)
                        ?? throw new LlmException($"The OpenAI-compatible adapter needs extracted text for document '{document.Title}' ({document.MediaType}); run text extraction first or route this purpose to Anthropic.");
                    user.Append("<document index=\"").Append(documentIndex++).Append("\" title=\"").Append(document.Title.Replace('"', '\'')).Append("\">\n")
                        .Append(textContent).Append("\n</document>\n\n");
                    break;
            }
        }

        var body = new JsonObject
        {
            ["model"] = model,
            ["max_tokens"] = request.MaxOutputTokens,
            ["temperature"] = 0,
            ["messages"] = new JsonArray(
                new JsonObject { ["role"] = "system", ["content"] = request.SystemPrompt },
                new JsonObject { ["role"] = "user", ["content"] = user.ToString().TrimEnd() }),
        };

        body["response_format"] = options.Value.UseJsonSchema
            ? new JsonObject
            {
                ["type"] = "json_schema",
                ["json_schema"] = new JsonObject { ["name"] = typeof(T).Name, ["strict"] = true, ["schema"] = JsonNode.Parse(LlmJsonSchema.For<T>()) },
            }
            : new JsonObject { ["type"] = "json_object" };

        return body;
    }

    private static string Truncate(string text) => text.Length <= 500 ? text : text[..500];
}
