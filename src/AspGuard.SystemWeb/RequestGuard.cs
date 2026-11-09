using System.Globalization;
using System.Web;
using System.Web.Routing;
using Microsoft.Extensions.Logging;

namespace AspGuard.SystemWeb;

/// <summary>
/// The ASP.NET 4.x request logic, written against <see cref="HttpContextBase"/> so it can be tested without IIS.
/// Same rules as the ASP.NET Core middleware (<see cref="SecurityRules"/>).
/// </summary>
internal sealed partial class RequestGuard
{
    internal const string ScopeKey = "AspGuard.Scope";
    internal const string CountHeader = "X-AspGuard-Query-Count";
    internal const string FindingsHeader = "X-AspGuard-Query-Findings";

    // Header names ASP.NET and IIS add that reveal the server technology.
    private static readonly string[] _technologyHeaders = ["Server", "X-AspNet-Version", "X-AspNetMvc-Version", "X-Powered-By"];

    private static readonly object _gate = new();
    private static RequestGuard? _current;

    private readonly AspGuardRuntime _runtime;
    private readonly bool _debugging;
    private readonly KeyValuePair<string, string>[] _headers;
    private readonly string? _hsts;
    private readonly HashSet<string> _trusted;
    private readonly FixedWindowRateLimiter? _limiter;
    private readonly ILogger _logger;

    internal RequestGuard(AspGuardRuntime runtime, bool debugging, ILogger logger, Func<DateTime>? clock = null)
    {
        _runtime = runtime;
        _debugging = debugging;
        _logger = logger;
        var security = runtime.Options.Security;
        _headers = SecurityRules.Headers(security.Headers);
        _hsts = security.Hsts.Enabled && !debugging ? SecurityRules.Hsts(security.Hsts) : null;
        _trusted = SecurityRules.TrustedOrigins(security.CrossSiteProtection.TrustedOrigins);
        _limiter = security.RateLimit.Enabled ? new FixedWindowRateLimiter(security.RateLimit.PermitLimit, security.RateLimit.Window, clock) : null;
    }

    /// <summary>The guard for the current configuration; rebuilt when <see cref="AspGuardFramework.Configure"/> changes it.</summary>
    internal static RequestGuard Current
    {
        get
        {
            var runtime = AspGuardFramework.Runtime;
            var current = Volatile.Read(ref _current);
            if (current is not null && ReferenceEquals(current._runtime, runtime))
            {
                return current;
            }

            lock (_gate)
            {
                if (_current is null || !ReferenceEquals(_current._runtime, runtime))
                {
                    _current = new RequestGuard(runtime, AspGuardSystemWeb.IsDebugging, AspGuardFramework.CreateLogger("AspGuard.SystemWeb"));
                }

                return _current;
            }
        }
    }

    private AspGuardOptions Options => _runtime.Options;

    /// <summary>Security checks and scope creation. Returns <c>true</c> when the request was answered and must end here.</summary>
    public bool OnBeginRequest(HttpContextBase context)
    {
        var request = context.Request;
        var security = Options.Security;
        if (security.Enabled)
        {
            if (security.CrossSiteProtection.Enabled
                && !SecurityRules.IsCrossSiteRequestAllowed(request.HttpMethod, request.Headers["Cookie"] is { Length: > 0 }, request.Headers["Origin"], request.Headers["Sec-Fetch-Site"], Host(request), _trusted))
            {
                LogBlocked(_logger, request.HttpMethod, request.Path, request.Headers["Origin"] ?? string.Empty, request.Headers["Sec-Fetch-Site"] ?? string.Empty);
                End(context, 403, "Cross-site request blocked.");
                return true;
            }

            if (_limiter is not null && !IsExcluded(request.Path, security.RateLimit.ExcludedPaths))
            {
                var client = request.UserHostAddress ?? "unknown";
                if (!_limiter.TryAcquire(client, out var retryAfter))
                {
                    LogRejected(_logger, client, request.Path);
                    context.Response.AppendHeader("Retry-After", Math.Max(1, (int)Math.Ceiling(retryAfter.TotalSeconds)).ToString(CultureInfo.InvariantCulture));
                    End(context, 429, "Too many requests.");
                    return true;
                }
            }

            if (security.HttpsRedirection && !_debugging && !IsHttps(request)
                && (request.HttpMethod == "GET" || request.HttpMethod == "HEAD") && request.Url is { } url)
            {
                context.Response.StatusCode = 307;
                context.Response.AppendHeader("Location", "https://" + url.Host + request.RawUrl);
                return true;
            }
        }

        if (Options.EnableQueryDetection)
        {
            context.Items[ScopeKey] = AspGuardScope.CreateDetached($"{request.HttpMethod} {request.Path}", _runtime);
        }

        return false;
    }

