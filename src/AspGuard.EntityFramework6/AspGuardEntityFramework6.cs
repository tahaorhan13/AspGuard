using System.Data.Common;
using System.Data.Entity.Infrastructure.Interception;
using System.Web;
using AspGuard.EntityFramework6;

[assembly: PreApplicationStartMethod(typeof(AspGuardEntityFramework6), nameof(AspGuardEntityFramework6.Register))]

namespace AspGuard.EntityFramework6;

/// <summary>
/// Hooks AspGuard into Entity Framework 6. In ASP.NET 4.x applications this runs automatically before the application
/// starts; in console apps, services and tests call <see cref="Register"/> once at startup.
/// </summary>
public static class AspGuardEntityFramework6
{
    private static readonly object Gate = new();
    private static Ef6Interceptor? _interceptor;

    /// <summary>
    /// Adds the AspGuard command interceptor to EF6 (once; later calls do nothing). Queries are recorded into the current
    /// <see cref="AspGuardScope"/>: the HTTP request with AspGuard.SystemWeb, or a scope you <c>Begin</c> yourself.
    /// Configure thresholds and modes with <see cref="AspGuardFramework.Configure"/>.
    /// </summary>
    public static void Register()
    {
        lock (Gate)
        {
            if (_interceptor is not null)
            {
                return;
            }

            _interceptor = new Ef6Interceptor();
            DbInterception.Add(_interceptor);
        }
    }

    /// <summary>Removes the interceptor again (tests only).</summary>
    internal static void Unregister()
    {
        lock (Gate)
        {
            if (_interceptor is not null)
            {
                DbInterception.Remove(_interceptor);
                _interceptor = null;
            }
        }
    }
}

/// <summary>
/// Feeds every EF6 command to the ambient scope. EF6 has no "sensitive data logging" switch, so parameter values are never
/// included, whatever <see cref="AspGuardOptions.IncludeParameterValues"/> says.
/// </summary>
internal sealed class Ef6Interceptor : IDbCommandInterceptor
{
    public void NonQueryExecuting(DbCommand command, DbCommandInterceptionContext<int> interceptionContext) => Record(command);

    public void NonQueryExecuted(DbCommand command, DbCommandInterceptionContext<int> interceptionContext)
    {
    }

    public void ReaderExecuting(DbCommand command, DbCommandInterceptionContext<DbDataReader> interceptionContext) => Record(command);

    public void ReaderExecuted(DbCommand command, DbCommandInterceptionContext<DbDataReader> interceptionContext)
    {
    }

    public void ScalarExecuting(DbCommand command, DbCommandInterceptionContext<object> interceptionContext) => Record(command);

    public void ScalarExecuted(DbCommand command, DbCommandInterceptionContext<object> interceptionContext)
    {
    }

    private static void Record(DbCommand command)
    {
        var scope = AspGuardScope.Current;
        if (scope is null || AspGuardScope.IsSuppressed)
        {
            return;
        }

        var runtime = scope.Runtime ?? AspGuardFramework.Runtime;
        if (!runtime.Options.EnableQueryDetection)
        {
            return;
        }

        runtime.Record(scope, command, 0, static _ => false);
    }
}
