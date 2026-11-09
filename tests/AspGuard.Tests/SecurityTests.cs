using System.Net;
using Microsoft.AspNetCore.HttpsPolicy;
using Microsoft.AspNetCore.Server.Kestrel.Core;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace AspGuard.Tests;

public class SecurityTests
{
    private static string? Header(HttpResponseMessage response, string name) =>
        response.Headers.TryGetValues(name, out var values) ? values.Single() : null;

    private static HttpRequestMessage Post(string path, string? cookie = "session=abc", string? origin = null, string? fetchSite = null)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, path);
        if (cookie is not null)
        {
            request.Headers.Add("Cookie", cookie);
        }

        if (origin is not null)
        {
            request.Headers.Add("Origin", origin);
        }

        if (fetchSite is not null)
        {
            request.Headers.Add("Sec-Fetch-Site", fetchSite);
        }

        return request;
    }

    [Fact]
    public async Task Default_security_headers_are_added()
    {
        await using var app = await TestApp.StartAsync();
        var response = await app.Client().GetAsync("/");

        Assert.Equal("nosniff", Header(response, "X-Content-Type-Options"));
        Assert.Equal("DENY", Header(response, "X-Frame-Options"));
        Assert.Equal("strict-origin-when-cross-origin", Header(response, "Referrer-Policy"));
        Assert.Contains("camera=()", Header(response, "Permissions-Policy"));
        Assert.Equal("same-origin", Header(response, "Cross-Origin-Opener-Policy"));
        Assert.Equal("none", Header(response, "X-Permitted-Cross-Domain-Policies"));
        Assert.Equal("0", Header(response, "X-XSS-Protection"));
        Assert.Null(Header(response, "Content-Security-Policy"));
    }

    [Fact]
    public async Task Header_set_by_the_application_is_kept()
    {
        await using var app = await TestApp.StartAsync();
        var response = await app.Client().GetAsync("/custom-frame");

        Assert.Equal("SAMEORIGIN", Header(response, "X-Frame-Options"));
    }

    [Fact]
    public async Task Headers_are_configurable()
    {
        await using var app = await TestApp.StartAsync(o =>
        {
            o.Security.Headers.ContentSecurityPolicy = "default-src 'none'";
            o.Security.Headers.FrameOptions = null;
        });
        var response = await app.Client().GetAsync("/");

        Assert.Equal("default-src 'none'", response.Content.Headers.TryGetValues("Content-Security-Policy", out var csp) ? csp.Single() : Header(response, "Content-Security-Policy"));
        Assert.Null(Header(response, "X-Frame-Options"));
    }

    [Fact]
    public async Task Security_master_switch_turns_everything_off()
    {
        await using var app = await TestApp.StartAsync(o => o.Security.Enabled = false);
        var client = app.Client();

        var response = await client.GetAsync("/");
        var crossSite = await client.SendAsync(Post("/submit", fetchSite: "cross-site"));

        Assert.Null(Header(response, "X-Content-Type-Options"));
        Assert.Equal(HttpStatusCode.OK, crossSite.StatusCode);
    }

    [Fact]
    public async Task Hsts_is_sent_over_https_outside_development()
    {
        await using var app = await TestApp.StartAsync(environment: "Production");
        var response = await app.Client("https://example.com").GetAsync("/");

        Assert.Equal("max-age=31536000", Header(response, "Strict-Transport-Security"));
    }

    [Fact]
    public async Task Hsts_directives_are_configurable()
    {
        await using var app = await TestApp.StartAsync(
            o =>
            {
                o.Security.Hsts.MaxAge = TimeSpan.FromDays(1);
                o.Security.Hsts.IncludeSubDomains = true;
                o.Security.Hsts.Preload = true;
            },
            environment: "Production");
        var response = await app.Client("https://example.com").GetAsync("/");

        Assert.Equal("max-age=86400; includeSubDomains; preload", Header(response, "Strict-Transport-Security"));
    }

    [Theory]
    [InlineData("Development", "https://example.com")]
    [InlineData("Production", "https://localhost")]
    [InlineData("Production", "http://example.com")]
    public async Task Hsts_is_not_sent_in_development_for_localhost_or_over_http(string environment, string baseAddress)
    {
        await using var app = await TestApp.StartAsync(environment: environment);
        var response = await app.Client(baseAddress).GetAsync("/");

        Assert.Null(Header(response, "Strict-Transport-Security"));
    }

    [Fact]
    public async Task Http_is_redirected_to_https_outside_development()
    {
        await using var app = await TestApp.StartAsync(
            environment: "Production",
            services: s => s.Configure<HttpsRedirectionOptions>(o => o.HttpsPort = 443));
        var response = await app.Client().GetAsync("/");

        Assert.Equal(HttpStatusCode.TemporaryRedirect, response.StatusCode);
        Assert.Equal("https://example.com/", response.Headers.Location?.ToString());
    }

    [Fact]
    public async Task Https_redirection_is_skipped_in_development_and_can_be_turned_off()
    {
        await using var development = await TestApp.StartAsync(services: s => s.Configure<HttpsRedirectionOptions>(o => o.HttpsPort = 443));
        await using var off = await TestApp.StartAsync(
            o => o.Security.HttpsRedirection = false,
            environment: "Production",
            services: s => s.Configure<HttpsRedirectionOptions>(o => o.HttpsPort = 443));

        Assert.Equal(HttpStatusCode.OK, (await development.Client().GetAsync("/")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await off.Client().GetAsync("/")).StatusCode);
    }

    [Theory]
    [InlineData("cross-site", null)]
    [InlineData("same-site", null)]
    [InlineData(null, "https://evil.example")]
    [InlineData(null, "null")]
    public async Task Cross_site_post_with_cookies_is_blocked(string? fetchSite, string? origin)
    {
        await using var app = await TestApp.StartAsync();
        var response = await app.Client().SendAsync(Post("/submit", origin: origin, fetchSite: fetchSite));

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Theory]
    [InlineData("same-origin", null, "session=abc")]
    [InlineData("none", null, "session=abc")]
    [InlineData(null, "http://example.com", "session=abc")]
    [InlineData(null, null, "session=abc")]
    [InlineData("cross-site", "https://evil.example", null)]
    public async Task Same_origin_or_cookieless_post_is_allowed(string? fetchSite, string? origin, string? cookie)
    {
        await using var app = await TestApp.StartAsync();
        var response = await app.Client().SendAsync(Post("/submit", cookie, origin, fetchSite));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task Safe_methods_are_never_blocked()
    {
        await using var app = await TestApp.StartAsync();
        var request = new HttpRequestMessage(HttpMethod.Get, "/");
        request.Headers.Add("Cookie", "session=abc");
        request.Headers.Add("Sec-Fetch-Site", "cross-site");

        var response = await app.Client().SendAsync(request);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task Trusted_origin_is_allowed()
    {
        await using var app = await TestApp.StartAsync(o => o.Security.CrossSiteProtection.TrustedOrigins.Add("https://app.example.com/"));
        var response = await app.Client().SendAsync(Post("/submit", origin: "https://app.example.com", fetchSite: "cross-site"));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task Cross_site_protection_can_be_turned_off()
    {
        await using var app = await TestApp.StartAsync(o => o.Security.CrossSiteProtection.Enabled = false);
        var response = await app.Client().SendAsync(Post("/submit", fetchSite: "cross-site"));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task Rate_limit_rejects_with_429_and_retry_after()
    {
        await using var app = await TestApp.StartAsync(o => o.Security.RateLimit.PermitLimit = 3);
        var client = app.Client();

        for (var i = 0; i < 3; i++)
        {
            Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/")).StatusCode);
        }

        var rejected = await client.GetAsync("/");

        Assert.Equal(HttpStatusCode.TooManyRequests, rejected.StatusCode);
        Assert.True(int.Parse(Header(rejected, "Retry-After")!, System.Globalization.CultureInfo.InvariantCulture) >= 1);
    }

    [Fact]
    public async Task Excluded_paths_are_not_rate_limited()
    {
        await using var app = await TestApp.StartAsync(o =>
        {
            o.Security.RateLimit.PermitLimit = 1;
            o.Security.RateLimit.ExcludedPaths.Add("/health");
        });
        var client = app.Client();

        for (var i = 0; i < 5; i++)
        {
            Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/health")).StatusCode);
        }

        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/")).StatusCode);
        Assert.Equal(HttpStatusCode.TooManyRequests, (await client.GetAsync("/")).StatusCode);
    }

    [Fact]
    public async Task Rate_limit_can_be_turned_off()
    {
        await using var app = await TestApp.StartAsync(o =>
        {
            o.Security.RateLimit.PermitLimit = 1;
            o.Security.RateLimit.Enabled = false;
        });
        var client = app.Client();

        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/")).StatusCode);
    }

    [Fact]
    public void Invalid_rate_limit_fails_options_validation()
    {
        using var services = new ServiceCollection().AddAspGuardCore(o => o.Security.RateLimit.PermitLimit = 0).BuildServiceProvider();

        Assert.Throws<OptionsValidationException>(() => services.GetRequiredService<IOptions<AspGuardOptions>>().Value);
    }

    [Fact]
    public async Task Kestrel_server_header_is_removed_by_default()
    {
        await using var on = await TestApp.StartAsync();
        await using var off = await TestApp.StartAsync(o => o.Security.Headers.RemoveServerHeader = false);

        Assert.False(on.Services.GetRequiredService<IOptions<KestrelServerOptions>>().Value.AddServerHeader);
        Assert.True(off.Services.GetRequiredService<IOptions<KestrelServerOptions>>().Value.AddServerHeader);
    }

    [Fact]
    public async Task Calling_AddAspGuard_twice_registers_the_middleware_once()
    {
        await using var app = await TestApp.StartAsync(
            o => o.Security.RateLimit.PermitLimit = 2,
            services: s => s.AddAspGuard());
        var client = app.Client();

        // Two middleware instances would each take a permit, so the second request would already be rejected.
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/")).StatusCode);
        Assert.Equal(HttpStatusCode.TooManyRequests, (await client.GetAsync("/")).StatusCode);
    }
}
