using System.Data.Common;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace AspGuard;

/// <summary>Observes every command and feeds it to the ambient <see cref="AspGuardScope"/>. Exits on the first line when disabled.</summary>
internal sealed class AspGuardInterceptor(AspGuardRuntime runtime) : DbCommandInterceptor
{
    private readonly AspGuardOptions _options = runtime.Options;

    public override InterceptionResult<DbDataReader> ReaderExecuting(DbCommand command, CommandEventData eventData, InterceptionResult<DbDataReader> result)
    {
        if (_options.EnableQueryDetection)
        {
            Record(command, eventData);
        }

        return result;
    }

    public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(DbCommand command, CommandEventData eventData, InterceptionResult<DbDataReader> result, CancellationToken cancellationToken = default)
    {
        if (_options.EnableQueryDetection)
        {
            Record(command, eventData);
        }

        return new ValueTask<InterceptionResult<DbDataReader>>(result);
    }

    public override InterceptionResult<int> NonQueryExecuting(DbCommand command, CommandEventData eventData, InterceptionResult<int> result)
    {
        if (_options.EnableQueryDetection)
        {
            Record(command, eventData);
        }

        return result;
    }

    public override ValueTask<InterceptionResult<int>> NonQueryExecutingAsync(DbCommand command, CommandEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default)
    {
        if (_options.EnableQueryDetection)
        {
            Record(command, eventData);
        }

        return new ValueTask<InterceptionResult<int>>(result);
    }

    public override InterceptionResult<object> ScalarExecuting(DbCommand command, CommandEventData eventData, InterceptionResult<object> result)
    {
        if (_options.EnableQueryDetection)
        {
            Record(command, eventData);
        }

        return result;
    }

    public override ValueTask<InterceptionResult<object>> ScalarExecutingAsync(DbCommand command, CommandEventData eventData, InterceptionResult<object> result, CancellationToken cancellationToken = default)
    {
        if (_options.EnableQueryDetection)
        {
            Record(command, eventData);
        }

        return new ValueTask<InterceptionResult<object>>(result);
    }

    private void Record(DbCommand command, CommandEventData eventData)
    {
        var scope = AspGuardScope.Current;
        if (scope is null || AspGuardScope.IsSuppressed)
        {
            return;
        }

        runtime.Record(scope, command, eventData);
    }
}
