using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;

namespace AspGuard.Tests;

/// <summary>A minimal in-process web app that only calls <c>services.AddAspGuard()</c>.</summary>
internal static class TestApp
{
    public static async Task<WebApplication> StartAsync(
        Action<AspGuardOptions>? configure = null,
        string environment = "Development",
        Action<IServiceCollection>? services = null,
        Action<WebApplication>? endpoints = null)
    {
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions { EnvironmentName = environment });
        builder.WebHost.UseTestServer();
        builder.Services.AddAspGuard(configure);
        services?.Invoke(builder.Services);

        var app = builder.Build();
        app.MapGet("/", () => "ok");
        app.MapPost("/submit", () => "saved");
        app.MapGet("/health", () => "healthy");
        app.MapGet("/custom-frame", CustomFrame);
        endpoints?.Invoke(app);
        await app.StartAsync();
        return app;
    }

    private static string CustomFrame(Microsoft.AspNetCore.Http.HttpContext context)
    {
        context.Response.Headers["X-Frame-Options"] = "SAMEORIGIN";
        return "custom";
    }

    public static HttpClient Client(this WebApplication app, string baseAddress = "http://example.com")
    {
        var client = app.GetTestClient();
        client.BaseAddress = new Uri(baseAddress);
        return client;
    }
}
