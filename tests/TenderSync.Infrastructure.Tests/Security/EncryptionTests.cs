using System.Security.Cryptography;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using TenderSync.Infrastructure.Security;
using TenderSync.Infrastructure.Storage;

namespace TenderSync.Infrastructure.Tests.Security;

/// <summary>Envelope encryption round trip, tamper detection and master-key rotation (WP09 acceptance).</summary>
public class EncryptionTests
{
    private static readonly string KeyA = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32));
    private static readonly string KeyB = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32));

    private static AesGcmEnvelopeEncryptionService Service(int chunkSize = 4096, string active = "a", params (string Id, string Key)[] keys)
    {
        var options = new EncryptionOptions { ActiveKeyId = active, ChunkSize = chunkSize };
        foreach (var (id, key) in keys.Length == 0 ? [("a", KeyA)] : keys)
        {
            options.Keys[id] = key;
        }

        return new AesGcmEnvelopeEncryptionService(new EnvironmentMasterKeyProvider(Options.Create(options)), Options.Create(options));
    }

    private static async Task<(byte[] Cipher, string WrappedKey, string Sha256, long Length)> EncryptAsync(AesGcmEnvelopeEncryptionService service, byte[] plaintext)
    {
        await using var stream = await service.EncryptAsync(new MemoryStream(plaintext));
        using var buffer = new MemoryStream();
        await stream.CopyToAsync(buffer);
        stream.Completed.Should().BeTrue();
        return (buffer.ToArray(), stream.WrappedDataKey, stream.PlaintextSha256, stream.PlaintextLength);
    }

    private static async Task<byte[]> DecryptAsync(AesGcmEnvelopeEncryptionService service, byte[] cipher, string wrappedKey)
    {
        await using var stream = await service.DecryptAsync(new MemoryStream(cipher), wrappedKey);
        using var buffer = new MemoryStream();
        await stream.CopyToAsync(buffer);
        return buffer.ToArray();
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(4095)]
    [InlineData(4096)]
    [InlineData(4097)]
    [InlineData(8192)]
    [InlineData(100_003)]
    public async Task Round_trip_preserves_every_byte_and_reports_the_plaintext_digest(int size)
    {
        var service = Service();
        var plaintext = RandomNumberGenerator.GetBytes(size);

        var (cipher, wrappedKey, sha256, length) = await EncryptAsync(service, plaintext);

        cipher.Length.Should().BeGreaterThan(size, "the header, lengths and tags add bytes");
        cipher.Take(4).Should().Equal("TSE1"u8.ToArray());
        length.Should().Be(size);
        sha256.Should().Be(Convert.ToHexStringLower(SHA256.HashData(plaintext)));
        wrappedKey.Should().StartWith("v1|a|");
        service.KeyIdOf(wrappedKey).Should().Be("a");
        (await DecryptAsync(service, cipher, wrappedKey)).Should().Equal(plaintext);
    }

    [Fact]
    public async Task Synchronous_reads_in_odd_sizes_give_the_same_result()
    {
        var service = Service(chunkSize: 4096);
        var plaintext = RandomNumberGenerator.GetBytes(10_000);
        await using var encrypting = await service.EncryptAsync(new MemoryStream(plaintext));
        using var cipher = new MemoryStream();
        var buffer = new byte[777];
        int read;
        while ((read = encrypting.Read(buffer, 0, buffer.Length)) > 0)
        {
            cipher.Write(buffer, 0, read);
        }

        await using var decrypting = await service.DecryptAsync(new MemoryStream(cipher.ToArray()), encrypting.WrappedDataKey);
        using var plain = new MemoryStream();
        while ((read = decrypting.Read(buffer, 0, 333)) > 0)
        {
            plain.Write(buffer, 0, read);
        }

        plain.ToArray().Should().Equal(plaintext);
    }

    [Fact]
    public async Task Each_file_gets_its_own_data_key_so_equal_plaintexts_differ()
    {
        var service = Service();
        var plaintext = "same content"u8.ToArray();

        var first = await EncryptAsync(service, plaintext);
        var second = await EncryptAsync(service, plaintext);

        first.Cipher.Should().NotEqual(second.Cipher);
        first.WrappedKey.Should().NotBe(second.WrappedKey);
    }

    [Fact]
    public async Task A_modified_byte_a_dropped_chunk_and_a_swapped_key_are_all_rejected()
    {
        var service = Service(chunkSize: 4096);
        var (cipher, wrappedKey, _, _) = await EncryptAsync(service, RandomNumberGenerator.GetBytes(10_000));

        var tampered = (byte[])cipher.Clone();
        tampered[^30] ^= 0x01;
        await FluentActions.Awaiting(() => DecryptAsync(service, tampered, wrappedKey)).Should().ThrowAsync<CryptographicException>().WithMessage("*authentication*");

        var truncated = cipher[..(cipher.Length - (10_000 - 8192) - 16 - 4)];
        await FluentActions.Awaiting(() => DecryptAsync(service, truncated, wrappedKey)).Should().ThrowAsync<CryptographicException>().WithMessage("*truncated*");

        var other = await EncryptAsync(service, "other"u8.ToArray());
        await FluentActions.Awaiting(() => DecryptAsync(service, cipher, other.WrappedKey)).Should().ThrowAsync<CryptographicException>();

        await FluentActions.Awaiting(() => DecryptAsync(service, "not a tendersync file at all"u8.ToArray(), wrappedKey)).Should().ThrowAsync<CryptographicException>().WithMessage("*not a TenderSync*");
    }

    [Fact]
    public async Task Rotation_rewraps_the_data_key_under_the_new_master_key_without_touching_the_ciphertext()
    {
        var before = Service(active: "a", keys: [("a", KeyA)]);
        var plaintext = RandomNumberGenerator.GetBytes(5000);
        var (cipher, wrappedA, _, _) = await EncryptAsync(before, plaintext);

        // Step 2 of the runbook: both keys present, B active.
        var rotating = Service(active: "b", keys: [("a", KeyA), ("b", KeyB)]);
        rotating.ActiveKeyId.Should().Be("b");
        var wrappedB = await rotating.RewrapAsync(wrappedA);
        rotating.KeyIdOf(wrappedB).Should().Be("b");
        wrappedB.Should().NotBe(wrappedA);

        // Step 4: the old key is removed; the re-wrapped document still opens, the old wrapping does not.
        var after = Service(active: "b", keys: [("b", KeyB)]);
        (await DecryptAsync(after, cipher, wrappedB)).Should().Equal(plaintext);
        await FluentActions.Awaiting(() => DecryptAsync(after, cipher, wrappedA)).Should().ThrowAsync<InvalidOperationException>().WithMessage("*'a' is not configured*");
    }

    [Fact]
    public void Misconfigured_keys_fail_on_first_use_with_a_clear_message()
    {
        var none = new EnvironmentMasterKeyProvider(Options.Create(new EncryptionOptions()));
        FluentActions.Invoking(() => none.ActiveKeyId).Should().Throw<InvalidOperationException>().WithMessage("*No master key is configured*");

        var tooShort = new EnvironmentMasterKeyProvider(Options.Create(new EncryptionOptions { Keys = { ["a"] = Convert.ToBase64String(new byte[16]) } }));
        FluentActions.Invoking(() => tooShort.ActiveKeyId).Should().Throw<InvalidOperationException>().WithMessage("*32 bytes*");

        var ambiguous = new EnvironmentMasterKeyProvider(Options.Create(new EncryptionOptions { Keys = { ["a"] = KeyA, ["b"] = KeyB } }));
        FluentActions.Invoking(() => ambiguous.ActiveKeyId).Should().Throw<InvalidOperationException>().WithMessage("*ActiveKeyId*");

        var wrongActive = new EnvironmentMasterKeyProvider(Options.Create(new EncryptionOptions { ActiveKeyId = "zz", Keys = { ["a"] = KeyA } }));
        FluentActions.Invoking(() => wrongActive.ActiveKeyId).Should().Throw<InvalidOperationException>().WithMessage("*'zz'*");

        var single = new EnvironmentMasterKeyProvider(Options.Create(new EncryptionOptions { Keys = { ["only"] = KeyA } }));
        single.ActiveKeyId.Should().Be("only", "a single key needs no ActiveKeyId");
    }

    [Fact]
    public async Task A_wrapped_key_cannot_be_replayed_under_another_key_id()
    {
        var provider = new EnvironmentMasterKeyProvider(Options.Create(new EncryptionOptions { ActiveKeyId = "a", Keys = { ["a"] = KeyA, ["same"] = KeyA } }));
        var wrapped = await provider.WrapAsync("a", RandomNumberGenerator.GetBytes(32));

        await FluentActions.Awaiting(() => provider.UnwrapAsync("same", wrapped)).Should().ThrowAsync<CryptographicException>("the key id is authenticated data");
    }

    [Fact]
    public async Task Encrypted_documents_round_trip_through_the_filesystem_store()
    {
        var root = Path.Combine(Path.GetTempPath(), "tendersync-tests", Guid.NewGuid().ToString("N"));
        try
        {
            var store = new FileSystemDocumentStore(root, NullLogger<FileSystemDocumentStore>.Instance);
            var service = Service(chunkSize: 4096);
            var plaintext = RandomNumberGenerator.GetBytes(20_000);
            var path = $"companies/{Guid.CreateVersion7()}/{Guid.CreateVersion7()}.enc";

            string wrappedKey;
            await using (var encrypting = await service.EncryptAsync(new MemoryStream(plaintext)))
            {
                await store.SaveAsync(path, encrypting, "application/octet-stream");
                wrappedKey = encrypting.WrappedDataKey;
            }

            var onDisk = await File.ReadAllBytesAsync(Path.Combine(root, path.Replace('/', Path.DirectorySeparatorChar)));
            onDisk.AsSpan().IndexOf(plaintext.AsSpan(0, 64)).Should().Be(-1, "nothing on disk is plaintext");

            await using var decrypting = await service.DecryptAsync(await store.OpenReadAsync(path), wrappedKey);
            using var buffer = new MemoryStream();
            await decrypting.CopyToAsync(buffer);
            buffer.ToArray().Should().Equal(plaintext);
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }
}
