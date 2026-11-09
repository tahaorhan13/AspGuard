using AspGuard;
using AspGuard.Security;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Server.Kestrel.Core;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

namespace Microsoft.Extensions.DependencyInjection;

/// <summary>The single AspGuard registration for ASP.NET Core applications.</summary>
public static class AspGuardServiceCollectionExtensions
{
    /// <summary>
    /// Adds everything AspGuard offers with one call; no other change to <c>Program.cs</c> or to the DbContext is needed:
    /// <list type="bullet">
    /// <item>security middleware in front of the pipeline: security headers, HSTS and HTTPS redirection (outside Development),
    /// cross-site request (CSRF) protection and per-IP rate limiting;</item>
    /// <item>N+1 and excessive query detection for every EF Core DbContext, one scope per HTTP request
    /// (on in Development only, unless <see cref="AspGuardOptions.EnableQueryDetection"/> is set).</item>
    /// </list>
    /// The Roslyn analyzers and code fixes in the package work at build time and need no registration.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <param name="configure">Optional configuration; every measure can be tuned or turned off here.</param>
    public static IServiceCollection AddAspGuard(this IServiceCollection services, Action<AspGuardOptions>? configure = null)
    {
        ArgumentNullException.ThrowIfNull(services);

        if (!services.Any(d => d.ServiceType == typeof(AspGuardMarker)))
        {
            services.AddSingleton<AspGuardMarker>();

            // Environment-based defaults run before the caller's configuration, so explicit settings win.
            services.AddOptions<AspGuardOptions>()
                .Configure<IHostEnvironment>((o, env) => o.EnableQueryDetection = env.IsDevelopment());

            services.AddOptions<KestrelServerOptions>()
                .Configure<IOptions<AspGuardOptions>>((kestrel, guard) =>
                {
                    var security = guard.Value.Security;
                    if (security.Enabled && security.Headers.RemoveServerHeader)
                    {
                        kestrel.AddServerHeader = false;
                    }
                });
        }

        services.AddAspGuardCore(configure);
        services.TryAddSingleton<ClientRateLimiter>();
        services.TryAddEnumerable(ServiceDescriptor.Transient<IStartupFilter, AspGuardStartupFilter>());
        return services;
    }

    /// <summary>Marks that the one-time registrations above were made.</summary>
    private sealed class AspGuardMarker;
}
