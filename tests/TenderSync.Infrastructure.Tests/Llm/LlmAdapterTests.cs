using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using TenderSync.Application.Abstractions.Llm;
using TenderSync.Application.Features.Llm;
using TenderSync.Infrastructure.Llm;
using TenderSync.Infrastructure.Tests.Documents;

namespace TenderSync.Infrastructure.Tests.Llm;

/// <summary>Captures the outgoing request and answers with a recorded fixture from docs/research/llm-samples.</summary>
public sealed class FixtureHandler(string fixture, HttpStatusCode status = HttpStatusCode.OK) : HttpMessageHandler
{
    public HttpRequestMessage? Request { get; private set; }

    public string? RequestBody { get; private set; }

    public static string Read(string file) => File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures", "llm", file));

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        Request = request;
        RequestBody = request.Content is null ? null : await request.Content.ReadAsStringAsync(cancellationToken);
        return new HttpResponseMessage(status) { Content = new StringContent(Read(fixture), Encoding.UTF8, "application/json") };
    }
}

public class AnthropicLlmClientTests
{
    private static readonly LlmPricing Pricing = new(Options.Create(new LlmOptions()), NullLogger<LlmPricing>.Instance);

    private static AnthropicLlmClient Client(FixtureHandler handler, Action<AnthropicOptions>? configure = null)
    {
        var options = new AnthropicOptions { ApiKey = "sk-test" };
        configure?.Invoke(options);
        return new AnthropicLlmClient(Options.Create(options), Pricing, NullLogger<AnthropicLlmClient>.Instance, new HttpClient(handler));
    }

    private static LlmRequest PdfRequest(bool citations = true) => new(
        "smoke", "v1", "You are a procurement analyst.",
        [new LlmDocumentPart(SampleFiles.Pdf("Invitation to bid", "Closing date: 20 November 2026 at 11:00"), "application/pdf", "Bid Document"), new LlmTextPart("Summarise the document.")],
        ModelTier.Fast, LlmEffort.Low, 2048, EnableCitations: citations);

    [Fact]
    public async Task Sends_structured_output_pdf_document_citations_cache_control_and_fallbacks()
    {
        var handler = new FixtureHandler("anthropic-message-with-citations.json");

        var result = await Client(handler).CompleteAsync<SmokeSummary>(PdfRequest());

        handler.Request!.RequestUri!.AbsolutePath.Should().EndWith("/v1/messages");
        var allHeaders = string.Join("; ", handler.Request.Headers.Select(h => h.Key + "=" + string.Join(",", h.Value)));
        // SDK 13.x sends the key as a bearer token; older versions used x-api-key. Either must carry the configured key.
        var apiKeyHeader = handler.Request.Headers.TryGetValues("x-api-key", out var keys) ? string.Join(",", keys) : handler.Request.Headers.Authorization?.Parameter;
        apiKeyHeader.Should().Be("sk-test", "headers were: {0}", allHeaders);
        handler.Request.Headers.GetValues("anthropic-version").Should().ContainSingle();
        (handler.Request.Headers.TryGetValues("anthropic-beta", out var betaValues) ? string.Join(",", betaValues) : string.Empty).Should().Contain("server-side-fallback", "headers were: {0}", allHeaders);

        var body = JsonNode.Parse(handler.RequestBody!)!.AsObject();
        body["model"]!.ToString().Should().Be("claude-haiku-5-5");
        body["max_tokens"]!.GetValue<int>().Should().Be(2048);
        body["thinking"]!["type"]!.ToString().Should().Be("adaptive");
        body["fallbacks"]!.ToString().Should().Be("default");
        body["system"]![0]!["text"]!.ToString().Should().Be("You are a procurement analyst.");
        body["system"]![0]!["cache_control"]!["type"]!.ToString().Should().Be("ephemeral", "the system prompt is the cached prefix");
        body["output_config"]!["effort"]!.ToString().Should().Be("low");
        var format = body["output_config"]!["format"]!;
        format["type"]!.ToString().Should().Be("json_schema");
        format["schema"]!["additionalProperties"]!.GetValue<bool>().Should().BeFalse();
        format["schema"]!["required"]!.AsArray().Select(n => n!.ToString()).Should().Contain(["summary", "bidNumber", "closingDate", "closingDatePage"]);

        var content = body["messages"]![0]!["content"]!.AsArray();
        body["messages"]![0]!["role"]!.ToString().Should().Be("user");
        content.Should().HaveCount(2);
        content[0]!["type"]!.ToString().Should().Be("document");
        content[0]!["title"]!.ToString().Should().Be("Bid Document");
        content[0]!["citations"]!["enabled"]!.GetValue<bool>().Should().BeTrue();
        content[0]!["source"]!["type"]!.ToString().Should().Be("base64");
        content[0]!["source"]!["media_type"]!.ToString().Should().Be("application/pdf");
        Convert.FromBase64String(content[0]!["source"]!["data"]!.ToString()).Take(4).Should().Equal("%PDF"u8.ToArray());
        content[1]!["type"]!.ToString().Should().Be("text");
        body.ContainsKey("messages").Should().BeTrue();
        body["messages"]!.AsArray().Should().HaveCount(1, "the assistant turn is never prefilled");

        result.Value.BidNumber.Should().Be("WCGHCC0038/1/2025");
        result.Value.ClosingDatePage.Should().Be(2);
        result.Citations.Should().ContainSingle().Which.Should().Be(new LlmCitation(0, 2, 2, "Closing date: 20 November 2026 at 11:00"));
        result.Usage.Should().Be(new LlmUsage(2345, 87, 1200));
        result.CostUsd.Should().Be(0.000290m, "2345×0.10 + 87×0.50 + 1200×0.01 per MTok");
        result.Provider.Should().Be("anthropic");
        result.Model.Should().Be("claude-haiku-5-5");
        result.Duration.Should().BeGreaterThan(TimeSpan.Zero);
    }

