using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;
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
        var classification = services.AddOptions<TenderSync.Application.Features.Classification.ClassificationOptions>();
        var extraction = services.AddOptions<TenderSync.Application.Features.Requirements.ExtractionOptions>();
        var qualification = services.AddOptions<TenderSync.Application.Features.Qualification.QualificationOptions>();
        if (configuration is not null)
        {
            classification.Bind(configuration.GetSection(TenderSync.Application.Features.Classification.ClassificationOptions.SectionName)).ValidateDataAnnotations().ValidateOnStart();
            extraction.Bind(configuration.GetSection(TenderSync.Application.Features.Requirements.ExtractionOptions.SectionName)).ValidateDataAnnotations().ValidateOnStart();
            qualification.Bind(configuration.GetSection(TenderSync.Application.Features.Qualification.QualificationOptions.SectionName)).ValidateDataAnnotations().ValidateOnStart();
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
        services.AddSingleton(provider =>
        {
            // Configured answers (Llm:FakeAnswers) are loaded first; tests register their own on top.
            var fake = new FakeLlmClient();
            foreach (var (purpose, json) in provider.GetRequiredService<IOptions<LlmOptions>>().Value.FakeAnswers)
            {
                fake.AnswerJson(purpose, json);
            }

            return fake;
        });
        services.AddKeyedSingleton<ILlmClient>(LlmOptions.FakeProvider, (provider, _) => provider.GetRequiredService<FakeLlmClient>());
        services.AddSingleton<ILlmClientRouter, LlmClientRouter>();

        return services;
    }
}
