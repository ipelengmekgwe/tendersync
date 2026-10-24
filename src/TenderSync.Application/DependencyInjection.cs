using FluentValidation;
using MediatR;
using Microsoft.Extensions.DependencyInjection;
using TenderSync.Application.Common.Behaviours;

namespace TenderSync.Application;

public static class DependencyInjection
{
    /// <summary>
    /// Registers MediatR handlers, the pipeline behaviours (outermost first: logging → authorization → validation → unit of work → audit)
    /// and FluentValidation validators from this assembly.
    /// </summary>
    public static IServiceCollection AddApplication(this IServiceCollection services)
    {
        services.AddMediatR(configuration =>
        {
            configuration.RegisterServicesFromAssembly(typeof(AssemblyMarker).Assembly);
            configuration.AddOpenBehavior(typeof(LoggingBehaviour<,>));
            configuration.AddOpenBehavior(typeof(AuthorizationBehaviour<,>));
            configuration.AddOpenBehavior(typeof(ValidationBehaviour<,>));
            configuration.AddOpenBehavior(typeof(UnitOfWorkBehaviour<,>));
            configuration.AddOpenBehavior(typeof(AuditBehaviour<,>));
        });

        services.AddValidatorsFromAssembly(typeof(AssemblyMarker).Assembly, includeInternalTypes: true);
        services.AddScoped<Jobs.JobRunner>();
        services.AddScoped<Jobs.OutboxProcessor>();
        services.AddScoped<Features.Ingest.TenderIngestor>();
        services.AddScoped<Features.Documents.TenderDocumentDownloader>();
        services.AddScoped<Features.Documents.DocumentTextExtraction>();
        services.AddOptions<Features.Documents.DocumentOptions>(); // bound and validated by Infrastructure.AddDocumentProcessing
        services.AddOptions<Features.Llm.LlmOptions>(); // bound and validated by Infrastructure.AddLlm
        services.AddSingleton<Abstractions.Llm.IPromptRegistry, Features.Llm.PromptRegistry>();
        services.AddSingleton<Features.Llm.LlmPricing>();
        services.AddScoped<Abstractions.Llm.ILlmCostRecorder, Features.Llm.LlmCostRecorder>();
        services.AddScoped<Features.Llm.LlmCostCap>();
        services.AddScoped<Abstractions.Llm.ILlmClient, Features.Llm.GuardedLlmClient>();
        services.AddOptions<Features.Classification.ClassificationOptions>(); // bound and validated by Infrastructure.AddClassification
        services.AddSingleton<Features.Classification.KeywordPrefilter>();
        services.AddScoped<Features.Classification.TenderClassifier>();
        services.AddScoped<Features.Classification.Golden.GoldenSetBuilder>();
        return services;
    }
}
