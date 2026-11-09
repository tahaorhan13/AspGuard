using System.Collections.Concurrent;

namespace AspGuard.SystemWeb;

/// <summary>
/// Fixed-window counter per client key (System.Threading.RateLimiting is not available on .NET Framework).
/// Expired windows are swept periodically so memory stays bounded by the number of active clients.
/// </summary>
internal sealed class FixedWindowRateLimiter(int permitLimit, TimeSpan window, Func<DateTime>? clock = null)
{
    private readonly ConcurrentDictionary<string, Counter> _counters = new(StringComparer.Ordinal);
    private readonly Func<DateTime> _clock = clock ?? (() => DateTime.UtcNow);
    private long _nextSweepTicks;

    public int PermitLimit { get; } = permitLimit;

    public TimeSpan Window { get; } = window;

    /// <summary>Takes a permit for <paramref name="key"/>. When none is left, <paramref name="retryAfter"/> says when the window resets.</summary>
    public bool TryAcquire(string key, out TimeSpan retryAfter)
    {
        var now = _clock();
        Sweep(now);

        var counter = _counters.GetOrAdd(key, _ => new Counter(now));
        lock (counter)
        {
            if (now - counter.WindowStart >= Window)
            {
                counter.WindowStart = now;
                counter.Count = 0;
            }

            if (counter.Count < PermitLimit)
            {
                counter.Count++;
                retryAfter = TimeSpan.Zero;
                return true;
            }

            retryAfter = counter.WindowStart + Window - now;
            return false;
        }
    }

    private void Sweep(DateTime now)
    {
        var next = Interlocked.Read(ref _nextSweepTicks);
        if (now.Ticks < next || Interlocked.CompareExchange(ref _nextSweepTicks, now.Ticks + Window.Ticks, next) != next)
        {
            return;
        }

        foreach (var pair in _counters)
        {
            if (now - pair.Value.WindowStart >= Window)
            {
                _counters.TryRemove(pair.Key, out _);
            }
        }
    }

    private sealed class Counter(DateTime windowStart)
    {
        public DateTime WindowStart { get; set; } = windowStart;

        public int Count { get; set; }
    }
}
