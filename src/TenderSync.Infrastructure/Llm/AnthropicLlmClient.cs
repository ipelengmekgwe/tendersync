using System.Diagnostics;
using System.Text;
using System.Text.Json;
using Anthropic;
using Anthropic.Exceptions;
using Anthropic.Models.Beta;
using Anthropic.Models.Beta.Messages;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using TenderSync.Application.Abstractions.Llm;
using TenderSync.Application.Features.Llm;

namespace TenderSync.Infrastructure.Llm;

/// <summary>
/// Anthropic adapter over the official SDK (PLAN.md §8.2): structured outputs through output_config.format, PDFs as
/// native document blocks with citations, adaptive thinking with the request's effort, server-side refusal fallbacks,
/// and the system prompt as the cached prefix. Never prefills the assistant turn.
/// </summary>
public sealed class AnthropicLlmClient : ILlmClient
{
    private readonly AnthropicOptions _options;
    private readonly LlmPricing _pricing;
    private readonly ILogger<AnthropicLlmClient> _logger;
    private readonly Lazy<AnthropicClient> _client;

    public AnthropicLlmClient(IOptions<AnthropicOptions> options, LlmPricing pricing, ILogger<AnthropicLlmClient> logger, HttpClient? httpClient = null)
    {
        _options = options.Value;
        _pricing = pricing;
        _logger = logger;
        _client = new Lazy<AnthropicClient>(() => CreateClient(_options, httpClient));
    }

    public string Provider => LlmOptions.AnthropicProvider;

    public string ModelFor(ModelTier tier) => tier == ModelTier.Strong ? _options.StrongModel : _options.FastModel;

    public async Task<LlmResult<T>> CompleteAsync<T>(LlmRequest request, CancellationToken cancellationToken = default)
        where T : class
    {
        ArgumentNullException.ThrowIfNull(request);
        var model = ModelFor(request.Tier);
        var parameters = BuildParameters<T>(request, model);
        var watch = Stopwatch.StartNew();

        BetaMessage response;
        try
        {
            response = await _client.Value.Beta.Messages.Create(parameters, cancellationToken);
        }
        catch (AnthropicException ex)
        {
            throw new LlmException($"Anthropic request failed: {ex.Message}", ex);
        }

        watch.Stop();
        var usage = new LlmUsage((int)response.Usage.InputTokens, (int)response.Usage.OutputTokens, (int)(response.Usage.CacheReadInputTokens ?? 0));
        var servedBy = RawString(response, "model") ?? model;
        var cost = _pricing.Cost(servedBy, usage, (int)(response.Usage.CacheCreationInputTokens ?? 0));
        var stopReason = RawString(response, "stop_reason") ?? string.Empty;

        if (stopReason == "refusal")
        {
            var details = response.StopDetails;
            var detail = details is null ? "no details" : $"{details.Category} {details.Explanation}".Trim();
            throw new LlmCallFailedException(servedBy, usage, cost, new LlmRefusalException($"Anthropic declined the request for {request.Purpose}: {detail}."));
        }

        if (stopReason == "max_tokens")
        {
            throw new LlmCallFailedException(servedBy, usage, cost, new LlmOutputException($"Output for {request.Purpose} was cut off at {request.MaxOutputTokens} tokens; raise MaxOutputTokens."));
        }

        var text = new StringBuilder();
        var citations = new List<LlmCitation>();
        foreach (var block in response.Content)
        {
            if (!block.TryPickText(out var textBlock) || textBlock is null)
            {
                continue;
            }

            text.Append(textBlock.Text);
            foreach (var citation in textBlock.Citations ?? [])
            {
                if (citation.TryPickCitationPageLocation(out var page) && page is not null)
                {
                    // end_page_number is exclusive in the API; store the inclusive last page.
                    citations.Add(new LlmCitation((int)page.DocumentIndex, (int)page.StartPageNumber, (int)Math.Max(page.StartPageNumber, page.EndPageNumber - 1), page.CitedText));
                }
                else if (citation.TryPickCitationCharLocation(out var chars) && chars is not null)
                {
                    citations.Add(new LlmCitation((int)chars.DocumentIndex, null, null, chars.CitedText));
                }
            }
        }

        var raw = text.ToString();
        T value;
        try
        {
            value = LlmJsonSchema.Parse<T>(raw);
        }
        catch (LlmOutputException ex)
        {
            throw new LlmCallFailedException(servedBy, usage, cost, ex);
        }

        if (!string.Equals(servedBy, model, StringComparison.OrdinalIgnoreCase))
        {
            _logger.LogInformation("Anthropic served {Purpose} with fallback model {Served} instead of {Requested}", request.Purpose, servedBy, model);
        }

        return new LlmResult<T>(value, raw, citations, usage, cost, Provider, servedBy, watch.Elapsed);
    }

