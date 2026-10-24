namespace TenderSync.Application.Abstractions.Security;

/// <summary>
/// Envelope encryption for company documents (PLAN.md §12, ADR-013): a random data key per file, AES-256-GCM over the
/// content, the data key wrapped by the active master key. Wrapped keys carry the master key id so rotation can re-wrap
/// them without touching the ciphertext. Both directions stream: nothing is written to disk unencrypted.
/// </summary>
public interface IEncryptionService
{
    /// <summary>Id of the master key new documents are wrapped with.</summary>
    string ActiveKeyId { get; }

    /// <summary>Starts encrypting <paramref name="plaintext"/>: the returned stream yields ciphertext as it is read and reports the digest once drained.</summary>
    Task<EncryptingStream> EncryptAsync(Stream plaintext, CancellationToken cancellationToken = default);

    /// <summary>Opens a stream that decrypts <paramref name="ciphertext"/> as it is read, verifying every chunk's authentication tag.</summary>
    Task<Stream> DecryptAsync(Stream ciphertext, string wrappedDataKey, CancellationToken cancellationToken = default);

    /// <summary>Unwraps with the key named in <paramref name="wrappedDataKey"/> and wraps again with the active master key.</summary>
    Task<string> RewrapAsync(string wrappedDataKey, CancellationToken cancellationToken = default);

    /// <summary>The master key id a wrapped data key was produced with.</summary>
    string KeyIdOf(string wrappedDataKey);
}

/// <summary>A readable ciphertext stream. The digest members are available once the stream has been read to its end.</summary>
public abstract class EncryptingStream : Stream
{
    /// <summary>The data key wrapped by the master key, to store on the document.</summary>
    public abstract string WrappedDataKey { get; }

    /// <summary>SHA-256 of the plaintext, lowercase hex.</summary>
    public abstract string PlaintextSha256 { get; }

    public abstract long PlaintextLength { get; }

    /// <summary>True once the plaintext has been consumed and the final chunk emitted.</summary>
    public abstract bool Completed { get; }
}
