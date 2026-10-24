using System.Buffers.Binary;
using System.Security.Cryptography;
using Microsoft.Extensions.Options;
using TenderSync.Application.Abstractions.Security;

namespace TenderSync.Infrastructure.Security;

/// <summary>
/// Envelope encryption (ADR-013). File format: "TSE1", chunk size (int32 LE), 8-byte nonce prefix; then chunks of
/// [plaintext length int32 LE][ciphertext][16-byte tag]. Chunk i uses nonce prefix + i (uint32 BE) and authenticates its
/// index and a final flag, so chunks cannot be reordered, dropped or truncated without the decryptor noticing.
/// The wrapped data key is "v1|{masterKeyId}|{base64}".
/// </summary>
public sealed class AesGcmEnvelopeEncryptionService(IMasterKeyProvider masterKeys, IOptions<EncryptionOptions> options) : IEncryptionService
{
    public const string FormatVersion = "v1";
    private const int KeySize = 32;
    private const int TagSize = 16;
    private const int NoncePrefixSize = 8;
    private static readonly byte[] Magic = "TSE1"u8.ToArray();

    public string ActiveKeyId => masterKeys.ActiveKeyId;

    public async Task<EncryptingStream> EncryptAsync(Stream plaintext, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(plaintext);
        var dataKey = RandomNumberGenerator.GetBytes(KeySize);
        var keyId = masterKeys.ActiveKeyId;
        var wrapped = await masterKeys.WrapAsync(keyId, dataKey, cancellationToken);
        return new EncryptingReadStream(plaintext, dataKey, Encode(keyId, wrapped), options.Value.ChunkSize);
    }

    public async Task<Stream> DecryptAsync(Stream ciphertext, string wrappedDataKey, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(ciphertext);
        var (keyId, wrapped) = Decode(wrappedDataKey);
        var dataKey = await masterKeys.UnwrapAsync(keyId, wrapped, cancellationToken);
        return new DecryptingReadStream(ciphertext, dataKey);
    }

    public async Task<string> RewrapAsync(string wrappedDataKey, CancellationToken cancellationToken = default)
    {
        var (keyId, wrapped) = Decode(wrappedDataKey);
        var dataKey = await masterKeys.UnwrapAsync(keyId, wrapped, cancellationToken);
        try
        {
            var active = masterKeys.ActiveKeyId;
            return Encode(active, await masterKeys.WrapAsync(active, dataKey, cancellationToken));
        }
        finally
        {
            CryptographicOperations.ZeroMemory(dataKey);
        }
    }

    public string KeyIdOf(string wrappedDataKey) => Decode(wrappedDataKey).KeyId;

    private static string Encode(string keyId, byte[] wrapped) => $"{FormatVersion}|{keyId}|{Convert.ToBase64String(wrapped)}";

    private static (string KeyId, byte[] Wrapped) Decode(string wrappedDataKey)
    {
        var parts = (wrappedDataKey ?? string.Empty).Split('|', 3);
        if (parts.Length != 3 || parts[0] != FormatVersion || parts[1].Length == 0)
        {
            throw new CryptographicException("The stored data key is not in the expected v1|keyId|base64 format.");
        }

        return (parts[1], Convert.FromBase64String(parts[2]));
    }

    private static void FillNonce(Span<byte> nonce, ReadOnlySpan<byte> prefix, uint index)
    {
        prefix.CopyTo(nonce);
        BinaryPrimitives.WriteUInt32BigEndian(nonce[NoncePrefixSize..], index);
    }

    private static void FillAssociatedData(Span<byte> aad, uint index, bool final)
    {
        BinaryPrimitives.WriteUInt32BigEndian(aad, index);
        aad[4] = final ? (byte)1 : (byte)0;
    }

    /// <summary>Produces the ciphertext stream lazily, chunk by chunk, hashing the plaintext as it goes.</summary>
    private sealed class EncryptingReadStream : EncryptingStream
    {
        private readonly Stream _plaintext;
        private readonly AesGcm _aes;
        private readonly byte[] _noncePrefix = RandomNumberGenerator.GetBytes(NoncePrefixSize);
        private readonly int _chunkSize;
        private readonly byte[] _plainBuffer;
        private readonly IncrementalHash _hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        private byte[] _pending;
        private int _pendingOffset;
        private int _pendingLength;
        private uint _chunkIndex;
        private bool _headerWritten;
        private bool _finalEmitted;
        private bool _sourceDrained;
        private long _plaintextLength;
        private string? _sha256;

