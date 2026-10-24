using System.Net;
using System.Net.Http.Headers;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Http.Resilience;
using Microsoft.Extensions.Options;
using Polly;
using TenderSync.Application.Abstractions.Sources;

namespace TenderSync.Infrastructure.ETenders;

public static class ETendersServiceExtensions
{
    public const string ResiliencePipelineName = "etenders";
    public const string UserAgent = "TenderSync/1.0 (+https://github.com/ipelengmekgwe/tendersync)";

    /// <summary>
    /// eTenders client with the resilience pipeline from ADR-006: total timeout → retry (exponential + jitter) →
    /// circuit breaker → per-attempt timeout. Values come from <see cref="ETendersOptions"/>, read at resolve time.
    /// A plain HTTP 500 is neither retried nor counted by the breaker: the API returns it deterministically for certain
    /// releases, and <see cref="ETendersClient"/> handles those by bisecting the page.
    /// </summary>
    public static IServiceCollection AddETenders(this IServiceCollection services, IConfiguration? configuration = null)
    {
        var optionsBuilder = services.AddOptions<ETendersOptions>();
        var status = services.AddOptions<TenderSync.Application.Features.Status.StatusOptions>();
        if (configuration is not null)
        {
            status.Bind(configuration.GetSection(TenderSync.Application.Features.Status.StatusOptions.SectionName)).ValidateDataAnnotations().ValidateOnStart();
            optionsBuilder.Bind(configuration.GetSection(ETendersOptions.SectionName)).ValidateDataAnnotations().ValidateOnStart();
        }

        services.AddSingleton<ReleaseSchemaValidator>();
        services.AddSingleton<ReleaseMapper>();
        services.AddScoped<ETendersOcdsSource>();
        services.AddScoped<ITenderSource>(provider => provider.GetRequiredService<ETendersOcdsSource>());

        services.AddHttpClient<ETendersClient>((provider, client) =>
            {
                var options = provider.GetRequiredService<IOptions<ETendersOptions>>().Value;
                client.BaseAddress = new Uri(options.BaseUrl, UriKind.Absolute);
                client.Timeout = Timeout.InfiniteTimeSpan; // the resilience pipeline owns timeouts
                client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
                client.DefaultRequestHeaders.UserAgent.ParseAdd(UserAgent);
            })
            .AddResilienceHandler(ResiliencePipelineName, (builder, context) =>
            {
                var options = context.ServiceProvider.GetRequiredService<IOptions<ETendersOptions>>().Value;

                builder.AddTimeout(TimeSpan.FromSeconds(options.TotalTimeoutSeconds));
                builder.AddRetry(new HttpRetryStrategyOptions
                {
                    MaxRetryAttempts = options.RetryCount,
                    BackoffType = DelayBackoffType.Exponential,
                    UseJitter = true,
                    Delay = TimeSpan.FromSeconds(options.RetryBaseDelaySeconds),
                    ShouldHandle = args => ValueTask.FromResult(IsTransientButNotPoison(args.Outcome)),
                });
                builder.AddCircuitBreaker(new HttpCircuitBreakerStrategyOptions
                {
                    FailureRatio = options.CircuitBreakerFailureRatio,
                    MinimumThroughput = options.CircuitBreakerMinimumThroughput,
                    SamplingDuration = TimeSpan.FromSeconds(options.CircuitBreakerSamplingSeconds),
                    BreakDuration = TimeSpan.FromSeconds(options.CircuitBreakerBreakSeconds),
                    ShouldHandle = args => ValueTask.FromResult(IsTransientButNotPoison(args.Outcome)),
                });
                builder.AddTimeout(TimeSpan.FromSeconds(options.AttemptTimeoutSeconds));
            });

        return services;
    }

    /// <summary>Standard transient predicate (5xx, 408, 429, connection and timeout failures) minus plain 500.</summary>
    internal static bool IsTransientButNotPoison(Outcome<HttpResponseMessage> outcome)
    {
        if (outcome.Result?.StatusCode == HttpStatusCode.InternalServerError)
        {
            return false;
        }

        return HttpClientResiliencePredicates.IsTransient(outcome);
    }
}
