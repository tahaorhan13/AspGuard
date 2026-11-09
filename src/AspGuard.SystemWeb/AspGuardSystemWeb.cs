using System.Web;
using System.Web.Configuration;
using AspGuard.SystemWeb;

[assembly: PreApplicationStartMethod(typeof(AspGuardSystemWeb), nameof(AspGuardSystemWeb.Start))]

namespace AspGuard.SystemWeb;

/// <summary>
/// Entry point of AspGuard for ASP.NET 4.x. ASP.NET calls <see cref="Start"/> before the application starts, so
/// installing the package is the whole setup. Change defaults with <see cref="AspGuardFramework.Configure"/> in
/// <c>Application_Start</c>.
/// </summary>
public static class AspGuardSystemWeb
{
    private static readonly object _gate = new();
    private static bool _started;

    /// <summary>
    /// <c>true</c> when <c>&lt;compilation debug="true"&gt;</c> is set in web.config, which AspGuard treats as the
    /// development environment: query detection is on, HSTS is not sent.
    /// </summary>
    public static bool IsDebugging { get; private set; }

    /// <summary>
    /// Registers the AspGuard HTTP module. Called by ASP.NET through <see cref="PreApplicationStartMethodAttribute"/>;
    /// you do not need to call it.
    /// </summary>
    public static void Start()
    {
        lock (_gate)
        {
            if (_started)
            {
                return;
            }

            _started = true;
            IsDebugging = ReadDebugFlag();
            ApplyDefaults(IsDebugging);
            AspGuardScope.AmbientFallback = static () => HttpContext.Current?.Items[RequestGuard.ScopeKey] as AspGuardScope;
            HttpApplication.RegisterModule(typeof(AspGuardModule));
        }
    }

    /// <summary>
    /// ASP.NET 4.x defaults on top of the shared ones: query detection follows the debug flag, and HTTPS redirection is
    /// opt-in because the module cannot know the HTTPS port and TLS is often terminated by a load balancer.
    /// </summary>
    internal static void ApplyDefaults(bool debugging) =>
        AspGuardFramework.AddDefault(o =>
        {
            o.EnableQueryDetection = debugging;
            o.Security.HttpsRedirection = false;
        });

    private static bool ReadDebugFlag()
    {
        try
        {
            return WebConfigurationManager.GetSection("system.web/compilation") is CompilationSection { Debug: true };
        }
        catch (InvalidOperationException)
        {
            return false;
        }
        catch (System.Configuration.ConfigurationException)
        {
            return false;
        }
    }
}
