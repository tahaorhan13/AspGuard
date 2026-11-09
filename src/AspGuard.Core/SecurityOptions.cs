namespace AspGuard;

/// <summary>
/// Baseline HTTP security applied by <c>services.AddAspGuard()</c> (AspGuard package) in front of the application pipeline.
/// The defaults are chosen not to break typical APIs and web apps; every measure can be tuned or turned off.
/// </summary>
public sealed class SecurityOptions
{
    /// <summary>Master switch for all security middleware. Default <c>true</c>.</summary>
    public bool Enabled { get; set; } = true;

    /// <summary>Security response headers (nosniff, frame options, referrer policy, ...).</summary>
    public SecurityHeadersOptions Headers { get; } = new();

    /// <summary>HTTP Strict Transport Security. Never sent in Development, for localhost, or over plain HTTP.</summary>
    public StrictTransportSecurityOptions Hsts { get; } = new();

    /// <summary>Redirect HTTP requests to HTTPS (ASP.NET Core's HTTPS redirection middleware). Skipped in Development. Default <c>true</c>.</summary>
    public bool HttpsRedirection { get; set; } = true;

    /// <summary>Cross-site request forgery (CSRF) protection based on the <c>Sec-Fetch-Site</c> and <c>Origin</c> headers.</summary>
    public CrossSiteRequestOptions CrossSiteProtection { get; } = new();

    /// <summary>Per-client-IP request rate limiting.</summary>
    public RequestRateLimitOptions RateLimit { get; } = new();
}

/// <summary>
/// Security response headers. A header is only added when the application did not set it itself;
/// set a value to <c>null</c> to not send that header.
/// </summary>
public sealed class SecurityHeadersOptions
{
    /// <summary>Add the headers below. Default <c>true</c>.</summary>
    public bool Enabled { get; set; } = true;

    /// <summary><c>X-Content-Type-Options</c>. Default <c>nosniff</c>.</summary>
    public string? ContentTypeOptions { get; set; } = "nosniff";

    /// <summary><c>X-Frame-Options</c> (clickjacking). Default <c>DENY</c>.</summary>
    public string? FrameOptions { get; set; } = "DENY";

    /// <summary><c>Referrer-Policy</c>. Default <c>strict-origin-when-cross-origin</c>.</summary>
    public string? ReferrerPolicy { get; set; } = "strict-origin-when-cross-origin";

    /// <summary><c>Permissions-Policy</c>. Default denies camera, microphone, geolocation, payment and similar device APIs.</summary>
    public string? PermissionsPolicy { get; set; } = "accelerometer=(), camera=(), geolocation=(), gyroscope=(), magnetometer=(), microphone=(), payment=(), usb=()";

    /// <summary><c>Cross-Origin-Opener-Policy</c>. Default <c>same-origin</c>.</summary>
    public string? CrossOriginOpenerPolicy { get; set; } = "same-origin";

    /// <summary><c>X-Permitted-Cross-Domain-Policies</c>. Default <c>none</c>.</summary>
    public string? PermittedCrossDomainPolicies { get; set; } = "none";

    /// <summary><c>X-XSS-Protection</c>. Default <c>0</c>, which turns off the legacy (and exploitable) browser XSS auditor, as OWASP recommends.</summary>
    public string? XssProtection { get; set; } = "0";

    /// <summary>
    /// <c>Content-Security-Policy</c>. Default <c>null</c> (not sent), because a policy that fits one front end breaks another.
    /// For a pure JSON API, <c>default-src 'none'; frame-ancestors 'none'</c> is a good choice.
    /// </summary>
    public string? ContentSecurityPolicy { get; set; }

    /// <summary>Stop Kestrel from sending the <c>Server</c> header (hides the server technology). Default <c>true</c>.</summary>
    public bool RemoveServerHeader { get; set; } = true;
}

/// <summary>HTTP Strict Transport Security (<c>Strict-Transport-Security</c> header).</summary>
public sealed class StrictTransportSecurityOptions
{
    /// <summary>Send the header on HTTPS responses outside Development. Default <c>true</c>.</summary>
    public bool Enabled { get; set; } = true;

    /// <summary>How long browsers must only use HTTPS. Default 365 days.</summary>
    public TimeSpan MaxAge { get; set; } = TimeSpan.FromDays(365);

    /// <summary>Apply to all subdomains too. Default <c>false</c>, because a subdomain still served over HTTP would break.</summary>
    public bool IncludeSubDomains { get; set; }

    /// <summary>Add the <c>preload</c> directive. Default <c>false</c>; only enable it if you submit the domain to the preload list.</summary>
    public bool Preload { get; set; }
}

/// <summary>
/// Blocks cross-site state-changing requests (POST, PUT, PATCH, DELETE) that carry cookies. Browsers send
/// <c>Sec-Fetch-Site</c> (or at least <c>Origin</c>), so a forged request from another site is rejected with 403.
/// Requests without cookies (bearer tokens, API keys, server-to-server calls) and safe methods are never blocked.
/// </summary>
public sealed class CrossSiteRequestOptions
{
    /// <summary>Enable the protection. Default <c>true</c>.</summary>
    public bool Enabled { get; set; } = true;

    /// <summary>
    /// Origins allowed to send cookie-carrying state-changing requests from another site, for example
    /// <c>https://app.example.com</c> (scheme, host and optional port; no path).
    /// </summary>
    public IList<string> TrustedOrigins { get; } = new List<string>();
}

/// <summary>
/// Fixed-window rate limiting per client IP address. Rejected requests get <c>429 Too Many Requests</c> with a
/// <c>Retry-After</c> header. Behind a reverse proxy, configure ASP.NET Core's forwarded headers middleware so the real
/// client IP is used; otherwise every client shares the proxy's limit.
/// </summary>
public sealed class RequestRateLimitOptions
{
    /// <summary>Enable rate limiting. Default <c>true</c>.</summary>
    public bool Enabled { get; set; } = true;

    /// <summary>Requests allowed per client IP in each <see cref="Window"/>. Default 300.</summary>
    public int PermitLimit { get; set; } = 300;

    /// <summary>Length of the window. Default one minute.</summary>
    public TimeSpan Window { get; set; } = TimeSpan.FromMinutes(1);

    /// <summary>Path prefixes that are never limited, for example <c>/health</c>.</summary>
    public IList<string> ExcludedPaths { get; } = new List<string>();
}