    /// <summary>Adds headers just before they are sent. Headers the application set itself are kept.</summary>
    public void OnPreSendRequestHeaders(HttpContextBase context, bool integratedPipeline)
    {
        var response = context.Response;
        var security = Options.Security;

        // Classic pipeline: response.Headers is not available, so headers can only be appended.
        var existing = integratedPipeline ? response.Headers : null;
        void Add(string name, string value)
        {
            if (existing is null)
            {
                response.AppendHeader(name, value);
            }
            else if (existing[name] is null)
            {
                existing[name] = value;
            }
        }

        if (security.Enabled)
        {
            foreach (var header in _headers)
            {
                Add(header.Key, header.Value);
            }

            if (_hsts is not null && IsHttps(context.Request) && !SecurityRules.IsLocalhost(context.Request.Url?.Host ?? string.Empty))
            {
                Add("Strict-Transport-Security", _hsts);
            }

            if (security.Headers.RemoveServerHeader && existing is not null)
            {
                foreach (var name in _technologyHeaders)
                {
                    existing.Remove(name);
                }
            }
        }

        if (Options.AddResponseHeaders && context.Items[ScopeKey] is AspGuardScope scope)
        {
            Add(CountHeader, scope.QueryCount.ToString(CultureInfo.InvariantCulture));
            Add(FindingsHeader, scope.GetFindings().Count.ToString(CultureInfo.InvariantCulture));
        }
    }

    /// <summary>Names the scope after the matched route pattern (never the query string) and reports its findings.</summary>
    public void OnEndRequest(HttpContextBase context)
    {
        if (context.Items[ScopeKey] is not AspGuardScope scope)
        {
            return;
        }

        context.Items.Remove(ScopeKey);
        if (context.Request.RequestContext?.RouteData?.Route is Route { Url: { } pattern })
        {
            scope.Rename($"{context.Request.HttpMethod} /{pattern}");
        }

        scope.Dispose();
    }

    private static void End(HttpContextBase context, int status, string message)
    {
        context.Response.StatusCode = status;
        context.Response.ContentType = "text/plain";
        context.Response.Write(message);
    }

    private static string Host(HttpRequestBase request) => request.Headers["Host"] ?? request.Url?.Authority ?? string.Empty;

    // A TLS-terminating load balancer forwards plain HTTP and says so in X-Forwarded-Proto.
    private static bool IsHttps(HttpRequestBase request) =>
        request.IsSecureConnection || string.Equals(request.Headers["X-Forwarded-Proto"], "https", StringComparison.OrdinalIgnoreCase);

    private static bool IsExcluded(string path, IList<string> excluded)
    {
        foreach (var prefix in excluded)
        {
            var normalized = "/" + prefix.Trim().Trim('/');
            if (normalized.Length > 1
                && path.StartsWith(normalized, StringComparison.OrdinalIgnoreCase)
                && (path.Length == normalized.Length || path[normalized.Length] == '/'))
            {
                return true;
            }
        }

        return false;
    }

    [LoggerMessage(EventId = 100, Level = LogLevel.Warning, Message = "AspGuard blocked a cross-site {Method} request to {Path} (Origin: '{Origin}', Sec-Fetch-Site: '{FetchSite}').")]
    private static partial void LogBlocked(ILogger logger, string method, string path, string origin, string fetchSite);

    [LoggerMessage(EventId = 101, Level = LogLevel.Warning, Message = "AspGuard rate limit exceeded for client {Client} on {Path}.")]
    private static partial void LogRejected(ILogger logger, string client, string path);
}
