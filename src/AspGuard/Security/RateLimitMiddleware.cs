using System.Globalization;
using System.Net;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace AspGuard.Security;

/// <summary>Owns the per-client limiter so the container disposes it with the host.</summary>
internal sealed class ClientRateLimiter : IDisposable
{
    public ClientRateLimiter(IOptions<AspGuardOptions> options)
    {
        var settings = options.Value.Security.RateLimit;
        Limiter = PartitionedRateLimiter.Create<HttpContext, string>(context =>
            RateLimitPartition.GetFixedWindowLimiter(
                PartitionKey(context),
                _ => new FixedWindowRateLimiterOptions
                {
                    PermitLimit = settings.PermitLimit,
                    Window = settings.Window,
                    QueueLimit = 0,
                    AutoReplenishment = true,
                }));
    }

    public PartitionedRateLimiter<HttpContext> Limiter { get; }

    public void Dispose() => Limiter.Dispose();

    internal static string PartitionKey(HttpContext context)
    {
        var address = context.Connection.RemoteIpAddress;
        if (address is null)
        {
            return "unknown";
        }

        return (address.IsIPv4MappedToIPv6 ? address.MapToIPv4() : address).ToString();
    }
}

/// <summary>Fixed-window rate limiting per client IP; answers 429 with <c>Retry-After</c> when the window is used up.</summary>
internal sealed partial class RateLimitMiddleware(
    RequestDelegate next,
    ClientRateLimiter limiter,
    IOptions<AspGuardOptions> options,
    ILogger<RateLimitMiddleware> logger)
{
    private readonly PathString[] _excluded = options.Value.Security.RateLimit.ExcludedPaths
        .Where(p => !string.IsNullOrWhiteSpace(p))
        .Select(p => new PathString(p.StartsWith('/') ? p.TrimEnd('/') : "/" + p.TrimEnd('/')))
        .ToArray();

    public async Task InvokeAsync(HttpContext context)
    {
        foreach (var path in _excluded)
        {
            if (context.Request.Path.StartsWithSegments(path, StringComparison.OrdinalIgnoreCase))
            {
                await next(context);
                return;
            }
        }

        using var lease = limiter.Limiter.AttemptAcquire(context);
        if (lease.IsAcquired)
        {
            await next(context);
            return;
        }

        LogRejected(logger, ClientRateLimiter.PartitionKey(context), context.Request.Path.Value);
        context.Response.StatusCode = (int)HttpStatusCode.TooManyRequests;
        if (lease.TryGetMetadata(MetadataName.RetryAfter, out var retryAfter))
        {
            context.Response.Headers.RetryAfter = Math.Max(1, (int)Math.Ceiling(retryAfter.TotalSeconds)).ToString(CultureInfo.InvariantCulture);
        }

        await context.Response.WriteAsync("Too many requests.");
    }

    [LoggerMessage(EventId = 101, Level = LogLevel.Warning, Message = "AspGuard rate limit exceeded for client {Client} on {Path}.")]
    private static partial void LogRejected(ILogger logger, string client, string? path);
}
