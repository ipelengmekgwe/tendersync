using System.Collections.Concurrent;
using System.Text.Json;
using TenderSync.Application.Abstractions.Llm;

namespace TenderSync.Application.Features.Llm;

/// <summary>
/// Canned answers by purpose (PLAN.md §8.2) for tests and for running the pipeline without a provider key
/// (Llm:DefaultProvider = fake). Register responses as objects or JSON; unregistered purposes throw so a test fails loudly.
/// </summary>
public sealed class FakeLlmClient : ILlmClient
{
    private readonly ConcurrentDictionary<string, Func<LlmRequest, string>> _answers = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentQueue<LlmRequest> _requests = new();

    public string Provider => LlmOptions.FakeProvider;

    public string Model { get; set; } = "fake-model";

    public LlmUsage Usage { get; set; } = new(1000, 100, 0);

    public decimal CostUsd { get; set; } = 0.001m;

    public IReadOnlyList<LlmRequest> Requests => [.. _requests];

    public FakeLlmClient Answer(string purpose, object value)
    {
        _answers[purpose] = _ => JsonSerializer.Serialize(value, LlmJsonSchema.SerializerOptions);
        return this;
    }

    public FakeLlmClient AnswerJson(string purpose, string json)
    {
        _answers[purpose] = _ => json;
        return this;
    }

    public FakeLlmClient Answer(string purpose, Func<LlmRequest, object> factory)
    {
        _answers[purpose] = request => JsonSerializer.Serialize(factory(request), LlmJsonSchema.SerializerOptions);
        return this;
    }

    public Task<LlmResult<T>> CompleteAsync<T>(LlmRequest request, CancellationToken cancellationToken = default)
        where T : class
    {
        ArgumentNullException.ThrowIfNull(request);
        _requests.Enqueue(request);
        if (!_answers.TryGetValue(request.Purpose, out var answer))
        {
            throw new LlmOutputException($"FakeLlmClient has no answer registered for purpose '{request.Purpose}'.");
        }

        var json = answer(request);
        var value = LlmJsonSchema.Parse<T>(json);
        return Task.FromResult(new LlmResult<T>(value, json, [], Usage, CostUsd, Provider, Model, TimeSpan.FromMilliseconds(1)));
    }
}
