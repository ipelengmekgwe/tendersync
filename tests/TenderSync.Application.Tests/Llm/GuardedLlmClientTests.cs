using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NSubstitute;
using TenderSync.Application.Abstractions;
using TenderSync.Application.Abstractions.Llm;
using TenderSync.Application.Abstractions.Persistence;
using TenderSync.Application.Features.Llm;
using TenderSync.Domain.Events;
using TenderSync.Domain.Operations;

namespace TenderSync.Application.Tests.Llm;

/// <summary>The guard around every model call: daily cap, routing, llm_calls rows on success and failure.</summary>
public class GuardedLlmClientTests
{
    private sealed record Answer(string Text);

    private static readonly DateTimeOffset Now = new(2026, 10, 24, 9, 0, 0, TimeSpan.Zero);
    private static readonly LlmRequest Request = new("classify", "v1", "system", [new LlmTextPart("hello")], TenderId: Guid.CreateVersion7());

    private readonly IAppDbContext _db = Substitute.For<IAppDbContext>();
    private readonly IReadDb _readDb = Substitute.For<IReadDb>();
    private readonly List<LlmCall> _calls = [];
    private readonly List<SourceHealthCheck> _health = [];
    private readonly FakeLlmClient _fake = new FakeLlmClient().Answer("classify", new Answer("ok"));
    private decimal _spentToday;

    private GuardedLlmClient Sut(decimal cap = 10m, ILlmClient? client = null)
    {
        var clock = Substitute.For<IDateTimeProvider>();
        clock.UtcNow.Returns(Now);
        _db.LlmCalls.When(r => r.Add(Arg.Any<LlmCall>())).Do(c => _calls.Add(c.Arg<LlmCall>()));
        _db.SourceHealthChecks.When(r => r.Add(Arg.Any<SourceHealthCheck>())).Do(c => _health.Add(c.Arg<SourceHealthCheck>()));
        _readDb.ExecuteScalarAsync<decimal?>(Arg.Is<string>(s => s.Contains("sum(cost_usd)")), Arg.Any<object?>(), Arg.Any<CancellationToken>()).Returns(_ => _spentToday);
        _readDb.ExecuteScalarAsync<long>(Arg.Is<string>(s => s.Contains("source_health")), Arg.Any<object?>(), Arg.Any<CancellationToken>()).Returns(_ => _health.Count);

        var options = Options.Create(new LlmOptions { DailyCostCapUsd = cap });
        var router = Substitute.For<ILlmClientRouter>();
        router.Resolve("classify").Returns(client ?? _fake);
        var recorder = new LlmCostRecorder(_db, clock, NullLogger<LlmCostRecorder>.Instance);
        var capService = new LlmCostCap(_db, _readDb, options, clock, NullLogger<LlmCostCap>.Instance);
        return new GuardedLlmClient(router, capService, recorder);
    }

    [Fact]
    public async Task A_successful_call_is_routed_and_recorded_with_its_cost()
    {
        _fake.CostUsd = 0.0123m;
        _fake.Usage = new LlmUsage(2000, 150, 800);

        var result = await Sut().CompleteAsync<Answer>(Request);

        result.Value.Text.Should().Be("ok");
        var row = _calls.Should().ContainSingle().Subject;
        row.Purpose.Should().Be("classify");
        row.Provider.Should().Be("fake");
        row.PromptVersion.Should().Be("v1");
        row.TenderId.Should().Be(Request.TenderId);
        row.TokensIn.Should().Be(2000);
        row.CacheReadTokens.Should().Be(800);
        row.CostUsd.Should().Be(0.0123m);
        row.Success.Should().BeTrue();
        await _db.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task A_refusal_is_recorded_as_a_failed_call_with_the_tokens_it_cost()
    {
        var refusing = Substitute.For<ILlmClient>();
        refusing.Provider.Returns("anthropic");
        refusing.CompleteAsync<Answer>(Arg.Any<LlmRequest>(), Arg.Any<CancellationToken>())
            .Returns<LlmResult<Answer>>(_ => throw new LlmCallFailedException("claude-haiku-5-5", new LlmUsage(500, 0, 0), 0.00005m, new LlmRefusalException("declined")));

        await FluentActions.Awaiting(() => Sut(client: refusing).CompleteAsync<Answer>(Request)).Should().ThrowAsync<LlmRefusalException>().WithMessage("declined");

        var row = _calls.Should().ContainSingle().Subject;
        row.Success.Should().BeFalse();
        row.Model.Should().Be("claude-haiku-5-5");
        row.TokensIn.Should().Be(500);
        row.Error.Should().Contain("declined");
    }

    [Fact]
    public async Task A_transport_failure_is_recorded_with_zero_usage()
    {
        var broken = Substitute.For<ILlmClient>();
        broken.Provider.Returns("openai");
        broken.CompleteAsync<Answer>(Arg.Any<LlmRequest>(), Arg.Any<CancellationToken>()).Returns<LlmResult<Answer>>(_ => throw new LlmException("HTTP 503"));

        await FluentActions.Awaiting(() => Sut(client: broken).CompleteAsync<Answer>(Request)).Should().ThrowAsync<LlmException>();

        _calls.Should().ContainSingle().Which.Should().Match<LlmCall>(c => !c.Success && c.Provider == "openai" && c.TokensIn == 0 && c.Error == "HTTP 503");
    }

    [Fact]
    public async Task The_daily_cap_stops_calls_and_raises_one_pipeline_failure_per_day()
    {
        _spentToday = 10m;
        var sut = Sut(cap: 10m);

        await FluentActions.Awaiting(() => sut.CompleteAsync<Answer>(Request)).Should().ThrowAsync<LlmCostCapExceededException>().WithMessage("*$10.00*");
        await FluentActions.Awaiting(() => sut.CompleteAsync<Answer>(Request)).Should().ThrowAsync<LlmCostCapExceededException>();

        _fake.Requests.Should().BeEmpty("no provider call is made once the cap is reached");
        _calls.Should().BeEmpty("blocked calls cost nothing and are not llm_calls rows");
        var alert = _health.Should().ContainSingle("the alert is raised once, not on every blocked call").Subject;
        alert.Source.Should().Be(LlmCostCap.HealthSource);
        alert.Ok.Should().BeFalse();
        alert.DomainEvents.OfType<PipelineFailure>().Should().ContainSingle().Which.Message.Should().Contain("cap");
    }

    [Fact]
    public async Task A_cap_of_zero_disables_the_check_and_spend_below_the_cap_passes()
    {
        _spentToday = 999m;
        (await Sut(cap: 0m).CompleteAsync<Answer>(Request)).Value.Text.Should().Be("ok");

        _spentToday = 9.99m;
        (await Sut(cap: 10m).CompleteAsync<Answer>(Request)).Value.Text.Should().Be("ok");
        _health.Should().BeEmpty();
    }
}
