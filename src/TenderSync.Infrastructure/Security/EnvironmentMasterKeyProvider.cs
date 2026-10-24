using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Options;

namespace TenderSync.Infrastructure.Security;

/// <summary>
/// Master keys held in configuration (user-secrets, .env, Container Apps secrets): AES-256-GCM key wrapping with the key id as
/// associated data, so a wrapped key cannot be replayed under another id. Keys are read on first use, never at registration.
/// </summary>
public sealed class EnvironmentMasterKeyProvider(IOptions<EncryptionOptions> options) : IMasterKeyProvider
{
    private const int NonceSize = 12;
    private const int TagSize = 16;

    private readonly Lazy<(string ActiveKeyId, Dictionary<string, byte[]> Keys)> _keys = new(() => Load(options.Value));

    public string ActiveKeyId => _keys.Value.ActiveKeyId;

    public Task<byte[]> WrapAsync(string keyId, ReadOnlyMemory<byte> dataKey, CancellationToken cancellationToken = default)
    {
        var master = KeyFor(keyId);
        var nonce = RandomNumberGenerator.GetBytes(NonceSize);
        var ciphertext = new byte[dataKey.Length];
        var tag = new byte[TagSize];
        using (var aes = new AesGcm(master, TagSize))
        {
            aes.Encrypt(nonce, dataKey.Span, ciphertext, tag, Encoding.UTF8.GetBytes(keyId));
        }

        var wrapped = new byte[NonceSize + ciphertext.Length + TagSize];
        nonce.CopyTo(wrapped, 0);
        ciphertext.CopyTo(wrapped, NonceSize);
        tag.CopyTo(wrapped, NonceSize + ciphertext.Length);
        return Task.FromResult(wrapped);
    }

    public Task<byte[]> UnwrapAsync(string keyId, ReadOnlyMemory<byte> wrappedKey, CancellationToken cancellationToken = default)
    {
        var master = KeyFor(keyId);
        var span = wrappedKey.Span;
        if (span.Length <= NonceSize + TagSize)
        {
            throw new CryptographicException("The wrapped data key is too short.");
        }

        var nonce = span[..NonceSize];
        var ciphertext = span[NonceSize..^TagSize];
        var tag = span[^TagSize..];
        var dataKey = new byte[ciphertext.Length];
        using (var aes = new AesGcm(master, TagSize))
        {
            aes.Decrypt(nonce, ciphertext, tag, dataKey, Encoding.UTF8.GetBytes(keyId));
        }

        return Task.FromResult(dataKey);
    }

    private byte[] KeyFor(string keyId) =>
        _keys.Value.Keys.TryGetValue(keyId, out var key)
            ? key
            : throw new InvalidOperationException($"Master key '{keyId}' is not configured. Add it under Encryption:Keys to read documents wrapped with it (docs/runbooks/key-rotation.md).");

    private static (string ActiveKeyId, Dictionary<string, byte[]> Keys) Load(EncryptionOptions options)
    {
        var keys = new Dictionary<string, byte[]>(StringComparer.Ordinal);
        foreach (var (id, value) in options.Keys)
        {
            if (!string.IsNullOrWhiteSpace(value))
            {
                keys[id] = Parse(id, value);
            }
        }

        var fromEnvironment = Environment.GetEnvironmentVariable(EncryptionOptions.MasterKeyVariable);
        if (!string.IsNullOrWhiteSpace(fromEnvironment) && !keys.ContainsKey(EncryptionOptions.DefaultEnvironmentKeyId))
        {
            keys[EncryptionOptions.DefaultEnvironmentKeyId] = Parse(EncryptionOptions.DefaultEnvironmentKeyId, fromEnvironment);
        }

        if (keys.Count == 0)
        {
            throw new InvalidOperationException($"No master key is configured: set Encryption:Keys:{{id}} (and Encryption:ActiveKeyId) or the {EncryptionOptions.MasterKeyVariable} environment variable to a base64 32-byte key.");
        }

        var active = string.IsNullOrWhiteSpace(options.ActiveKeyId)
            ? keys.Count == 1 ? keys.Keys.First() : keys.ContainsKey(EncryptionOptions.DefaultEnvironmentKeyId) ? EncryptionOptions.DefaultEnvironmentKeyId : throw new InvalidOperationException("Encryption:ActiveKeyId must name one of the configured keys when more than one is present.")
            : options.ActiveKeyId.Trim();
        if (!keys.ContainsKey(active))
        {
            throw new InvalidOperationException($"Encryption:ActiveKeyId '{active}' is not among the configured keys ({string.Join(", ", keys.Keys)}).");
        }

        return (active, keys);
    }

    private static byte[] Parse(string id, string base64)
    {
        byte[] key;
        try
        {
            key = Convert.FromBase64String(base64.Trim());
        }
        catch (FormatException ex)
        {
            throw new InvalidOperationException($"Master key '{id}' is not valid base64.", ex);
        }

        return key.Length == 32 ? key : throw new InvalidOperationException($"Master key '{id}' must be 32 bytes (256 bits); it is {key.Length}.");
    }
}
