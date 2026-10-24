using System.Net;
using System.Net.Http.Json;
using Microsoft.Extensions.DependencyInjection;
using TenderSync.Application.Abstractions.Persistence;
using TenderSync.Application.Features.Admin.Queries.GetLlmCallsSummary;
using TenderSync.Domain.Operations;

namespace TenderSync.Api.Tests;

[Collection(ApiCollection.Name)]
[Trait("Category", "Integration")]
public class LlmCallsEndpointTests(ApiFactory factory)
{
    [Fact]
    public async Task Admin_sees_spend_by_day_purpose_and_model_with_todays_total_against_the_cap()
    {
        var purpose = $"smoke-{Guid.NewGuid():N}";
        var now = DateTimeOffset.UtcNow;
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<IAppDbContext>();
            db.LlmCalls.Add(LlmCall.Record(purpose, "anthropic", "claude-haiku-5-5", "v1", null, 2000, 100, 500, 0.0003m, 900, true, null, now));
            db.LlmCalls.Add(LlmCall.Record(purpose, "anthropic", "claude-opus-5-5", "v1", null, 40000, 4000, 0, 0.24m, 12000, true, null, now.AddMinutes(-5)));
            db.LlmCalls.Add(LlmCall.Record(purpose, "openai", "gpt-4.1-mini", "v1", null, 500, 0, 0, 0m, 300, false, "HTTP 502", now.AddMinutes(-10)));
            await db.SaveChangesAsync();
        }

        using var viewer = await factory.CreateViewerClientAsync();
        (await viewer.GetAsync("/api/v1/admin/llm-calls")).StatusCode.Should().Be(HttpStatusCode.Forbidden);

        using var admin = await factory.CreateAdminClientAsync();
        var summary = await admin.GetFromJsonAsync<LlmCallsSummaryDto>($"/api/v1/admin/llm-calls?purpose={purpose}&recent=10");

        summary!.Calls.Should().Be(3);
        summary.Failed.Should().Be(1);
        summary.TokensIn.Should().Be(42500);
        summary.CostUsd.Should().Be(0.2403m);
        summary.DailyCapUsd.Should().Be(10m);
        summary.SpentTodayUsd.Should().BeGreaterThanOrEqualTo(0.2403m);
        summary.ByPurpose.Should().ContainSingle().Which.Key.Should().Be(purpose);
        summary.ByModel.Select(b => b.Key).Should().Contain("anthropic/claude-opus-5-5").And.Contain("openai/gpt-4.1-mini");
        summary.ByModel[0].Key.Should().Be("anthropic/claude-opus-5-5", "ordered by cost");
        summary.ByDay.Should().NotBeEmpty();
        summary.Recent.Should().HaveCount(3);
        summary.Recent[0].Model.Should().Be("claude-haiku-5-5", "newest first");
        summary.Recent.Should().Contain(c => !c.Success && c.Error == "HTTP 502");

        (await admin.GetAsync("/api/v1/admin/llm-calls?recent=5000")).StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }
}
