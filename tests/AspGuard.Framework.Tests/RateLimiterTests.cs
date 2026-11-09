using AspGuard.SystemWeb;

namespace AspGuard.Framework.Tests;

public class RateLimiterTests
{
    [Fact]
    public void Each_client_has_its_own_window()
    {
        var now = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        var limiter = new FixedWindowRateLimiter(1, TimeSpan.FromSeconds(10), () => now);

        Assert.True(limiter.TryAcquire("a", out _));
        Assert.False(limiter.TryAcquire("a", out var retryAfter));
        Assert.Equal(TimeSpan.FromSeconds(10), retryAfter);
        Assert.True(limiter.TryAcquire("b", out _));

        now = now.AddSeconds(4);
        Assert.False(limiter.TryAcquire("a", out retryAfter));
        Assert.Equal(TimeSpan.FromSeconds(6), retryAfter);

        now = now.AddSeconds(6);
        Assert.True(limiter.TryAcquire("a", out _));
    }

    [Fact]
    public async Task Concurrent_requests_never_exceed_the_limit()
    {
        var limiter = new FixedWindowRateLimiter(100, TimeSpan.FromHours(1));
        var granted = 0;

        await Task.WhenAll(Enumerable.Range(0, 500).Select(i => Task.Run(() =>
        {
            if (limiter.TryAcquire("client", out _))
            {
                Interlocked.Increment(ref granted);
            }
        })));

        Assert.Equal(100, granted);
    }
}
