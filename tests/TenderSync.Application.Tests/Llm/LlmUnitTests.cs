using System.ComponentModel;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using TenderSync.Application.Abstractions.Llm;
using TenderSync.Application.Features.Llm;

namespace TenderSync.Application.Tests.Llm;

public class LlmJsonSchemaTests
{
    private enum Verdict
    {
        Qualify,
        NotQualify,
    }

    private sealed record Finding([property: Description("Where it was found")] string Source, int Page);

    private sealed record Answer(string Title, Verdict Verdict, int? Score, IReadOnlyList<Finding> Findings, string? Note);

    [Fact]
    public void Objects_are_closed_every_property_is_required_and_nullable_members_allow_null()
    {
        var schema = JsonNode.Parse(LlmJsonSchema.For<Answer>())!.AsObject();

        schema["type"]!.ToString().Should().Be("object");
        schema["additionalProperties"]!.GetValue<bool>().Should().BeFalse();
        schema["required"]!.AsArray().Select(n => n!.ToString()).Should().BeEquivalentTo(["title", "verdict", "score", "findings", "note"]);
        schema["properties"]!["verdict"]!["enum"]!.AsArray().Select(n => n!.ToString()).Should().Equal("Qualify", "NotQualify");
        schema["properties"]!["score"]!["type"]!.AsArray().Select(n => n!.ToString()).Should().Contain("null");
        schema["properties"]!["title"]!["type"]!.ToString().Should().Be("string", "non-nullable reference members are required and not null");

        var finding = schema["properties"]!["findings"]!["items"]!.AsObject();
        finding["additionalProperties"]!.GetValue<bool>().Should().BeFalse();
        finding["properties"]!["source"]!["description"]!.ToString().Should().Be("Where it was found");
    }

    [Fact]
    public void Parse_uses_the_same_options_as_the_schema()
    {
        var answer = LlmJsonSchema.Parse<Answer>("""{"title":"x","verdict":"NotQualify","score":"42","findings":[{"source":"SBD 1","page":3}],"note":null}""");

        answer.Verdict.Should().Be(Verdict.NotQualify);
        answer.Score.Should().Be(42, "numbers may arrive as strings");
        answer.Findings.Should().ContainSingle().Which.Page.Should().Be(3);

        FluentActions.Invoking(() => LlmJsonSchema.Parse<Answer>("not json")).Should().Throw<LlmOutputException>();
        FluentActions.Invoking(() => LlmJsonSchema.Parse<Answer>("null")).Should().Throw<LlmOutputException>();
    }
}

public class PromptRegistryTests
{
    [Fact]
    public void Parses_the_header_and_body()
    {
        var template = PromptRegistry.Parse("classify", 2, "---\npurpose: classify\ntier: strong\nschema: Classification\neval: classification.jsonl\n---\n\nYou are an analyst.\n\nScore it.\n");

        template.Should().BeEquivalentTo(new PromptTemplate("classify", 2, ModelTier.Strong, "Classification", "classification.jsonl", "You are an analyst.\n\nScore it."));
        template.VersionTag.Should().Be("v2");
    }

    [Fact]
    public void A_file_without_a_header_is_a_fast_prompt_with_the_whole_text_as_body()
    {
        PromptRegistry.Parse("x", 1, "Just the prompt").Should().Be(new PromptTemplate("x", 1, ModelTier.Fast, null, null, "Just the prompt"));
    }