    /// <summary>The request as the SDK sends it; public so contract tests can inspect the shape without a network call.</summary>
    public MessageCreateParams BuildParameters<T>(LlmRequest request, string model)
        where T : class
    {
        ArgumentNullException.ThrowIfNull(request);
        var content = new List<BetaContentBlockParam>();
        foreach (var part in request.UserParts)
        {
            switch (part)
            {
                case LlmTextPart text:
                    content.Add(new BetaTextBlockParam(text.Text));
                    break;
                case LlmDocumentPart document when document.MediaType.Equals("application/pdf", StringComparison.OrdinalIgnoreCase):
                    content.Add(new BetaRequestDocumentBlock
                    {
                        Source = new BetaBase64PdfSource(Convert.ToBase64String(document.Content.Span)),
                        Title = document.Title,
                        Citations = new BetaCitationsConfigParam { Enabled = request.EnableCitations },
                    });
                    break;
                case LlmDocumentPart document:
                    content.Add(new BetaRequestDocumentBlock
                    {
                        Source = new BetaPlainTextSource(document.ExtractedText ?? Encoding.UTF8.GetString(document.Content.Span)),
                        Title = document.Title,
                        Citations = new BetaCitationsConfigParam { Enabled = request.EnableCitations },
                    });
                    break;
            }
        }

        var system = _options.CacheSystemPrompt
            ? new BetaTextBlockParam { Text = request.SystemPrompt, CacheControl = new BetaCacheControlEphemeral() }
            : new BetaTextBlockParam(request.SystemPrompt);

        var useFallbacks = string.Equals(_options.Fallbacks, "default", StringComparison.OrdinalIgnoreCase);
        var schema = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(LlmJsonSchema.For<T>())!;

        return useFallbacks
            ? new MessageCreateParams
            {
                Model = model,
                MaxTokens = request.MaxOutputTokens,
                System = new List<BetaTextBlockParam> { system },
                Messages = [new BetaUserMessageParam(content)],
                Thinking = new BetaThinkingConfigAdaptive(),
                OutputConfig = new BetaOutputConfig { Effort = MapEffort(request.Effort), Format = new BetaJsonOutputFormat { Schema = schema } },
                Fallbacks = new Default(),
                Betas = [AnthropicBeta.ServerSideFallback2026_07_01],
            }
            : new MessageCreateParams
            {
                Model = model,
                MaxTokens = request.MaxOutputTokens,
                System = new List<BetaTextBlockParam> { system },
                Messages = [new BetaUserMessageParam(content)],
                Thinking = new BetaThinkingConfigAdaptive(),
                OutputConfig = new BetaOutputConfig { Effort = MapEffort(request.Effort), Format = new BetaJsonOutputFormat { Schema = schema } },
            };
    }

    private static Effort MapEffort(LlmEffort effort) => effort switch
    {
        LlmEffort.Low => Effort.Low,
        LlmEffort.High => Effort.High,
        _ => Effort.Medium,
    };

    private static string? RawString(BetaMessage response, string field) =>
        response.RawData.TryGetValue(field, out var element) && element.ValueKind == JsonValueKind.String ? element.GetString() : null;

    private static AnthropicClient CreateClient(AnthropicOptions options, HttpClient? httpClient)
    {
        if (string.IsNullOrWhiteSpace(options.ApiKey) && httpClient is null)
        {
            throw new LlmException("Llm:Anthropic:ApiKey is not configured. Set it in user-secrets or the environment, or route purposes to another provider.");
        }

        var apiKey = string.IsNullOrWhiteSpace(options.ApiKey) ? "test-key" : options.ApiKey;
        var timeout = TimeSpan.FromSeconds(options.TimeoutSeconds);
        var baseUrl = string.IsNullOrWhiteSpace(options.BaseUrl) ? null : options.BaseUrl;

        if (httpClient is not null)
        {
            return baseUrl is null
                ? new AnthropicClient { ApiKey = apiKey, MaxRetries = options.MaxRetries, Timeout = timeout, HttpClient = httpClient }
                : new AnthropicClient { ApiKey = apiKey, MaxRetries = options.MaxRetries, Timeout = timeout, HttpClient = httpClient, BaseUrl = baseUrl };
        }

        return baseUrl is null
            ? new AnthropicClient { ApiKey = apiKey, MaxRetries = options.MaxRetries, Timeout = timeout }
            : new AnthropicClient { ApiKey = apiKey, MaxRetries = options.MaxRetries, Timeout = timeout, BaseUrl = baseUrl };
    }
}
