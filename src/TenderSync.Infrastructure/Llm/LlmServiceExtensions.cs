using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using TenderSync.Application.Abstractions.Llm;
using TenderSync.Application.Features.Llm;

namespace TenderSync.Infrastructure.Llm;

public static class LlmServiceExtensions
{
    /// <summary>
    /// Provider adapters as keyed services (anthropic, openai, fake) behind <see cref="ILlmClientRouter"/>. Application
    /// registers the guarded <see cref="ILlmClient"/> that features inject. Keys are read when an adapter is first used,
    /// so hosts start without one and the fake provider needs none.
    /// </summary>
    public static IServiceCollection AddLlm(this IServiceCollection services, IConfiguration? configuration)
    {
        var llm = services.AddOptions<LlmOptions>();
        var anthropic = services.AddOptions<AnthropicOptions>();
        var openAi = services.AddOptions<OpenAiCompatibleOptions>();
        if (configuration is not null)
        {
            llm.Bind(configuration.GetSection(LlmOptions.SectionName)).ValidateDataAnnotations().ValidateOnStart();
            anthropic.Bind(configuration.GetSection(AnthropicOptions.SectionName)).ValidateDataAnnotations().ValidateOnStart();
            openAi.Bind(configuration.GetSection(OpenAiCompatibleOptions.SectionName)).ValidateDataAnnotations().ValidateOnStart();
        }

        services.AddHttpClient(OpenAiCompatibleLlmClient.ClientName).AddStandardResilienceHandler(options =>
        {
            options.AttemptTimeout.Timeout = TimeSpan.FromMinutes(5);
            options.TotalRequestTimeout.Timeout = TimeSpan.FromMinutes(12);
            options.CircuitBreaker.SamplingDuration = TimeSpan.FromMinutes(10);
        });

        services.TryAddSingleton<LlmPricing>();
        services.AddKeyedSingleton<ILlmClient, AnthropicLlmClient>(LlmOptions.AnthropicProvider);
        services.AddKeyedSingleton<ILlmClient, OpenAiCompatibleLlmClient>(LlmOptions.OpenAiProvider);
        services.AddSingleton<FakeLlmClient>();
        services.AddKeyedSingleton<ILlmClient>(LlmOptions.FakeProvider, (provider, _) => provider.GetRequiredService<FakeLlmClient>());
        services.AddSingleton<ILlmClientRouter, LlmClientRouter>();

        return services;
    }
}