    [Fact]
    public async Task A_refusal_surfaces_as_LlmRefusalException_with_the_usage_for_billing()
    {
        var handler = new FixtureHandler("anthropic-refusal.json");

        var act = () => Client(handler).CompleteAsync<SmokeSummary>(PdfRequest());

        var failure = (await act.Should().ThrowAsync<LlmCallFailedException>()).Which;
        failure.InnerException.Should().BeOfType<LlmRefusalException>().Which.Message.Should().Contain("safety classifier");
        failure.Usage.InputTokens.Should().Be(512);
        failure.Model.Should().Be("claude-haiku-5-5");
    }

    [Fact]
    public async Task Fallbacks_and_cache_control_can_be_turned_off_and_strong_tier_uses_the_strong_model()
    {
        var handler = new FixtureHandler("anthropic-message-with-citations.json");

        await Client(handler, o => { o.Fallbacks = string.Empty; o.CacheSystemPrompt = false; o.StrongModel = "claude-opus-5-5"; })
            .CompleteAsync<SmokeSummary>(PdfRequest() with { Tier = ModelTier.Strong, Effort = LlmEffort.High });

        var body = JsonNode.Parse(handler.RequestBody!)!.AsObject();
        body["model"]!.ToString().Should().Be("claude-opus-5-5");
        body.ContainsKey("fallbacks").Should().BeFalse();
        body["system"]![0]!.AsObject().ContainsKey("cache_control").Should().BeFalse();
        body["output_config"]!["effort"]!.ToString().Should().Be("high");
        (handler.Request!.Headers.TryGetValues("anthropic-beta", out var betas) ? string.Join(",", betas) : string.Empty).Should().NotContain("server-side-fallback");
    }

    [Fact]
    public async Task Non_pdf_documents_go_as_plain_text_sources()
    {
        var handler = new FixtureHandler("anthropic-message-with-citations.json");
        var request = new LlmRequest("smoke", "v1", "sys", [new LlmDocumentPart(Encoding.UTF8.GetBytes("ignored"), "application/vnd.openxmlformats-officedocument.wordprocessingml.document", "Pricing.docx", ExtractedText: "Item\tPrice\nLaptop\t15000")]);

        await Client(handler).CompleteAsync<SmokeSummary>(request);

        var source = JsonNode.Parse(handler.RequestBody!)!["messages"]![0]!["content"]![0]!["source"]!;
        source["type"]!.ToString().Should().Be("text");
        source["media_type"]!.ToString().Should().Be("text/plain");
        source["data"]!.ToString().Should().Contain("Laptop");
    }

