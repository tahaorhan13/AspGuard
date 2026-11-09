using System.Collections.Specialized;
using System.Web.Routing;
using AspGuard.SystemWeb;
using Microsoft.Extensions.Logging.Abstractions;

namespace AspGuard.Framework.Tests;

public sealed class SystemWebTests : IDisposable
{
    public SystemWebTests() => AspGuardFramework.Reset();

    public void Dispose()
    {
        AspGuardScope.AmbientFallback = null;
        AspGuardFramework.Reset();
    }

    private static RequestGuard Guard(Action<AspGuardOptions>? configure = null, bool debugging = false, Func<DateTime>? clock = null)
    {
        AspGuardFramework.Reset();
        AspGuardSystemWeb.ApplyDefaults(debugging);
        if (configure is not null)
        {
            AspGuardFramework.Configure(configure);
        }

        return new RequestGuard(AspGuardFramework.Runtime, debugging, NullLogger.Instance, clock);
    }

    private static NameValueCollection Headers(params string[] pairs)
    {
        var headers = new NameValueCollection();
        for (var i = 0; i < pairs.Length; i += 2)
        {
            headers[pairs[i]] = pairs[i + 1];
        }

        return headers;
    }

    private static FakeHttpContext Send(RequestGuard guard, FakeHttpContext context, bool integrated = true)
    {
        if (!guard.OnBeginRequest(context))
        {
            guard.OnPreSendRequestHeaders(context, integrated);
        }

        guard.OnEndRequest(context);
        return context;
    }

    [Fact]
    public void Defaults_follow_the_debug_flag_and_keep_https_redirection_opt_in()
    {
        Guard(debugging: false);
        Assert.False(AspGuardFramework.Options.EnableQueryDetection);
        Assert.False(AspGuardFramework.Options.Security.HttpsRedirection);

        Guard(debugging: true);
        Assert.True(AspGuardFramework.Options.EnableQueryDetection);

        Guard(o => o.EnableQueryDetection = true, debugging: false);
        Assert.True(AspGuardFramework.Options.EnableQueryDetection);
    }

    [Fact]
    public void Invalid_configuration_is_rejected()
    {
        AspGuardFramework.Reset();
        Assert.Throws<ArgumentException>(() => AspGuardFramework.Configure(o => o.NPlusOneThreshold = 1));
    }

    [Fact]
    public void Security_headers_are_added_and_technology_headers_removed()
    {
        var context = new FakeHttpContext();
        context.FakeResponse.Headers["Server"] = "Microsoft-IIS/10.0";
        context.FakeResponse.Headers["X-AspNet-Version"] = "4.0.30319";

        Send(Guard(), context);

        var headers = context.FakeResponse.Headers;
        Assert.Equal("nosniff", headers["X-Content-Type-Options"]);
        Assert.Equal("DENY", headers["X-Frame-Options"]);
        Assert.Equal("strict-origin-when-cross-origin", headers["Referrer-Policy"]);
        Assert.Equal("0", headers["X-XSS-Protection"]);
        Assert.Null(headers["Content-Security-Policy"]);
        Assert.Null(headers["Server"]);
        Assert.Null(headers["X-AspNet-Version"]);
    }

    [Fact]
    public void Header_set_by_the_application_is_kept()
    {
        // MVC's AntiForgery sets X-Frame-Options: SAMEORIGIN; it must not become "DENY, SAMEORIGIN".
        var context = new FakeHttpContext();
        context.FakeResponse.Headers["X-Frame-Options"] = "SAMEORIGIN";

        Send(Guard(), context);

        Assert.Equal("SAMEORIGIN", context.FakeResponse.Headers["X-Frame-Options"]);
    }

    [Fact]
    public void Classic_pipeline_appends_headers()
    {
        var context = Send(Guard(), new FakeHttpContext(), integrated: false);

        Assert.Equal("nosniff", context.FakeResponse.Appended["X-Content-Type-Options"]);
    }

    [Theory]
    [InlineData("https://example.com/", false, null, true)]
    [InlineData("http://example.com/", false, "https", true)]
    [InlineData("https://example.com/", true, null, false)]
    [InlineData("https://localhost/", false, null, false)]
    [InlineData("http://example.com/", false, null, false)]
    public void Hsts_only_over_https_outside_debug_and_not_for_localhost(string url, bool debugging, string? forwardedProto, bool expected)
    {
        var headers = forwardedProto is null ? new NameValueCollection() : Headers("X-Forwarded-Proto", forwardedProto);
        var context = Send(Guard(debugging: debugging), new FakeHttpContext(url: url, headers: headers));

        Assert.Equal(expected ? "max-age=31536000" : null, context.FakeResponse.Headers["Strict-Transport-Security"]);
    }

    [Theory]
    [InlineData("cross-site", null)]
    [InlineData(null, "https://evil.example")]
    public void Cross_site_post_with_cookies_is_blocked(string? fetchSite, string? origin)
    {
        var headers = Headers("Cookie", "ASP.NET_SessionId=abc", "Host", "example.com");
        if (fetchSite is not null)
        {
            headers["Sec-Fetch-Site"] = fetchSite;
        }

        if (origin is not null)
        {
            headers["Origin"] = origin;
        }

        var context = new FakeHttpContext("POST", headers: headers);

        Assert.True(Guard().OnBeginRequest(context));
        Assert.Equal(403, context.FakeResponse.StatusCode);
    }