    [Fact]
    public void The_folder_name_must_match_the_declared_purpose()
    {
        FluentActions.Invoking(() => PromptRegistry.Parse("classify", 1, "---\npurpose: extract\n---\nbody")).Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void Loads_versions_from_disk_and_returns_the_newest_by_default()
    {
        var root = Path.Combine(Path.GetTempPath(), "tendersync-prompts", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(root, "classify"));
        File.WriteAllText(Path.Combine(root, "classify", "v1.md"), "---\ntier: fast\n---\nold");
        File.WriteAllText(Path.Combine(root, "classify", "v10.md"), "---\ntier: strong\n---\nnew");
        File.WriteAllText(Path.Combine(root, "classify", "README.md"), "ignored");
        try
        {
            var registry = new PromptRegistry(root);

            registry.Get("classify").Should().Match<PromptTemplate>(p => p.Version == 10 && p.Body == "new", "numeric order, not string order");
            registry.Get("classify", 1).Body.Should().Be("old");
            registry.List().Should().HaveCount(2);
            FluentActions.Invoking(() => registry.Get("classify", 3)).Should().Throw<InvalidOperationException>().WithMessage("*v1, v10*");
            FluentActions.Invoking(() => registry.Get("missing")).Should().Throw<InvalidOperationException>();
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void The_shipped_smoke_prompt_loads_from_the_output_folder()
    {
        var registry = new PromptRegistry();

        var smoke = registry.Get("smoke");
        smoke.Tier.Should().Be(ModelTier.Fast);
        smoke.SchemaName.Should().Be("SmokeSummary");
        smoke.Body.Should().Contain("closing date");
    }
}

public class LlmPricingTests
{
    private static LlmPricing Pricing(LlmOptions? options = null) => new(Options.Create(options ?? new LlmOptions()), NullLogger<LlmPricing>.Instance);

    [Fact]
    public void Costs_follow_the_price_table_with_cache_reads_and_writes()
    {
        var usage = new LlmUsage(InputTokens: 10_000, OutputTokens: 1_000, CacheReadTokens: 50_000);

        Pricing().Cost("claude-haiku-5-5", usage, cacheWriteTokens: 4_000).Should().Be(0.0025m, "10k×0.10 + 1k×0.50 + 50k×0.01 + 4k×0.125 = $0.0025");
        Pricing().Cost("claude-opus-5-5-20261001", usage).Should().Be(0.07m, "model ids with a date suffix match their family");
    }

    [Fact]
    public void Unknown_models_cost_zero_and_configured_prices_win()
    {
        Pricing().Cost("llama-3", new LlmUsage(1_000_000, 0, 0)).Should().Be(0m);

        var configured = new LlmOptions { Pricing = { ["llama-3"] = new ModelPrice(1m, 2m, 0m), ["claude-haiku-5-5"] = new ModelPrice(0m, 0m, 0m) } };
        Pricing(configured).Cost("llama-3", new LlmUsage(1_000_000, 500_000, 0)).Should().Be(2m);
        Pricing(configured).Cost("claude-haiku-5-5", new LlmUsage(1_000_000, 0, 0)).Should().Be(0m);
    }
}

public class FakeLlmClientTests
{
    private sealed record Classification(string Category, int Relevance);

    [Fact]
    public async Task Returns_canned_answers_by_purpose_and_records_requests()
    {
        var fake = new FakeLlmClient().Answer("classify", new Classification("IT", 90)).AnswerJson("extract", """{"category":"X","relevance":"5"}""");
        var request = new LlmRequest("classify", "v1", "system", [new LlmTextPart("tender text")]);

        var result = await fake.CompleteAsync<Classification>(request);
        result.Value.Should().Be(new Classification("IT", 90));
        result.Provider.Should().Be("fake");
        result.Citations.Should().BeEmpty();
        fake.Requests.Should().ContainSingle().Which.Should().BeSameAs(request);

        (await fake.CompleteAsync<Classification>(request with { Purpose = "extract" })).Value.Relevance.Should().Be(5);
        await FluentActions.Awaiting(() => fake.CompleteAsync<Classification>(request with { Purpose = "qualify" })).Should().ThrowAsync<LlmOutputException>();
    }
}

public class LlmCostCapDayTests
{
    [Theory]
    [InlineData("2026-10-24T05:30:00Z", "2026-10-23T22:00:00Z", "SAST is UTC+2, so 05:30 UTC is 07:30 local on the 24th; the day began at 22:00 UTC on the 23rd")]
    [InlineData("2026-10-24T21:59:00Z", "2026-10-23T22:00:00Z", "23:59 local is still the 24th")]
    [InlineData("2026-10-24T22:00:00Z", "2026-10-24T22:00:00Z", "midnight local starts the 25th")]
    public void Day_start_is_midnight_in_south_africa(string utcNow, string expected, string because)
    {
        LlmCostCap.DayStartUtc(DateTimeOffset.Parse(utcNow, System.Globalization.CultureInfo.InvariantCulture)).Should().Be(DateTimeOffset.Parse(expected, System.Globalization.CultureInfo.InvariantCulture), because);
    }

    [Fact]
    public void Serializer_options_round_trip_enums_as_strings()
    {
        JsonSerializer.Serialize(ModelTier.Strong, LlmJsonSchema.SerializerOptions).Should().Be("\"Strong\"");
    }
}