    [Fact]
    public void A_missing_key_fails_at_first_use_with_a_clear_message()
    {
        var client = new AnthropicLlmClient(Options.Create(new AnthropicOptions()), Pricing, NullLogger<AnthropicLlmClient>.Instance);

        var act = () => client.CompleteAsync<SmokeSummary>(PdfRequest());

        act.Should().ThrowAsync<LlmException>().WithMessage("*Llm:Anthropic:ApiKey*");
    }
}

public class OpenAiCompatibleLlmClientTests
{
    private static OpenAiCompatibleLlmClient Client(FixtureHandler handler, Action<OpenAiCompatibleOptions>? configure = null)
    {
        var options = new OpenAiCompatibleOptions { BaseUrl = "https://llm.example.org/v1/", ApiKey = "key-1", FastModel = "gpt-4.1-mini", StrongModel = "gpt-4.1" };
        configure?.Invoke(options);
        var services = new ServiceCollection();
        services.AddHttpClient(OpenAiCompatibleLlmClient.ClientName).ConfigurePrimaryHttpMessageHandler(() => handler);
        var provider = services.BuildServiceProvider();
        var pricing = new LlmPricing(Options.Create(new LlmOptions { Pricing = { ["gpt-4.1-mini-2025-04-14"] = new ModelPrice(0.4m, 1.6m, 0.1m) } }), NullLogger<LlmPricing>.Instance);
        return new OpenAiCompatibleLlmClient(provider.GetRequiredService<IHttpClientFactory>(), Options.Create(options), pricing);
    }

    [Fact]
    public async Task Sends_json_schema_response_format_and_documents_as_tagged_text()
    {
        var handler = new FixtureHandler("openai-chat-completion.json");
        var request = new LlmRequest("smoke", "v1", "You are an analyst.", [
            new LlmDocumentPart(new byte[] { 1, 2, 3 }, "application/pdf", "Bid Document", ExtractedText: "[page 1]\nClosing date 14 November 2026"),
            new LlmTextPart("Summarise."),
        ]);

        var result = await Client(handler).CompleteAsync<SmokeSummary>(request);

        handler.Request!.RequestUri!.ToString().Should().Be("https://llm.example.org/v1/chat/completions");
        handler.Request.Headers.Authorization!.Parameter.Should().Be("key-1");
        var body = JsonNode.Parse(handler.RequestBody!)!.AsObject();
        body["model"]!.ToString().Should().Be("gpt-4.1-mini");
        body["response_format"]!["type"]!.ToString().Should().Be("json_schema");
        body["response_format"]!["json_schema"]!["strict"]!.GetValue<bool>().Should().BeTrue();
        body["response_format"]!["json_schema"]!["schema"]!["additionalProperties"]!.GetValue<bool>().Should().BeFalse();
        body["messages"]![0]!["role"]!.ToString().Should().Be("system");
        body["messages"]![1]!["content"]!.ToString().Should().Contain("<document index=\"0\" title=\"Bid Document\">").And.Contain("Closing date 14 November 2026").And.Contain("Summarise.");

        result.Value.BidNumber.Should().Be("ETVET 04/2026");
        result.Citations.Should().BeEmpty("chat completions carry no citations");
        result.Usage.Should().Be(new LlmUsage(1800, 60, 1024));
        result.Model.Should().Be("gpt-4.1-mini-2025-04-14");
        result.CostUsd.Should().Be(0.000918m, "1800×0.4 + 60×1.6 + 1024×0.1 per MTok");
    }

    [Fact]
    public async Task A_pdf_without_extracted_text_is_rejected_before_any_request()
    {
        var handler = new FixtureHandler("openai-chat-completion.json");
        var request = new LlmRequest("smoke", "v1", "sys", [new LlmDocumentPart(new byte[] { 1 }, "application/pdf", "Scan.pdf")]);

        await FluentActions.Awaiting(() => Client(handler).CompleteAsync<SmokeSummary>(request)).Should().ThrowAsync<LlmException>().WithMessage("*extracted text*");
        handler.Request.Should().BeNull();
    }