    [Theory]
    [InlineData("Cookie", "s=1", "Sec-Fetch-Site", "same-origin")]
    [InlineData("Cookie", "s=1", "Origin", "http://example.com")]
    [InlineData("Sec-Fetch-Site", "cross-site", "Origin", "https://evil.example")]
    public void Same_origin_or_cookieless_post_is_allowed(string name1, string value1, string name2, string value2)
    {
        var context = new FakeHttpContext("POST", headers: Headers(name1, value1, name2, value2, "Host", "example.com"));

        Assert.False(Guard().OnBeginRequest(context));
        Assert.Equal(200, context.FakeResponse.StatusCode);
    }

    [Fact]
    public void Trusted_origin_and_disabled_protection_are_allowed()
    {
        var headers = Headers("Cookie", "s=1", "Sec-Fetch-Site", "cross-site", "Origin", "https://app.example.com", "Host", "example.com");

        Assert.False(Guard(o => o.Security.CrossSiteProtection.TrustedOrigins.Add("https://app.example.com")).OnBeginRequest(new FakeHttpContext("POST", headers: headers)));
        Assert.False(Guard(o => o.Security.CrossSiteProtection.Enabled = false).OnBeginRequest(new FakeHttpContext("POST", headers: headers)));
        Assert.False(Guard(o => o.Security.Enabled = false).OnBeginRequest(new FakeHttpContext("POST", headers: headers)));
    }

    [Fact]
    public void Rate_limit_answers_429_with_retry_after_and_resets_with_the_window()
    {
        var now = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        var guard = Guard(o => o.Security.RateLimit.PermitLimit = 2, clock: () => now);

        Assert.False(guard.OnBeginRequest(new FakeHttpContext()));
        Assert.False(guard.OnBeginRequest(new FakeHttpContext()));
        var rejected = new FakeHttpContext();
        Assert.True(guard.OnBeginRequest(rejected));
        Assert.Equal(429, rejected.FakeResponse.StatusCode);
        Assert.Equal("60", rejected.FakeResponse.Appended["Retry-After"]);

        Assert.False(guard.OnBeginRequest(new FakeHttpContext(clientIp: "10.0.0.2")));

        now = now.AddMinutes(1);
        Assert.False(guard.OnBeginRequest(new FakeHttpContext()));
    }

    [Fact]
    public void Excluded_paths_are_not_rate_limited()
    {
        var guard = Guard(o =>
        {
            o.Security.RateLimit.PermitLimit = 1;
            o.Security.RateLimit.ExcludedPaths.Add("/health");
        });

        for (var i = 0; i < 5; i++)
        {
            Assert.False(guard.OnBeginRequest(new FakeHttpContext(url: "http://example.com/health/ready")));
        }

        Assert.False(guard.OnBeginRequest(new FakeHttpContext(url: "http://example.com/healthy")));
        Assert.True(guard.OnBeginRequest(new FakeHttpContext(url: "http://example.com/healthy")));
    }

    [Fact]
    public void Https_redirection_is_opt_in_and_only_for_get_outside_debug()
    {
        Assert.False(Guard().OnBeginRequest(new FakeHttpContext(url: "http://example.com/orders?page=2")));

        var redirected = new FakeHttpContext(url: "http://example.com/orders?page=2");
        Assert.True(Guard(o => o.Security.HttpsRedirection = true).OnBeginRequest(redirected));
        Assert.Equal(307, redirected.FakeResponse.StatusCode);
        Assert.Equal("https://example.com/orders?page=2", redirected.FakeResponse.Appended["Location"]);

        Assert.False(Guard(o => o.Security.HttpsRedirection = true, debugging: true).OnBeginRequest(new FakeHttpContext(url: "http://example.com/")));
        Assert.False(Guard(o => o.Security.HttpsRedirection = true).OnBeginRequest(new FakeHttpContext("POST", "http://example.com/")));
    }

    [Fact]
    public void Request_scope_is_named_after_the_route_pattern_and_reported()
    {
        using var db = new Ef6TestDb();
        var detected = new List<AspGuardFinding>();
        var guard = Guard(o =>
        {
            o.EnableQueryDetection = true;
            o.AddResponseHeaders = true;
            o.OnDetected = detected.Add;
        });
        var context = new FakeHttpContext(url: "http://example.com/api/blogs/7");
        context.FakeRequest.Route = new Route("api/blogs/{id}", new StopRoutingHandler());
        AspGuardScope.AmbientFallback = () => context.Items[RequestGuard.ScopeKey] as AspGuardScope;

        Assert.False(guard.OnBeginRequest(context));
        using (var shop = db.NewContext())
        {
            foreach (var blog in shop.Blogs.ToList())
            {
                _ = shop.Posts.Where(p => p.BlogId == blog.Id).ToList();
            }
        }

        guard.OnPreSendRequestHeaders(context, integratedPipeline: true);
        guard.OnEndRequest(context);

        Assert.Equal("11", context.FakeResponse.Headers[RequestGuard.CountHeader]);
        Assert.Equal("1", context.FakeResponse.Headers[RequestGuard.FindingsHeader]);
        var finding = Assert.Single(detected);
        Assert.Equal("GET /api/blogs/{id}", finding.ScopeName);
        Assert.Contains(nameof(Request_scope_is_named_after_the_route_pattern_and_reported), finding.CallSite);
        Assert.Null(context.Items[RequestGuard.ScopeKey]);
    }

    [Fact]
    public void No_scope_when_query_detection_is_off()
    {
        var context = new FakeHttpContext();

        Guard(debugging: false).OnBeginRequest(context);

        Assert.Null(context.Items[RequestGuard.ScopeKey]);
    }
}
