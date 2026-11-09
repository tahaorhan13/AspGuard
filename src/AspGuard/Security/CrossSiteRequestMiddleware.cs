using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.Net.Http.Headers;

namespace AspGuard.Security;

/// <summary>
/// CSRF protection without tokens: a state-changing request that carries cookies must come from the same origin
/// (<c>Sec-Fetch-Site: same-origin</c>/<c>none</c>, or an <c>Origin</c> matching the <c>Host</c>) or from a trusted origin.
/// Requests without cookies cannot ride on a victim's session and are never blocked.
/// </summary>
internal sealed partial class CrossSiteRequestMiddleware
{
    private readonly RequestDelegate _next;
    private readonly ILogger _logger;
    private readonly HashSet<string> _trusted;

    public CrossSiteRequestMiddleware(RequestDelegate next, IOptions<AspGuardOptions> options, ILogger<CrossSiteRequestMiddleware> logger)
    {
        _next = next;
        _logger = logger;
        _trusted = SecurityRules.TrustedOrigins(options.Value.Security.CrossSiteProtection.TrustedOrigins);
    }

    public Task InvokeAsync(HttpContext context)
    {
        if (IsAllowed(context.Request))
        {
            return _next(context);
        }

        var request = context.Request;
        LogBlocked(_logger, request.Method, request.Path.Value, request.Headers.Origin.ToString(), request.Headers["Sec-Fetch-Site"].ToString());
        context.Response.StatusCode = StatusCodes.Status403Forbidden;
        return context.Response.WriteAsync("Cross-site request blocked.");
    }

    internal bool IsAllowed(HttpRequest request) =>
        SecurityRules.IsCrossSiteRequestAllowed(
            request.Method,
            request.Headers.ContainsKey(HeaderNames.Cookie),
            request.Headers.Origin.ToString(),
            request.Headers["Sec-Fetch-Site"].ToString(),
            request.Host.Value ?? string.Empty,
            _trusted);

    [LoggerMessage(EventId = 100, Level = LogLevel.Warning, Message = "AspGuard blocked a cross-site {Method} request to {Path} (Origin: '{Origin}', Sec-Fetch-Site: '{FetchSite}').")]
    private static partial void LogBlocked(ILogger logger, string method, string? path, string origin, string fetchSite);
}
