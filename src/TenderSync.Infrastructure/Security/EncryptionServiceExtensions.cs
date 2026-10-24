using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using TenderSync.Application.Abstractions.Security;
using TenderSync.Application.Features.Companies;

namespace TenderSync.Infrastructure.Security;

public static class EncryptionServiceExtensions
{
    /// <summary>
    /// Company-document encryption (ADR-013): the master key provider chosen by Encryption:Provider at resolve time and the
    /// AES-256-GCM envelope service behind <see cref="IEncryptionService"/>. Keys are read on first use so hosts and tests
    /// start without one.
    /// </summary>
    public static IServiceCollection AddEncryption(this IServiceCollection services, IConfiguration? configuration)
    {
        var encryption = services.AddOptions<EncryptionOptions>();
        var documents = services.AddOptions<CompanyDocumentOptions>();
        if (configuration is not null)
        {
            encryption.Bind(configuration.GetSection(EncryptionOptions.SectionName)).ValidateDataAnnotations().ValidateOnStart();
            documents.Bind(configuration.GetSection(CompanyDocumentOptions.SectionName)).ValidateDataAnnotations().ValidateOnStart();
        }

        services.AddSingleton<IMasterKeyProvider>(provider =>
        {
            var options = provider.GetRequiredService<IOptions<EncryptionOptions>>();
            return options.Value.Provider.Equals(EncryptionOptions.KeyVaultProvider, StringComparison.OrdinalIgnoreCase)
                ? new KeyVaultMasterKeyProvider(options)
                : new EnvironmentMasterKeyProvider(options);
        });
        services.AddSingleton<IEncryptionService, AesGcmEnvelopeEncryptionService>();
        return services;
    }
}
