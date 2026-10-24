using System.ComponentModel.DataAnnotations;

namespace TenderSync.Infrastructure.Security;

/// <summary>
/// Master keys for company-document encryption (PLAN.md §12, ADR-013), bound from "Encryption". Locally the master key is a
/// base64 32-byte value from <c>Encryption:Keys:{id}</c> or the <c>TENDERSYNC_MASTER_KEY</c> variable; on Azure it is a Key
/// Vault key that wraps and unwraps data keys without ever leaving the vault. Rotation adds a key and moves ActiveKeyId.
/// </summary>
public sealed class EncryptionOptions
{
    public const string SectionName = "Encryption";
    public const string EnvironmentProvider = "Environment";
    public const string KeyVaultProvider = "KeyVault";
    public const string DefaultEnvironmentKeyId = "env";
    public const string MasterKeyVariable = "TENDERSYNC_MASTER_KEY";

    [Required]
    [RegularExpression("^(Environment|KeyVault)$", ErrorMessage = "Encryption:Provider must be Environment or KeyVault.")]
    public string Provider { get; set; } = EnvironmentProvider;

    /// <summary>Key id new documents are wrapped with. Empty: the single configured key, or "env" for the environment variable.</summary>
    public string ActiveKeyId { get; set; } = string.Empty;

    /// <summary>Environment provider: key id → base64 32-byte key. Keep retired keys here until every document is re-wrapped.</summary>
    public Dictionary<string, string> Keys { get; set; } = new(StringComparer.Ordinal);

    public KeyVaultOptions KeyVault { get; set; } = new();

    /// <summary>Plaintext bytes per authenticated chunk.</summary>
    [Range(4096, 16 * 1024 * 1024)]
    public int ChunkSize { get; set; } = 64 * 1024;

    public sealed class KeyVaultOptions
    {
        /// <summary>https://{vault}.vault.azure.net/keys/{name} (the current version is resolved on start) or a versioned key URI.</summary>
        public string KeyUri { get; set; } = string.Empty;
    }
}
