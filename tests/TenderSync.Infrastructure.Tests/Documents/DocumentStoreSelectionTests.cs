using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using TenderSync.Application.Abstractions.Storage;
using TenderSync.Infrastructure.Documents;
using TenderSync.Infrastructure.Storage;

namespace TenderSync.Infrastructure.Tests.Documents;

public class DocumentStoreSelectionTests
{
    private static IDocumentStore Resolve(Dictionary<string, string?> settings)
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(settings).Build();
        var services = new ServiceCollection().AddLogging();
        services.AddDocumentProcessing(configuration);
        return services.BuildServiceProvider().GetRequiredService<IDocumentStore>();
    }

    [Fact]
    public void Azure_blob_with_a_service_uri_uses_managed_identity_without_a_connection_string()
    {
        var store = Resolve(new()
        {
            ["DocumentStore:Provider"] = "AzureBlob",
            ["DocumentStore:AzureBlob:ServiceUri"] = "https://sttendersyncdev.blob.core.windows.net",
        });

        store.Should().BeOfType<AzureBlobDocumentStore>();
    }

    [Fact]
    public void Azure_blob_without_a_connection_string_or_service_uri_fails_with_a_clear_message()
    {
        var act = () => Resolve(new() { ["DocumentStore:Provider"] = "AzureBlob" });

        act.Should().Throw<InvalidOperationException>().WithMessage("*ConnectionString or DocumentStore:AzureBlob:ServiceUri*");
    }
}
