using System.Diagnostics;
using Microsoft.Extensions.Options;
using TenderSync.Application.Features.Documents;

namespace TenderSync.Infrastructure.Documents;

/// <summary>
/// Polite outbound throttling for eTenders downloads (PLAN.md §2 and §7.3): at most N concurrent downloads and at least the
/// configured spacing between consecutive starts, process-wide (singleton).
/// </summary>
public sealed class DownloadThrottle : IDisposable
{
    private readonly SemaphoreSlim _slots;
    private readonly TimeSpan _spacing;
    private readonly Lock _gate = new();
    private long _nextStartTimestamp;

    public DownloadThrottle(IOptions<DocumentOptions> options)
        : this(options.Value.MaxConcurrentDownloads, TimeSpan.FromMilliseconds(options.Value.DownloadSpacingMs))
    {
    }

    public DownloadThrottle(int maxConcurrent, TimeSpan spacing)
    {
        _slots = new SemaphoreSlim(maxConcurrent, maxConcurrent);
        _spacing = spacing;
    }

    public int Available => _slots.CurrentCount;

    /// <summary>Takes a slot and waits out the spacing. Dispose the returned lease when the download ends.</summary>
    public async Task<IDisposable> AcquireAsync(CancellationToken cancellationToken)
    {
        await _slots.WaitAsync(cancellationToken);
        try
        {
            TimeSpan delay;
            lock (_gate)
            {
                var now = Stopwatch.GetTimestamp();
                var start = Math.Max(now, _nextStartTimestamp);
                delay = Stopwatch.GetElapsedTime(now, start);
                _nextStartTimestamp = start + (long)(_spacing.TotalSeconds * Stopwatch.Frequency);
            }

            if (delay > TimeSpan.Zero)
            {
                await Task.Delay(delay, cancellationToken);
            }
        }
        catch
        {
            _slots.Release();
            throw;
        }

        return new Lease(_slots);
    }

    public void Dispose() => _slots.Dispose();

    private sealed class Lease(SemaphoreSlim slots) : IDisposable
    {
        private int _released;

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _released, 1) == 0)
            {
                slots.Release();
            }
        }
    }
}
