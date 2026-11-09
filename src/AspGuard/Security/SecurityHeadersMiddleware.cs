using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

namespace AspGuard.Security;

/// <summary>
/// Adds security response headers and HSTS just before the response starts. Headers the application set itself are kept.
/// </summary>
internal sealed class SecurityHeadersMiddleware
{
    private readonly RequestDelegate _next;
    private readonly KeyValuePair<string, string>[] _headers;
    private readonly string? _hsts;

    public SecurityHeadersMiddleware(RequestDelegate next, IOptions<AspGuardOptions> options, IHostEnvironment environment)
    {
        _next = next;
        var security = options.Value.Security;
        _headers = SecurityRules.Headers(security.Headers);
        _hsts = security.Hsts.Enabled && !environment.IsDevelopment() ? SecurityRules.Hsts(security.Hsts) : null;
    }

    public Task InvokeAsync(HttpContext context)
    {
        context.Response.OnStarting(
            static state =>
            {
                var (middleware, httpContext) = ((SecurityHeadersMiddleware, HttpContext))state;
                middleware.Apply(httpContext);
                return Task.CompletedTask;
            },
            (this, context));
        return _next(context);
    }

    private void Apply(HttpContext context)
    {
        var headers = context.Response.Headers;
        foreach (var (name, value) in _headers)
        {
            if (!headers.ContainsKey(name))
            {
                headers[name] = value;
            }
        }

        if (_hsts is not null && context.Request.IsHttps && !SecurityRules.IsLocalhost(context.Request.Host.Host) && !headers.ContainsKey("Strict-Transport-Security"))
        {
            headers["Strict-Transport-Security"] = _hsts;
        }
    }
}
