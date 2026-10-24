using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using TenderSync.Application.Abstractions.Llm;
using TenderSync.Application.Features.Llm;
using TenderSync.Infrastructure.Llm;
using UglyToad.PdfPig.Fonts.Standard14Fonts;
using UglyToad.PdfPig.Writer;

namespace TenderSync.Evals;

/// <summary>
/// WP06 acceptance: a real call with a 3-page PDF returns schema-valid JSON with at least one page citation.
/// Runs only with TENDERSYNC_EVALS=1 and an Anthropic key (Llm__Anthropic__ApiKey or ANTHROPIC_API_KEY); costs under a cent.
/// </summary>
public class LlmLiveSmokeTests
{
    private static string? ApiKey => Environment.GetEnvironmentVariable("Llm__Anthropic__ApiKey") ?? Environment.GetEnvironmentVariable("ANTHROPIC_API_KEY");

    private static bool Enabled => Environment.GetEnvironmentVariable("TENDERSYNC_EVALS") == "1" && !string.IsNullOrWhiteSpace(ApiKey);

    [Fact]
    public async Task Anthropic_returns_structured_json_with_a_page_citation_for_a_three_page_pdf()
    {
        if (!Enabled)
        {
            // xunit 2 has no dynamic skip without an extra package; the test is a no-op until the gate is set.
            Console.WriteLine("Live LLM smoke test skipped: set TENDERSYNC_EVALS=1 and an Anthropic API key.");
            return;
        }

        var registry = new PromptRegistry();
        var prompt = registry.Get("smoke");
        var pricing = new LlmPricing(Options.Create(new LlmOptions()), NullLogger<LlmPricing>.Instance);
        var client = new AnthropicLlmClient(Options.Create(new AnthropicOptions { ApiKey = ApiKey! }), pricing, NullLogger<AnthropicLlmClient>.Instance);

        var pdf = Pdf(
            "WESTERN CAPE GOVERNMENT: HEALTH AND WELLNESS\nINVITATION TO BID\nBid number: WCGHCC0038/1/2025\nSupplementary bid for the supply and delivery of comfort packs to district offices.",
            "SUBMISSION\nSealed bids must be deposited in the tender box at 4 Dorp Street, Cape Town.\nClosing date: 20 November 2026 at 11:00.\nLate bids will not be considered.",
            "EVALUATION\nBids will be evaluated on the 80/20 preference point system.\nA valid CSD registration and tax compliance status are mandatory.");
        var request = new LlmRequest(
            prompt.Purpose, prompt.VersionTag, prompt.Body,
            [new LlmDocumentPart(pdf, "application/pdf", "Bid Document"), new LlmTextPart("Answer with the JSON only.")],
            prompt.Tier, LlmEffort.Low, 1024, EnableCitations: true);

        var result = await client.CompleteAsync<SmokeSummary>(request);

        result.Value.Summary.Should().NotBeNullOrWhiteSpace();
        result.Value.BidNumber.Should().Contain("WCGHCC0038");
        result.Value.ClosingDate.Should().Contain("20 November 2026");
        result.Value.ClosingDatePage.Should().Be(2);
        result.Citations.Should().Contain(c => c.StartPage == 2, "the closing date is cited from page 2");
        result.Usage.InputTokens.Should().BeGreaterThan(0);
        result.Usage.OutputTokens.Should().BeGreaterThan(0);
        result.CostUsd.Should().BeGreaterThan(0).And.BeLessThan(0.05m);
        result.Model.Should().StartWith("claude-");
    }

    private static byte[] Pdf(params string[] pages)
    {
        var builder = new PdfDocumentBuilder();
        var font = builder.AddStandard14Font(Standard14Font.Helvetica);
        foreach (var text in pages)
        {
            var page = builder.AddPage(UglyToad.PdfPig.Content.PageSize.A4);
            var y = 790.0;
            foreach (var line in text.Split('\n'))
            {
                page.AddText(line, 11, new UglyToad.PdfPig.Core.PdfPoint(40, y), font);
                y -= 18;
            }
        }

        return builder.Build();
    }
}