    [Fact]
    public async Task Http_errors_and_missing_configuration_are_reported()
    {
        var handler = new FixtureHandler("openai-chat-completion.json", HttpStatusCode.BadGateway);
        var request = new LlmRequest("smoke", "v1", "sys", [new LlmTextPart("x")]);

        await FluentActions.Awaiting(() => Client(handler).CompleteAsync<SmokeSummary>(request)).Should().ThrowAsync<LlmException>().WithMessage("*502*");
        await FluentActions.Awaiting(() => Client(handler, o => o.BaseUrl = string.Empty).CompleteAsync<SmokeSummary>(request)).Should().ThrowAsync<LlmException>().WithMessage("*BaseUrl*");
        await FluentActions.Awaiting(() => Client(handler, o => o.StrongModel = string.Empty).CompleteAsync<SmokeSummary>(request with { Tier = ModelTier.Strong })).Should().ThrowAsync<LlmException>().WithMessage("*StrongModel*");
    }
}

public class LlmClientRouterTests
{
    private static ServiceProvider Build(Dictionary<string, string?> settings)
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(settings).Build();
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddLlm(configuration);
        return services.BuildServiceProvider();
    }

    [Fact]
    public void Routes_by_purpose_with_the_default_for_the_rest()
    {
        using var provider = Build(new Dictionary<string, string?>
        {
            ["Llm:DefaultProvider"] = "anthropic",
            ["Llm:Routing:classify"] = "fake",
            ["Llm:Routing:extract-requirements"] = "OpenAI",
        });
        var router = provider.GetRequiredService<ILlmClientRouter>();

        router.Resolve("classify").Should().BeOfType<FakeLlmClient>();
        router.Resolve("extract-requirements").Should().BeOfType<OpenAiCompatibleLlmClient>();
        router.Resolve("qualify").Should().BeOfType<AnthropicLlmClient>();
        router.Providers.Should().BeEquivalentTo(["anthropic", "openai", "fake"]);
    }

    [Fact]
    public void An_unknown_provider_name_is_an_error_and_recording_wraps_the_adapter()
    {
        using var provider = Build(new Dictionary<string, string?> { ["Llm:DefaultProvider"] = "bedrock" });
        FluentActions.Invoking(() => provider.GetRequiredService<ILlmClientRouter>().Resolve("x")).Should().Throw<LlmException>().WithMessage("*bedrock*");

        var path = Path.Combine(Path.GetTempPath(), "tendersync-llm-recordings", Guid.NewGuid().ToString("N"));
        using var recording = Build(new Dictionary<string, string?> { ["Llm:DefaultProvider"] = "fake", ["Llm:RecordingPath"] = path });
        recording.GetRequiredService<ILlmClientRouter>().Resolve("x").Should().BeOfType<RecordingLlmClient>();
    }

    [Fact]
    public async Task The_recording_client_writes_one_json_file_per_call()
    {
        var path = Path.Combine(Path.GetTempPath(), "tendersync-llm-recordings", Guid.NewGuid().ToString("N"));
        var fake = new FakeLlmClient().Answer("classify", new SmokeSummary("s", null, null, null));
        var client = new RecordingLlmClient(fake, path, NullLogger<RecordingLlmClient>.Instance);
        try
        {
            await client.CompleteAsync<SmokeSummary>(new LlmRequest("classify", "v1", "sys", [new LlmTextPart("hello"), new LlmDocumentPart(new byte[] { 1, 2 }, "application/pdf", "Doc")]));

            var file = Directory.GetFiles(Path.Combine(path, "classify")).Should().ContainSingle().Subject;
            var record = JsonNode.Parse(File.ReadAllText(file))!;
            record["purpose"]!.ToString().Should().Be("classify");
            record["parts"]![1]!["bytes"]!.GetValue<int>().Should().Be(2);
            record["response"]!["rawText"]!.ToString().Should().Contain("\"summary\"");
        }
        finally
        {
            Directory.Delete(path, recursive: true);
        }
    }
}
