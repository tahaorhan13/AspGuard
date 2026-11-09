using System.Globalization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Options;

namespace AspGuard;

/// <summary>Wraps every HTTP request in an <see cref="AspGuardScope"/> and optionally adds summary response headers.</summary>
internal sealed class QueryScopeMiddleware(RequestDelegate next, IOptions<AspGuardOptions> options, AspGuardRuntime runtime)
{
    internal const string CountHeader = "X-AspGuard-Query-Count";
    internal const string FindingsHeader = "X-AspGuard-Query-Findings";

    private readonly AspGuardOptions _options = options.Value;

    public async Task InvokeAsync(HttpContext context)
    {
        if (!_options.EnableQueryDetection)
        {
            await next(context);
            return;
        }

        // An ambient scope (e.g. one a test started around an in-process request) owns the queries; reuse it so its
        // assertions see them. Otherwise the request is its own scope.
        var ambient = AspGuardScope.Current;
        using var owned = ambient is null ? AspGuardScope.Begin($"{context.Request.Method} {context.Request.Path.Value}", runtime) : null;
        var scope = ambient ?? owned!;
        scope.Bind(runtime);

        if (_options.AddResponseHeaders)
        {
            context.Response.OnStarting(
                static state =>
                {
                    var (httpContext, requestScope) = ((HttpContext, AspGuardScope))state;
                    httpContext.Response.Headers[CountHeader] = requestScope.QueryCount.ToString(CultureInfo.InvariantCulture);
                    httpContext.Response.Headers[FindingsHeader] = requestScope.GetFindings().Count.ToString(CultureInfo.InvariantCulture);
                    return Task.CompletedTask;
                },
                (context, scope));
        }

        try
        {
            await next(context);
        }
        finally
        {
            // This middleware runs before routing; once the endpoint is known, the route pattern gives a low-cardinality
            // name for the end-of-request report. The query string is never included.
            if (owned is not null && context.GetEndpoint() is RouteEndpoint { RoutePattern.RawText: { } pattern })
            {
                owned.Rename($"{context.Request.Method} {(pattern.StartsWith('/') ? pattern : "/" + pattern)}");
            }
        }
    }
}
