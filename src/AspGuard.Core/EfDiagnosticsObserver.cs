using System.Diagnostics;
using System.Runtime.CompilerServices;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Infrastructure;

namespace AspGuard;

/// <summary>
/// Watches every DbContext in the process without configuration, through EF Core's public <see cref="DiagnosticListener"/>
/// events (<see cref="RelationalEventId.CommandExecuting"/>). Commands are routed to the ambient scope's runtime, or to the
/// runtime attached last. Contexts that use <see cref="AspGuardInterceptor"/> explicitly are skipped so nothing is counted twice.
/// </summary>
internal static class EfDiagnosticsObserver
{
    private static readonly string _commandExecuting = RelationalEventId.CommandExecuting.Name!;
    private static readonly ConditionalWeakTable<IDbContextOptions, object> _usesInterceptor = new();
    private static readonly object _yes = new();
    private static readonly object _no = new();
    private static readonly object _gate = new();
    // Copy-on-write; usually one runtime per process (several in test runs that host many apps). The last one is the default.
    private static AspGuardRuntime[] _attached = [];
    private static IDisposable? _subscription;

    /// <summary>Makes <paramref name="runtime"/> the default target and starts listening (once per process).</summary>
    public static void Attach(AspGuardRuntime runtime)
    {
        lock (_gate)
        {
            _attached = [.. _attached.Where(r => !ReferenceEquals(r, runtime)), runtime];
            _subscription ??= DiagnosticListener.AllListeners.Subscribe(new AllListenersObserver());
        }
    }

    /// <summary>Stops routing to <paramref name="runtime"/> (called when the runtime is disposed with its host).</summary>
    public static void Detach(AspGuardRuntime runtime)
    {
        lock (_gate)
        {
            _attached = [.. _attached.Where(r => !ReferenceEquals(r, runtime))];
        }
    }

    // Asked by EF before it builds the event payload; returning false keeps the disabled path allocation-free.
    private static bool IsEnabled(string name)
    {
        if (!string.Equals(name, _commandExecuting, StringComparison.Ordinal))
        {
            return false;
        }

        foreach (var runtime in Volatile.Read(ref _attached))
        {
            if (runtime.Options is { EnableQueryDetection: true, AutoAttachDbContexts: true })
            {
                return true;
            }
        }

        return false;
    }

    private static void OnCommand(object? payload)
    {
        if (payload is not CommandEventData data)
        {
            return;
        }

        var scope = AspGuardScope.Current;
        if (scope is null || AspGuardScope.IsSuppressed)
        {
            return;
        }

        var runtime = scope.Runtime ?? DefaultRuntime();
        if (runtime is null
            || !runtime.Options.EnableQueryDetection
            || !runtime.Options.AutoAttachDbContexts
            || HasExplicitInterceptor(data.Context))
        {
            return;
        }

        runtime.Record(scope, data.Command, data);
    }

    // The most recently attached runtime that is switched on; used for scopes no middleware bound (e.g. manual scopes).
    private static AspGuardRuntime? DefaultRuntime()
    {
        var attached = Volatile.Read(ref _attached);
        for (var i = attached.Length - 1; i >= 0; i--)
        {
            if (attached[i].Options is { EnableQueryDetection: true, AutoAttachDbContexts: true })
            {
                return attached[i];
            }
        }

        return null;
    }

    private static bool HasExplicitInterceptor(DbContext? context)
    {
        if (context is null)
        {
            return false;
        }

        var options = context.GetService<IDbContextOptions>();
        var flag = _usesInterceptor.GetValue(
            options,
            static o => o.FindExtension<CoreOptionsExtension>()?.Interceptors?.Any(i => i is AspGuardInterceptor) == true ? _yes : _no);
        return ReferenceEquals(flag, _yes);
    }

    private sealed class AllListenersObserver : IObserver<DiagnosticListener>
    {
        public void OnNext(DiagnosticListener value)
        {
            if (value.Name == DbLoggerCategory.Name)
            {
                value.Subscribe(new CommandObserver(), IsEnabled);
            }
        }

        public void OnCompleted()
        {
        }

        public void OnError(Exception error)
        {
        }
    }

    private sealed class CommandObserver : IObserver<KeyValuePair<string, object?>>
    {
        public void OnNext(KeyValuePair<string, object?> value)
        {
            if (string.Equals(value.Key, _commandExecuting, StringComparison.Ordinal))
            {
                OnCommand(value.Value);
            }
        }

        public void OnCompleted()
        {
        }

        public void OnError(Exception error)
        {
        }
    }
}
