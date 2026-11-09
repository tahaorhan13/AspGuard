using System.Globalization;
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
        _headers = security.Headers.Enabled ? BuildHeaders(security.Headers) : [];
        _hsts = security.Hsts.Enabled && !environment.IsDevelopment() ? BuildHsts(security.Hsts) : null;
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

        if (_hsts is not null && context.Request.IsHttps && !IsLocalhost(context.Request.Host.Host) && !headers.ContainsKey("Strict-Transport-Security"))
        {
            headers["Strict-Transport-Security"] = _hsts;
        }
    }

    // Same exclusions as ASP.NET Core's HSTS middleware: browsers would pin HTTPS for local development hosts.
    private static bool IsLocalhost(string host) =>
        string.Equals(host, "localhost", StringComparison.OrdinalIgnoreCase) || host is "127.0.0.1" or "[::1]" or "::1";

    internal static KeyValuePair<string, string>[] BuildHeaders(SecurityHeadersOptions headers)
    {
        var list = new List<KeyValuePair<string, string>>();
        Add(list, "X-Content-Type-Options", headers.ContentTypeOptions);
        Add(list, "X-Frame-Options", headers.FrameOptions);
        Add(list, "Referrer-Policy", headers.ReferrerPolicy);
        Add(list, "Permissions-Policy", headers.PermissionsPolicy);
        Add(list, "Cross-Origin-Opener-Policy", headers.CrossOriginOpenerPolicy);
        Add(list, "X-Permitted-Cross-Domain-Policies", headers.PermittedCrossDomainPolicies);
        Add(list, "X-XSS-Protection", headers.XssProtection);
        Add(list, "Content-Security-Policy", headers.ContentSecurityPolicy);
        return [.. list];

        static void Add(List<KeyValuePair<string, string>> list, string name, string? value)
        {
            if (!string.IsNullOrWhiteSpace(value))
            {
                list.Add(new(name, value));
            }
        }
    }

    internal static string BuildHsts(StrictTransportSecurityOptions hsts)
    {
        var value = "max-age=" + ((long)hsts.MaxAge.TotalSeconds).ToString(CultureInfo.InvariantCulture);
        if (hsts.IncludeSubDomains)
        {
            value += "; includeSubDomains";
        }

        if (hsts.Preload)
        {
            value += "; preload";
        }

        return value;
    }
}
