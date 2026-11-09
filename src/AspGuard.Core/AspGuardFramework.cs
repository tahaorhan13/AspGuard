#if NETFRAMEWORK
using System.Diagnostics;
using Microsoft.Extensions.Logging;

namespace AspGuard;

/// <summary>
/// AspGuard settings for .NET Framework applications, which have no dependency injection container. The
/// AspGuard.SystemWeb and AspGuard.EntityFramework6 packages register themselves; call <see cref="Configure"/> only
/// to change a default, typically in <c>Application_Start</c> (or at the start of <c>Main</c>).
/// </summary>
public static class AspGuardFramework
{
    private static readonly object Gate = new();
    private static readonly List<Action<AspGuardOptions>> Defaults = [];
    private static readonly List<Action<AspGuardOptions>> Configurations = [];
    private static ILoggerFactory? _loggerFactory;
    private static AspGuardRuntime? _runtime;

    /// <summary>The effective options (defaults plus every <see cref="Configure"/> call). Treat it as read-only.</summary>
    public static AspGuardOptions Options => Runtime.Options;

    /// <summary>
    /// Routes AspGuard's log output to your logging library. Without it, findings are written to
    /// <see cref="Trace"/> (visible in the Visual Studio output window and in any configured trace listener).
    /// </summary>
    public static ILoggerFactory? LoggerFactory
    {
        get => _loggerFactory;
        set
        {
            lock (Gate)
            {
                _loggerFactory = value;
                _runtime = null;
            }
        }
    }

    /// <summary>Changes AspGuard's options. Calls accumulate; later calls win.</summary>
    /// <param name="configure">The configuration.</param>
    public static void Configure(Action<AspGuardOptions> configure)
    {
        if (configure is null)
        {
            throw new ArgumentNullException(nameof(configure));
        }

        lock (Gate)
        {
            Configurations.Add(configure);
            _runtime = Build();
        }
    }

    internal static AspGuardRuntime Runtime
    {
        get
        {
            var runtime = Volatile.Read(ref _runtime);
            if (runtime is not null)
            {
                return runtime;
            }

            lock (Gate)
            {
                return _runtime ??= Build();
            }
        }
    }

    /// <summary>Package defaults (for example "query detection only when debugging"), applied before user configuration.</summary>
    internal static void AddDefault(Action<AspGuardOptions> configure)
    {
        lock (Gate)
        {
            Defaults.Add(configure);
            _runtime = null;
        }
    }

    internal static ILogger CreateLogger(string category) =>
        LoggerFactory?.CreateLogger(category) ?? new TraceLogger(category);

    /// <summary>Forgets every setting (tests only).</summary>
    internal static void Reset()
    {
        lock (Gate)
        {
            Defaults.Clear();
            Configurations.Clear();
            _loggerFactory = null;
            _runtime = null;
        }
    }

    private static AspGuardRuntime Build()
    {
        var options = new AspGuardOptions();
        foreach (var configure in Defaults.Concat(Configurations))
        {
            configure(options);
        }

        var error = OptionsValidation.Validate(options);
        if (error is not null)
        {
            throw new ArgumentException(error);
        }

        return new AspGuardRuntime(options, CreateLogger("AspGuard"));
    }
}

/// <summary>Writes log entries of level Warning and above to <see cref="Trace"/>.</summary>
internal sealed class TraceLogger(string category) : ILogger
{
    public IDisposable BeginScope<TState>(TState state)
        where TState : notnull => NullScope.Instance;

    public bool IsEnabled(LogLevel logLevel) => logLevel >= LogLevel.Warning;

    public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
    {
        if (!IsEnabled(logLevel))
        {
            return;
        }

        var message = $"{category}: {formatter(state, exception)}";
        if (exception is not null)
        {
            message += Environment.NewLine + exception;
        }

        if (logLevel >= LogLevel.Error)
        {
            Trace.TraceError(message);
        }
        else
        {
            Trace.TraceWarning(message);
        }
    }

    private sealed class NullScope : IDisposable
    {
        public static readonly NullScope Instance = new();

        public void Dispose()
        {
        }
    }
}
#endif