        public EncryptingReadStream(Stream plaintext, byte[] dataKey, string wrappedDataKey, int chunkSize)
        {
            _plaintext = plaintext;
            _aes = new AesGcm(dataKey, TagSize);
            CryptographicOperations.ZeroMemory(dataKey);
            WrappedDataKey = wrappedDataKey;
            _chunkSize = chunkSize;
            _plainBuffer = new byte[chunkSize];
            _pending = new byte[Magic.Length + sizeof(int) + NoncePrefixSize + sizeof(int) + chunkSize + TagSize];
        }

        public override string WrappedDataKey { get; }

        public override string PlaintextSha256 => _sha256 ?? throw new InvalidOperationException("The plaintext has not been fully read yet.");

        public override long PlaintextLength => Completed ? _plaintextLength : throw new InvalidOperationException("The plaintext has not been fully read yet.");

        public override bool Completed => _finalEmitted && _pendingLength == 0;

        public override bool CanRead => true;

        public override bool CanSeek => false;

        public override bool CanWrite => false;

        public override long Length => throw new NotSupportedException();

        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }

        public override int Read(byte[] buffer, int offset, int count) => Read(buffer.AsSpan(offset, count));

        public override int Read(Span<byte> buffer)
        {
            if (_pendingLength == 0 && !_finalEmitted)
            {
                var read = _sourceDrained ? 0 : ReadPlaintext(_plainBuffer);
                Produce(read);
            }

            return Serve(buffer);
        }

        public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            if (_pendingLength == 0 && !_finalEmitted)
            {
                var read = _sourceDrained ? 0 : await ReadPlaintextAsync(_plainBuffer, cancellationToken);
                Produce(read);
            }

