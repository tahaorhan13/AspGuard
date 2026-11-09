using System.Globalization;

namespace AspGuard;

/// <summary>
/// Security decisions shared by the ASP.NET Core middleware and the ASP.NET 4.x module, written against plain values
/// so both hosts apply exactly the same rules.
/// </summary>
internal static class SecurityRules
{
    /// <summary>Security response headers to add (name, value), in order. Empty values are left out.</summary>
    public static KeyValuePair<string, string>[] Headers(SecurityHeadersOptions headers)
    {
        if (!headers.Enabled)
        {
            return [];
        }

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
                list.Add(new KeyValuePair<string, string>(name, value!));
            }
        }
    }

    /// <summary>The <c>Strict-Transport-Security</c> value.</summary>
    public static string Hsts(StrictTransportSecurityOptions hsts)
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

    /// <summary>Same exclusions as ASP.NET Core's HSTS middleware: browsers would pin HTTPS for local development hosts.</summary>
    public static bool IsLocalhost(string host) =>
        string.Equals(host, "localhost", StringComparison.OrdinalIgnoreCase) || host is "127.0.0.1" or "[::1]" or "::1";

    /// <summary>Normalizes configured trusted origins for <see cref="IsCrossSiteRequestAllowed"/>.</summary>
    public static HashSet<string> TrustedOrigins(IEnumerable<string> origins) =>
        new(origins.Select(o => o.Trim().TrimEnd('/')), StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// CSRF rule: a state-changing request that carries cookies must be same-origin (<c>Sec-Fetch-Site</c>, or for older
    /// browsers an <c>Origin</c> matching <paramref name="host"/>) or come from a trusted origin. Requests without cookies
    /// cannot ride on a victim's session and are always allowed.
    /// </summary>
    public static bool IsCrossSiteRequestAllowed(string method, bool hasCookie, string? origin, string? fetchSite, string host, HashSet<string> trusted)
    {
        if (IsSafeMethod(method) || !hasCookie)
        {
            return true;
        }

        origin ??= string.Empty;
        if (origin.Length > 0 && trusted.Contains(origin.TrimEnd('/')))
        {
            return true;
        }

        // Every current browser sends Sec-Fetch-Site; page script cannot set it.
        if (!string.IsNullOrEmpty(fetchSite))
        {
            return string.Equals(fetchSite, "same-origin", StringComparison.OrdinalIgnoreCase)
                || string.Equals(fetchSite, "none", StringComparison.OrdinalIgnoreCase);
        }

        // No Origin at all means a non-browser client, which a cross-site attacker cannot produce from a browser.
        if (origin.Length == 0)
        {
            return true;
        }

        return Uri.TryCreate(origin, UriKind.Absolute, out var uri)
            && string.Equals(uri.Authority, host, StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsSafeMethod(string method) =>
        string.Equals(method, "GET", StringComparison.OrdinalIgnoreCase)
        || string.Equals(method, "HEAD", StringComparison.OrdinalIgnoreCase)
        || string.Equals(method, "OPTIONS", StringComparison.OrdinalIgnoreCase)
        || string.Equals(method, "TRACE", StringComparison.OrdinalIgnoreCase);
}
