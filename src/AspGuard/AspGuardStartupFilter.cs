using AspGuard.Security;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

namespace AspGuard;

/// <summary>
/// Puts the AspGuard middleware in front of the application's own pipeline, so <c>services.AddAspGuard()</c> is the only
/// line the user writes. Order: security headers and HSTS, HTTPS redirection, cross-site protection, rate limiting,
/// query scope; then the application.
/// </summary>
internal sealed class AspGuardStartupFilter(
    IOptions<AspGuardOptions> options,
    AspGuardRuntime runtime,
    IHostEnvironment environment) : IStartupFilter
{
    public Action<IApplicationBuilder> Configure(Action<IApplicationBuilder> next) => app =>
    {
        var guard = options.Value;
        var security = guard.Security;
        var development = environment.IsDevelopment();

        if (security.Enabled)
        {
            if (security.Headers.Enabled || (security.Hsts.Enabled && !development))
            {
                app.UseMiddleware<SecurityHeadersMiddleware>();
            }

            if (security.HttpsRedirection && !development)
            {
                app.UseHttpsRedirection();
            }

            if (security.CrossSiteProtection.Enabled)
            {
                app.UseMiddleware<CrossSiteRequestMiddleware>();
            }

            if (security.RateLimit.Enabled)
            {
                app.UseMiddleware<RateLimitMiddleware>();
            }
        }

        if (guard.AutoAttachDbContexts)
        {
            // Detached again when the container disposes the runtime.
            EfDiagnosticsObserver.Attach(runtime);
        }

        app.UseMiddleware<QueryScopeMiddleware>();
        next(app);
    };
}
