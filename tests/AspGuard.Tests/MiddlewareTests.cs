using System.Net;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;

namespace AspGuard.Tests;

public class MiddlewareTests
{
    private static WebApplicationFactory<Program> CreateFactory(List<AspGuardFinding> detected, Action<AspGuardOptions>? configure = null) =>
        new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
            builder.ConfigureServices(services =>
                services.PostConfigure<AspGuardOptions>(o =>
                {
                    o.OnDetected = f =>
                    {
                        lock (detected)
                        {
                            detected.Add(f);
                        }
                    };
                    configure?.Invoke(o);
                })));

    private static int Header(HttpResponseMessage response, string name) =>
        int.Parse(response.Headers.GetValues(name).Single(), System.Globalization.CultureInfo.InvariantCulture);

    [Fact]
    public async Task N_plus_one_endpoint_is_flagged_with_headers_and_call_site()
    {
        var detected = new List<AspGuardFinding>();
        await using var factory = CreateFactory(detected);
        using var client = factory.CreateClient();

        var response = await client.GetAsync("/orders/n-plus-one");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(21, Header(response, "X-AspGuard-Query-Count"));
        Assert.Equal(1, Header(response, "X-AspGuard-Query-Findings"));

        var finding = Assert.Single(detected);
        Assert.Equal(20, finding.Count);
        Assert.Equal("GET /orders/n-plus-one", finding.ScopeName);
        Assert.Contains("OrderEndpoints", finding.CallSite);
        Assert.Contains("NPlusOneAsync", finding.CallSite);
    }

    [Fact]
    public async Task Fixed_endpoint_runs_one_query_and_reports_nothing()
    {
        var detected = new List<AspGuardFinding>();
        await using var factory = CreateFactory(detected);
        using var client = factory.CreateClient();

        var response = await client.GetAsync("/orders/fixed");

        Assert.Equal(1, Header(response, "X-AspGuard-Query-Count"));
        Assert.Equal(0, Header(response, "X-AspGuard-Query-Findings"));
        Assert.Empty(detected);
    }

    [Fact]
    public async Task Concurrent_requests_do_not_share_scopes()
    {
        var detected = new List<AspGuardFinding>();
        await using var factory = CreateFactory(detected);
        using var client = factory.CreateClient();

        var responses = await Task.WhenAll(
            Enumerable.Range(0, 6).Select(i => client.GetAsync(i % 2 == 0 ? "/orders/n-plus-one" : "/orders/fixed")));

        for (var i = 0; i < responses.Length; i++)
        {
            Assert.Equal(i % 2 == 0 ? 21 : 1, Header(responses[i], "X-AspGuard-Query-Count"));
            Assert.Equal(i % 2 == 0 ? 1 : 0, Header(responses[i], "X-AspGuard-Query-Findings"));
        }

        Assert.Equal(3, detected.Count);
    }

    [Fact]
    public async Task Throw_mode_fails_the_request()
    {
        var detected = new List<AspGuardFinding>();
        await using var factory = CreateFactory(detected, o => o.Mode = AspGuardMode.Throw);
        using var client = factory.CreateClient();

        // TestServer surfaces unhandled exceptions to the caller; a real host would answer 500.
        var exception = await Record.ExceptionAsync(async () =>
        {
            var response = await client.GetAsync("/orders/n-plus-one");
            Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);
        });

        if (exception is not null)
        {
            Assert.IsType<AspGuardException>(exception);
        }

        Assert.Single(detected);
    }

    [Fact]
    public async Task Test_scope_around_an_in_process_request_sees_its_queries()
    {
        var detected = new List<AspGuardFinding>();
        await using var factory = CreateFactory(detected);
        factory.Server.PreserveExecutionContext = true;
        using var client = factory.CreateClient();

        using (var scope = AspGuardScope.Begin("test"))
        {
            await client.GetAsync("/orders/fixed");
            scope.AssertNoNPlusOne();
            scope.AssertMaxQueries(1);
        }

        using (var scope = AspGuardScope.Begin("test"))
        {
            await client.GetAsync("/orders/n-plus-one");
            Assert.Throws<AspGuardException>(scope.AssertNoNPlusOne);
            Assert.Throws<AspGuardException>(() => scope.AssertMaxQueries(3));
        }
    }

    [Fact]
    public async Task Disabled_guard_adds_no_headers_and_reports_nothing()
    {
        var detected = new List<AspGuardFinding>();
        await using var factory = CreateFactory(detected, o => o.EnableQueryDetection = false);
        using var client = factory.CreateClient();

        var response = await client.GetAsync("/orders/n-plus-one");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.False(response.Headers.Contains("X-AspGuard-Query-Count"));
        Assert.Empty(detected);
    }

    [Fact]
    public async Task Headers_can_be_turned_off()
    {
        var detected = new List<AspGuardFinding>();
        await using var factory = CreateFactory(detected, o => o.AddResponseHeaders = false);
        using var client = factory.CreateClient();

        var response = await client.GetAsync("/orders/n-plus-one");

        Assert.False(response.Headers.Contains("X-AspGuard-Query-Count"));
        Assert.Single(detected);
    }
}
