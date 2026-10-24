using System.Text;
using Azure.Storage.Blobs;
using Microsoft.Extensions.Logging.Abstractions;
using Testcontainers.Azurite;
using TenderSync.Application.Abstractions.Storage;
using TenderSync.Infrastructure.Storage;

namespace TenderSync.Infrastructure.Tests.Documents;

/// <summary>The same round trip against both stores (PLAN.md WP04 acceptance).</summary>
public static class StoreContract
{
    public static async Task RoundTripAsync(IDocumentStore store)
    {
        var path = $"tenders/ocds-9t57fa-{Random.Shared.Next(100000, 999999)}/{Guid.CreateVersion7()}.pdf";
        var bytes = Encoding.UTF8.GetBytes("%PDF-1.7 round trip");

        (await store.ExistsAsync(path)).Should().BeFalse();
        var stored = await store.SaveAsync(path, new MemoryStream(bytes), "application/pdf");
        stored.Should().Be(path);
        (await store.ExistsAsync(path)).Should().BeTrue();

        await using (var read = await store.OpenReadAsync(path))
        {
            using var buffer = new MemoryStream();
            await read.CopyToAsync(buffer);
            buffer.ToArray().Should().Equal(bytes);
        }

        var replaced = Encoding.UTF8.GetBytes("%PDF-1.7 replaced");
        await store.SaveAsync(path, new MemoryStream(replaced), "application/pdf");
        await using (var read = await store.OpenReadAsync(path))
        {
            using var buffer = new MemoryStream();
            await read.CopyToAsync(buffer);
            buffer.ToArray().Should().Equal(replaced, "save overwrites");
        }

        await store.DeleteAsync(path);
        (await store.ExistsAsync(path)).Should().BeFalse();
        await store.DeleteAsync(path); // deleting twice is fine

        await FluentActions.Awaiting(() => store.OpenReadAsync(path)).Should().ThrowAsync<FileNotFoundException>();
        await FluentActions.Awaiting(() => store.SaveAsync("../escape.txt", new MemoryStream(bytes), null)).Should().ThrowAsync<ArgumentException>();
    }
}

public class FileSystemDocumentStoreTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "tendersync-tests", Guid.NewGuid().ToString("N"));

    [Fact]
    public async Task Round_trip_overwrite_delete_and_path_safety()
    {
        var store = new FileSystemDocumentStore(_root, NullLogger<FileSystemDocumentStore>.Instance);

        await StoreContract.RoundTripAsync(store);

        Directory.GetFiles(_root, "*.tmp", SearchOption.AllDirectories).Should().BeEmpty("writes go through a temp file that is moved into place");
    }

    [Theory]
    [InlineData("")]
    [InlineData("C:\\Windows\\win.ini")]
    [InlineData("tenders/../../x")]
    [InlineData("tenders//x")]
    [InlineData("./x")]
    public void Paths_that_leave_the_root_are_rejected(string path)
    {
        var store = new FileSystemDocumentStore(_root, NullLogger<FileSystemDocumentStore>.Instance);

        var act = () => store.Resolve(path);

        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void Backslashes_are_normalised_and_the_root_is_created()
    {
        var store = new FileSystemDocumentStore(_root, NullLogger<FileSystemDocumentStore>.Instance);

        store.Resolve("tenders\\a\\b.pdf").Should().Be(Path.Combine(_root, "tenders", "a", "b.pdf"));
        Directory.Exists(_root).Should().BeTrue();
    }

    public void Dispose()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }

        GC.SuppressFinalize(this);
    }
}

[Trait("Category", "Integration")]
public class AzureBlobDocumentStoreTests : IAsyncLifetime
{
    // The SDK's API version is usually newer than the emulator's; the flag makes Azurite serve it anyway.
    private readonly AzuriteContainer _azurite = new AzuriteBuilder("mcr.microsoft.com/azure-storage/azurite:3.35.0").WithCommand("--skipApiVersionCheck").Build();

    public Task InitializeAsync() => _azurite.StartAsync();

    public Task DisposeAsync() => _azurite.DisposeAsync().AsTask();

    [Fact]
    public async Task Round_trip_against_azurite()
    {
        using var store = new AzureBlobDocumentStore(new BlobContainerClient(_azurite.GetConnectionString(), "documents"));

        await StoreContract.RoundTripAsync(store);

        var client = new BlobContainerClient(_azurite.GetConnectionString(), "documents");
        (await client.ExistsAsync()).Value.Should().BeTrue("the container is created on first use");
    }
}
