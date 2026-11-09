using AspGuard;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace Microsoft.Extensions.DependencyInjection
{
    /// <summary>DI registration for the AspGuard core (query detection without ASP.NET Core).</summary>
    public static class AspGuardCoreServiceCollectionExtensions
    {
        /// <summary>
        /// Registers AspGuard query detection and its options for hosts without ASP.NET Core (console apps, workers, tests).
        /// Attach it to a DbContext with <c>UseAspGuard(serviceProvider)</c>. Web applications should call
        /// <c>services.AddAspGuard()</c> from the AspGuard package instead, which also needs no DbContext change.
        /// </summary>
        /// <param name="services">The service collection.</param>
        /// <param name="configure">Optional options configuration.</param>
        public static IServiceCollection AddAspGuardCore(this IServiceCollection services, Action<AspGuardOptions>? configure = null)
        {
            ArgumentNullException.ThrowIfNull(services);

            var builder = services.AddOptions<AspGuardOptions>()
                .Validate(o => o.NPlusOneThreshold >= 2, "NPlusOneThreshold must be at least 2.")
                .Validate(o => o.MaxQueriesPerScope >= 1, "MaxQueriesPerScope must be at least 1.")
                .Validate(o => !o.UpdateBaseline || !string.IsNullOrEmpty(o.BaselineFile), "UpdateBaseline requires BaselineFile.")
                .Validate(o => o.Security.RateLimit.PermitLimit >= 1, "Security.RateLimit.PermitLimit must be at least 1.")
                .Validate(o => o.Security.RateLimit.Window > TimeSpan.Zero, "Security.RateLimit.Window must be positive.")
                .Validate(o => o.Security.Hsts.MaxAge >= TimeSpan.Zero, "Security.Hsts.MaxAge must not be negative.");
            if (configure is not null)
            {
                builder.Configure(configure);
            }

            services.TryAddSingleton(sp => new AspGuardRuntime(
                sp.GetRequiredService<IOptions<AspGuardOptions>>().Value,
                sp.GetService<ILoggerFactory>()?.CreateLogger("AspGuard") ?? NullLogger.Instance));
            services.TryAddSingleton(sp => new AspGuardInterceptor(sp.GetRequiredService<AspGuardRuntime>()));
            return services;
        }
    }
}

namespace Microsoft.EntityFrameworkCore
{
    /// <summary>EF Core registration for AspGuard.</summary>
    public static class AspGuardDbContextOptionsBuilderExtensions
    {
        /// <summary>
        /// Adds the AspGuard interceptor registered by <c>AddAspGuardCore</c> (or <c>AddAspGuard</c>). Not needed in web
        /// applications that call <c>services.AddAspGuard()</c>: every DbContext is watched automatically there.
        /// </summary>
        /// <param name="builder">The options builder.</param>
        /// <param name="serviceProvider">The application service provider.</param>
        public static DbContextOptionsBuilder UseAspGuard(this DbContextOptionsBuilder builder, IServiceProvider serviceProvider)
        {
            ArgumentNullException.ThrowIfNull(builder);
            ArgumentNullException.ThrowIfNull(serviceProvider);

            var interceptor = serviceProvider.GetService<AspGuardInterceptor>()
                ?? throw new InvalidOperationException("AspGuard is not registered. Call services.AddAspGuard() (or AddAspGuardCore()) first.");
            return builder.AddInterceptors(interceptor);
        }

        /// <summary>
        /// Adds a standalone AspGuard interceptor without dependency injection. Reuse the resulting options
        /// (EF caches its internal service provider per interceptor instance).
        /// </summary>
        /// <param name="builder">The options builder.</param>
        /// <param name="configure">Optional options configuration.</param>
        public static DbContextOptionsBuilder UseAspGuard(this DbContextOptionsBuilder builder, Action<AspGuardOptions>? configure = null)
        {
            ArgumentNullException.ThrowIfNull(builder);

            var options = new AspGuardOptions();
            configure?.Invoke(options);
            return builder.AddInterceptors(new AspGuardInterceptor(new AspGuardRuntime(options, NullLogger.Instance)));
        }
    }
}
