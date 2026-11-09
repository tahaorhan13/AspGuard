using System.Globalization;
using AspGuard.Sample.Api;
using Microsoft.AspNetCore.Builder;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace AspGuard.Tests;

/// <summary><c>services.AddAspGuard()</c> alone watches DbContexts; no <c>UseAspGuard()</c> on the context is needed.</summary>
public sealed class AutoAttachTests : IDisposable
{
    private readonly string _connectionString = $"Data Source=auto-{Guid.NewGuid():N};Mode=Memory;Cache=Shared";
    private readonly SqliteConnection _keeper;

    public AutoAttachTests()
    {
        _keeper = new SqliteConnection(_connectionString);
        _keeper.Open();
    }

    public void Dispose() => _keeper.Dispose();

    private async Task<WebApplication> StartAsync(bool explicitInterceptor, Action<AspGuardOptions>? configure = null, string environment = "Development")
    {
        var app = await TestApp.StartAsync(
            o =>
            {
                o.AddResponseHeaders = true;
                configure?.Invoke(o);
            },
            environment,
            services => services.AddDbContext<ShopDb>((sp, options) =>
            {
                options.UseSqlite(_connectionString);
                if (explicitInterceptor)
                {
                    options.UseAspGuard(sp);
                }
            }),
            MapEndpoints);

        using var scope = app.Services.CreateScope();
        SeedData.Create(scope.ServiceProvider.GetRequiredService<ShopDb>());
        return app;
    }

    private static void MapEndpoints(WebApplication app)
    {
        app.MapGet("/orders/n-plus-one", (ShopDb db) => OrderEndpoints.NPlusOneAsync(db));
        app.MapGet("/orders/{id:int}", OrderWithTotalAsync);
    }

    private static async Task<int> OrderWithTotalAsync(int id, ShopDb db)
    {
        var order = await db.Orders.FindAsync(id);
        return order is null ? 0 : await db.Orders.CountAsync();
    }

    private static int Count(HttpResponseMessage response) =>
        int.Parse(response.Headers.GetValues("X-AspGuard-Query-Count").Single(), CultureInfo.InvariantCulture);

    [Fact]
    public async Task DbContext_without_any_AspGuard_call_is_watched()
    {
        var detected = new List<AspGuardFinding>();
        await using var app = await StartAsync(explicitInterceptor: false, o => o.OnDetected = detected.Add);

        var response = await app.Client().GetAsync("/orders/n-plus-one");

        Assert.Equal(21, Count(response));
        var finding = Assert.Single(detected);
        Assert.Equal("GET /orders/n-plus-one", finding.ScopeName);
        Assert.Contains("NPlusOneAsync", finding.CallSite);
    }

    [Fact]
    public async Task Explicit_interceptor_is_not_counted_twice()
    {
        await using var app = await StartAsync(explicitInterceptor: true);

        var response = await app.Client().GetAsync("/orders/n-plus-one");

        Assert.Equal(21, Count(response));
    }

    [Fact]
    public async Task Scope_name_uses_the_route_pattern_not_the_path()
    {
        var detected = new List<AspGuardFinding>();
        await using var app = await StartAsync(explicitInterceptor: false, o =>
        {
            o.NPlusOneThreshold = 100;
            o.MaxQueriesPerScope = 1;
            o.OnDetected = detected.Add;
        });

        await app.Client().GetAsync("/orders/7");

        var finding = Assert.Single(detected);
        Assert.Equal(FindingKind.ExcessiveQueries, finding.Kind);
        Assert.Equal("GET /orders/{id:int}", finding.ScopeName);
    }

    [Fact]
    public async Task Query_detection_is_off_outside_development_by_default()
    {
        await using var app = await StartAsync(explicitInterceptor: false, environment: "Production");

        var response = await app.Client("https://example.com").GetAsync("/orders/n-plus-one");

        Assert.False(response.Headers.Contains("X-AspGuard-Query-Count"));
        Assert.False(app.Services.GetRequiredService<IOptions<AspGuardOptions>>().Value.EnableQueryDetection);
    }

    [Fact]
    public async Task Explicit_setting_overrides_the_environment_default()
    {
        await using var app = await StartAsync(explicitInterceptor: false, o => o.EnableQueryDetection = true, environment: "Production");

        var response = await app.Client("https://example.com").GetAsync("/orders/n-plus-one");

        Assert.Equal(21, Count(response));
    }

    [Fact]
    public async Task Auto_attach_can_be_turned_off()
    {
        await using var app = await StartAsync(explicitInterceptor: false, o => o.AutoAttachDbContexts = false);

        var response = await app.Client().GetAsync("/orders/n-plus-one");

        Assert.Equal(0, Count(response));
    }
}
