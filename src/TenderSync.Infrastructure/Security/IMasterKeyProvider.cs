namespace TenderSync.Infrastructure.Security;

/// <summary>Wraps and unwraps per-document data keys with a named master key. Implemented over environment keys and Azure Key Vault.</summary>
public interface IMasterKeyProvider
{
    string ActiveKeyId { get; }

    Task<byte[]> WrapAsync(string keyId, ReadOnlyMemory<byte> dataKey, CancellationToken cancellationToken = default);

    Task<byte[]> UnwrapAsync(string keyId, ReadOnlyMemory<byte> wrappedKey, CancellationToken cancellationToken = default);
}
