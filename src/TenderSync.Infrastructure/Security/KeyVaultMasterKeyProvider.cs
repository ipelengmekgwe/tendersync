using System.Collections.Concurrent;
using Azure.Core;
using Azure.Identity;
using Azure.Security.KeyVault.Keys;
using Azure.Security.KeyVault.Keys.Cryptography;
using Microsoft.Extensions.Options;

namespace TenderSync.Infrastructure.Security;

/// <summary>
/// Azure Key Vault key wrapping (RSA-OAEP-256): the data key goes to the vault to be wrapped and unwrapped, the master key
/// never leaves it. The key id is the versioned key URI, so a rotated vault key keeps older versions readable until the
/// re-wrap command has run. Authenticates with DefaultAzureCredential (managed identity in Container Apps).
/// </summary>
public sealed class KeyVaultMasterKeyProvider : IMasterKeyProvider
{
    private readonly IOptions<EncryptionOptions> _options;
    private readonly TokenCredential _credential;
    private readonly ConcurrentDictionary<string, CryptographyClient> _clients = new(StringComparer.Ordinal);
    private readonly Lazy<string> _activeKeyId;

    public KeyVaultMasterKeyProvider(IOptions<EncryptionOptions> options)
        : this(options, new DefaultAzureCredential())
    {
    }

    public KeyVaultMasterKeyProvider(IOptions<EncryptionOptions> options, TokenCredential credential)
    {
        _options = options;
        _credential = credential;
        _activeKeyId = new Lazy<string>(ResolveActiveKeyId);
    }

    public string ActiveKeyId => _activeKeyId.Value;

    public async Task<byte[]> WrapAsync(string keyId, ReadOnlyMemory<byte> dataKey, CancellationToken cancellationToken = default)
    {
        var result = await ClientFor(keyId).WrapKeyAsync(KeyWrapAlgorithm.RsaOaep256, dataKey.ToArray(), cancellationToken);
        return result.EncryptedKey;
    }

    public async Task<byte[]> UnwrapAsync(string keyId, ReadOnlyMemory<byte> wrappedKey, CancellationToken cancellationToken = default)
    {
        var result = await ClientFor(keyId).UnwrapKeyAsync(KeyWrapAlgorithm.RsaOaep256, wrappedKey.ToArray(), cancellationToken);
        return result.Key;
    }

    private CryptographyClient ClientFor(string keyId) =>
        _clients.GetOrAdd(keyId, id => new CryptographyClient(new Uri(id), _credential));

    /// <summary>Resolves an unversioned key URI to the current version so new documents record exactly which version wrapped them.</summary>
    private string ResolveActiveKeyId()
    {
        var uri = _options.Value.KeyVault.KeyUri;
        if (string.IsNullOrWhiteSpace(uri))
        {
            throw new InvalidOperationException("Encryption:KeyVault:KeyUri is required when Encryption:Provider is KeyVault.");
        }

        var keyUri = new Uri(uri);
        var segments = keyUri.AbsolutePath.Trim('/').Split('/', StringSplitOptions.RemoveEmptyEntries);
        if (segments.Length < 2 || !segments[0].Equals("keys", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException($"Encryption:KeyVault:KeyUri '{uri}' is not a Key Vault key URI (https://{{vault}}.vault.azure.net/keys/{{name}}[/{{version}}]).");
        }

        if (segments.Length >= 3)
        {
            return keyUri.ToString();
        }

        var client = new KeyClient(new Uri(keyUri.GetLeftPart(UriPartial.Authority)), _credential);
        var key = client.GetKey(segments[1]);
        return key.Value.Id.ToString();
    }
}
