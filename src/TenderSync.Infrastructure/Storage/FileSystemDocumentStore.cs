using Microsoft.Extensions.Logging;
using TenderSync.Application.Abstractions.Storage;

namespace TenderSync.Infrastructure.Storage;

/// <summary>Stores documents under one root folder. Paths are relative, forward-slash, and may not escape the root. Writes are atomic (temp file + move).</summary>
public sealed class FileSystemDocumentStore : IDocumentStore
{
    private readonly ILogger<FileSystemDocumentStore> _logger;

    public FileSystemDocumentStore(string rootPath, ILogger<FileSystemDocumentStore> logger)
    {
        RootPath = Path.GetFullPath(rootPath);
        _logger = logger;
        Directory.CreateDirectory(RootPath);
    }

    public string RootPath { get; }

    public async Task<string> SaveAsync(string path, Stream content, string? contentType, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(content);
        var full = Resolve(path);
        Directory.CreateDirectory(Path.GetDirectoryName(full)!);

        var temp = $"{full}.{Guid.NewGuid():N}.tmp";
        try
        {
            await using (var file = new FileStream(temp, FileMode.CreateNew, FileAccess.Write, FileShare.None, 81920, FileOptions.Asynchronous))
            {
                await content.CopyToAsync(file, cancellationToken);
            }

            File.Move(temp, full, overwrite: true);
        }
        catch
        {
            TryDelete(temp);
            throw;
        }

        _logger.LogDebug("Stored {Path}", path);
        return Normalise(path);
    }

    public Task<Stream> OpenReadAsync(string path, CancellationToken cancellationToken = default)
    {
        var full = Resolve(path);
        if (!File.Exists(full))
        {
            throw new FileNotFoundException($"Document '{path}' is not in the store.", full);
        }

        Stream stream = new FileStream(full, FileMode.Open, FileAccess.Read, FileShare.Read, 81920, FileOptions.Asynchronous | FileOptions.SequentialScan);
        return Task.FromResult(stream);
    }

    public Task<bool> ExistsAsync(string path, CancellationToken cancellationToken = default) => Task.FromResult(File.Exists(Resolve(path)));

    public Task DeleteAsync(string path, CancellationToken cancellationToken = default)
    {
        TryDelete(Resolve(path));
        return Task.CompletedTask;
    }

    /// <summary>Maps a store path to an absolute file path, rejecting rooted paths and any segment that would leave the root.</summary>
    public string Resolve(string path)
    {
        var normalised = Normalise(path);
        if (normalised.Length == 0 || Path.IsPathRooted(normalised) || normalised.Contains(':', StringComparison.Ordinal) || normalised.Split('/').Any(s => s is "." or ".." || s.Length == 0))
        {
            throw new ArgumentException($"'{path}' is not a valid store path.", nameof(path));
        }

        var full = Path.GetFullPath(Path.Combine(RootPath, normalised));
        if (!full.StartsWith(RootPath + Path.DirectorySeparatorChar, StringComparison.Ordinal))
        {
            throw new ArgumentException($"'{path}' escapes the store root.", nameof(path));
        }

        return full;
    }

    private static string Normalise(string path) => (path ?? string.Empty).Trim().Replace('\\', '/').TrimStart('/');

    private static void TryDelete(string full)
    {
        try
        {
            if (File.Exists(full))
            {
                File.Delete(full);
            }
        }
        catch (IOException)
        {
            // best effort
        }
        catch (UnauthorizedAccessException)
        {
            // best effort
        }
    }
}
