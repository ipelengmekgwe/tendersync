using System.Net.Http.Headers;
using Azure.Storage.Blobs;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Http.Resilience;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Polly;
using TenderSync.Application.Abstractions.Documents;
using TenderSync.Application.Abstractions.Storage;
using TenderSync.Application.Features.Documents;
using TenderSync.Infrastructure.Documents.Extractors;
using TenderSync.Infrastructure.ETenders;
using TenderSync.Infrastructure.Storage;

namespace TenderSync.Infrastructure.Documents;

public static class DocumentServiceExtensions
{
    public const string DownloadResiliencePipelineName = "etenders-download";

    /// <summary>
    /// Document store (ADR-013: filesystem or Azure Blob, chosen at resolve time from DocumentStore:Provider), the throttled
    /// downloader with its resilience pipeline, and the text extractors behind <see cref="IDocumentTextExtractor"/>.
    /// </summary>
    public static IServiceCollection AddDocumentProcessing(this IServiceCollection services, IConfiguration? configuration)
    {
        var documentOptions = services.AddOptions<DocumentOptions>();
        var storeOptions = services.AddOptions<DocumentStoreOptions>();
        if (configuration is not null)
        {
            documentOptions.Bind(configuration.GetSection(DocumentOptions.SectionName)).ValidateDataAnnotations().ValidateOnStart();
            storeOptions.Bind(configuration.GetSection(DocumentStoreOptions.SectionName)).ValidateDataAnnotations().ValidateOnStart();
        }

        services.AddSingleton<IDocumentStore>(provider =>
        {
            var options = provider.GetRequiredService<IOptions<DocumentStoreOptions>>().Value;
            return options.Provider switch
            {
                DocumentStoreOptions.AzureBlobProvider => new AzureBlobDocumentStore(new BlobContainerClient(
                    string.IsNullOrWhiteSpace(options.AzureBlob.ConnectionString)
                        ? throw new InvalidOperationException("DocumentStore:AzureBlob:ConnectionString is required when Provider is AzureBlob.")
                        : options.AzureBlob.ConnectionString,
                    options.AzureBlob.ContainerName)),
                _ => new FileSystemDocumentStore(options.FileSystem.ResolveRoot(), provider.GetRequiredService<ILogger<FileSystemDocumentStore>>()),
            };
        });

        services.AddSingleton<DownloadThrottle>();
        services.AddScoped<IDocumentDownloader, HttpDocumentDownloader>();
        services.AddHttpClient(HttpDocumentDownloader.ClientName, client =>
            {
                client.Timeout = Timeout.InfiniteTimeSpan; // the downloader applies DownloadTimeoutSeconds itself
                client.DefaultRequestHeaders.UserAgent.ParseAdd(ETendersServiceExtensions.UserAgent);
                client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("*/*"));
            })
            .AddResilienceHandler(DownloadResiliencePipelineName, (builder, context) =>
            {
                var options = context.ServiceProvider.GetRequiredService<IOptions<DocumentOptions>>().Value;
                builder.AddRetry(new HttpRetryStrategyOptions
                {
                    MaxRetryAttempts = 3,
                    BackoffType = DelayBackoffType.Exponential,
                    UseJitter = true,
                    Delay = TimeSpan.FromSeconds(2),
                });
                builder.AddTimeout(TimeSpan.FromSeconds(options.DownloadTimeoutSeconds));
            });

        services.AddSingleton<ITextExtractor, PdfTextExtractor>();
        services.AddSingleton<ITextExtractor, WordTextExtractor>();
        services.AddSingleton<ITextExtractor, SpreadsheetTextExtractor>();
        services.AddSingleton<ITextExtractor, PlainTextExtractor>();
        services.AddSingleton<IDocumentTextExtractor, CompositeTextExtractor>();

        return services;
    }
}