            return Serve(buffer.Span);
        }

        public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken) =>
            ReadAsync(buffer.AsMemory(offset, count), cancellationToken).AsTask();

        public override void Flush()
        {
        }

        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

        public override void SetLength(long value) => throw new NotSupportedException();

        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                _aes.Dispose();
                _hash.Dispose();
                _plaintext.Dispose();
            }

            base.Dispose(disposing);
        }

        /// <summary>Fills the buffer as far as the source allows so every chunk but the last is full.</summary>
        private int ReadPlaintext(Span<byte> buffer)
        {
            var total = 0;
            while (total < buffer.Length)
            {
                var read = _plaintext.Read(buffer[total..]);
                if (read == 0)
                {
                    _sourceDrained = true;
                    break;
                }

                total += read;
            }

            return total;
        }

        private async ValueTask<int> ReadPlaintextAsync(Memory<byte> buffer, CancellationToken cancellationToken)
        {
            var total = 0;
            while (total < buffer.Length)
            {
                var read = await _plaintext.ReadAsync(buffer[total..], cancellationToken);
                if (read == 0)
                {
                    _sourceDrained = true;
                    break;
                }

                total += read;
            }

            return total;
        }

        private void Produce(int plaintextLength)
        {
            var final = plaintextLength < _chunkSize || _sourceDrained;
            var offset = 0;
            if (!_headerWritten)
            {
                Magic.CopyTo(_pending, 0);
                BinaryPrimitives.WriteInt32LittleEndian(_pending.AsSpan(Magic.Length), _chunkSize);
                _noncePrefix.CopyTo(_pending, Magic.Length + sizeof(int));
                offset = Magic.Length + sizeof(int) + NoncePrefixSize;
                _headerWritten = true;
            }

            _hash.AppendData(_plainBuffer, 0, plaintextLength);
            _plaintextLength += plaintextLength;

            Span<byte> nonce = stackalloc byte[12];
            Span<byte> aad = stackalloc byte[5];
            FillNonce(nonce, _noncePrefix, _chunkIndex);
            FillAssociatedData(aad, _chunkIndex, final);

            BinaryPrimitives.WriteInt32LittleEndian(_pending.AsSpan(offset), plaintextLength);
            offset += sizeof(int);
            var ciphertext = _pending.AsSpan(offset, plaintextLength);
            var tag = _pending.AsSpan(offset + plaintextLength, TagSize);
            _aes.Encrypt(nonce, _plainBuffer.AsSpan(0, plaintextLength), ciphertext, tag, aad);
            offset += plaintextLength + TagSize;

            _pendingOffset = 0;
            _pendingLength = offset;
            _chunkIndex++;
            if (final)
            {
                _finalEmitted = true;
                _sha256 = Convert.ToHexStringLower(_hash.GetHashAndReset());
            }
        }

        private int Serve(Span<byte> buffer)
        {
            var n = Math.Min(buffer.Length, _pendingLength);
            _pending.AsSpan(_pendingOffset, n).CopyTo(buffer);
            _pendingOffset += n;
            _pendingLength -= n;
            return n;
        }
    }

    /// <summary>Reads the ciphertext chunk by chunk, verifying each tag before handing plaintext out.</summary>
    private sealed class DecryptingReadStream : Stream
    {
        private readonly Stream _ciphertext;
        private readonly AesGcm _aes;
        private readonly byte[] _noncePrefix = new byte[NoncePrefixSize];
        private int _chunkSize;
        private byte[] _cipherBuffer = [];
        private byte[] _plainBuffer = [];
        private int _plainOffset;
        private int _plainLength;
        private uint _chunkIndex;
        private bool _headerRead;
        private bool _finalSeen;

        public DecryptingReadStream(Stream ciphertext, byte[] dataKey)
        {
            _ciphertext = ciphertext;
            _aes = new AesGcm(dataKey, TagSize);
            CryptographicOperations.ZeroMemory(dataKey);
        }

        public override bool CanRead => true;

        public override bool CanSeek => false;

        public override bool CanWrite => false;

        public override long Length => throw new NotSupportedException();

        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }

        public override int Read(byte[] buffer, int offset, int count) => Read(buffer.AsSpan(offset, count));

        public override int Read(Span<byte> buffer)
        {
            while (_plainLength == 0)
            {
                if (_finalSeen)
                {
                    return 0;
                }

                if (!_headerRead)
                {
                    var header = new byte[Magic.Length + sizeof(int) + NoncePrefixSize];
                    ReadCipherExactly(header);
                    ParseHeader(header);
                }

                var lengthBytes = new byte[sizeof(int)];
                if (!TryReadCipherExactly(lengthBytes))
                {
                    throw new CryptographicException("The encrypted document ends before its final chunk; it is truncated or corrupt.");
                }

                var length = ParseLength(lengthBytes);
                ReadCipherExactly(_cipherBuffer.AsSpan(0, length + TagSize));
                DecryptChunk(length);
            }

            return Serve(buffer);
        }

        public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            while (_plainLength == 0)
            {
                if (_finalSeen)
                {
                    return 0;
                }

                if (!_headerRead)
                {
                    var header = new byte[Magic.Length + sizeof(int) + NoncePrefixSize];
                    await ReadCipherExactlyAsync(header, cancellationToken);
                    ParseHeader(header);
                }

                var lengthBytes = new byte[sizeof(int)];
                if (!await TryReadCipherExactlyAsync(lengthBytes, cancellationToken))
                {
                    throw new CryptographicException("The encrypted document ends before its final chunk; it is truncated or corrupt.");
                }

                var length = ParseLength(lengthBytes);
                await ReadCipherExactlyAsync(_cipherBuffer.AsMemory(0, length + TagSize), cancellationToken);
                DecryptChunk(length);
            }

            return Serve(buffer.Span);
        }

        public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken) =>
            ReadAsync(buffer.AsMemory(offset, count), cancellationToken).AsTask();

        public override void Flush()
        {
        }

        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

        public override void SetLength(long value) => throw new NotSupportedException();

        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                _aes.Dispose();
                _ciphertext.Dispose();
            }

            base.Dispose(disposing);
        }

        private void ParseHeader(ReadOnlySpan<byte> header)
        {
            if (!header[..Magic.Length].SequenceEqual(Magic))
            {
                throw new CryptographicException("The stored document is not a TenderSync encrypted file.");
            }

            _chunkSize = BinaryPrimitives.ReadInt32LittleEndian(header[Magic.Length..]);
            if (_chunkSize is <= 0 or > 64 * 1024 * 1024)
            {
                throw new CryptographicException("The encrypted document declares an invalid chunk size.");
            }

            header.Slice(Magic.Length + sizeof(int), NoncePrefixSize).CopyTo(_noncePrefix);
            _cipherBuffer = new byte[_chunkSize + TagSize];
            _plainBuffer = new byte[_chunkSize];
            _headerRead = true;
        }

        private int ParseLength(ReadOnlySpan<byte> lengthBytes)
        {
            var length = BinaryPrimitives.ReadInt32LittleEndian(lengthBytes);
            if (length < 0 || length > _chunkSize)
            {
                throw new CryptographicException("The encrypted document declares an invalid chunk length.");
            }

            return length;
        }

        private void DecryptChunk(int length)
        {
            var final = length < _chunkSize;
            Span<byte> nonce = stackalloc byte[12];
            Span<byte> aad = stackalloc byte[5];
            FillNonce(nonce, _noncePrefix, _chunkIndex);
            FillAssociatedData(aad, _chunkIndex, final);
            try
            {
                _aes.Decrypt(nonce, _cipherBuffer.AsSpan(0, length), _cipherBuffer.AsSpan(length, TagSize), _plainBuffer.AsSpan(0, length), aad);
            }
            catch (AuthenticationTagMismatchException ex)
            {
                // A full-size chunk may still be the final one when the plaintext length is an exact multiple; retry with the final flag.
                if (final)
                {
                    throw new CryptographicException("The encrypted document failed authentication; it has been modified or the wrong key was used.", ex);
                }

                FillAssociatedData(aad, _chunkIndex, true);
                try
                {
                    _aes.Decrypt(nonce, _cipherBuffer.AsSpan(0, length), _cipherBuffer.AsSpan(length, TagSize), _plainBuffer.AsSpan(0, length), aad);
                    final = true;
                }
                catch (AuthenticationTagMismatchException inner)
                {
                    throw new CryptographicException("The encrypted document failed authentication; it has been modified or the wrong key was used.", inner);
                }
            }

            _plainOffset = 0;
            _plainLength = length;
            _chunkIndex++;
            _finalSeen = final;
        }

        private int Serve(Span<byte> buffer)
        {
            var n = Math.Min(buffer.Length, _plainLength);
            _plainBuffer.AsSpan(_plainOffset, n).CopyTo(buffer);
            _plainOffset += n;
            _plainLength -= n;
            return n;
        }

        private void ReadCipherExactly(Span<byte> buffer)
        {
            if (!TryReadCipherExactly(buffer))
            {
                throw new CryptographicException("The encrypted document is truncated.");
            }
        }

        private bool TryReadCipherExactly(Span<byte> buffer)
        {
            var total = 0;
            while (total < buffer.Length)
            {
                var read = _ciphertext.Read(buffer[total..]);
                if (read == 0)
                {
                    return total == 0 ? false : throw new CryptographicException("The encrypted document is truncated.");
                }

                total += read;
            }

            return true;
        }

        private async ValueTask ReadCipherExactlyAsync(Memory<byte> buffer, CancellationToken cancellationToken)
        {
            if (!await TryReadCipherExactlyAsync(buffer, cancellationToken))
            {
                throw new CryptographicException("The encrypted document is truncated.");
            }
        }

        private async ValueTask<bool> TryReadCipherExactlyAsync(Memory<byte> buffer, CancellationToken cancellationToken)
        {
            var total = 0;
            while (total < buffer.Length)
            {
                var read = await _ciphertext.ReadAsync(buffer[total..], cancellationToken);
                if (read == 0)
                {
                    return total == 0 ? false : throw new CryptographicException("The encrypted document is truncated.");
                }

                total += read;
            }

            return true;
        }
    }
}
