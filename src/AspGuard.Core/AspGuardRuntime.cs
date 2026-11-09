using System.Data.Common;
using System.Globalization;
using System.Text;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.Logging;

namespace AspGuard;

/// <summary>Detection logic shared by all interceptor entry points.</summary>
internal sealed class AspGuardRuntime : IDisposable
{
    private readonly ILogger _logger;
    private readonly Baseline? _baseline;

    public AspGuardRuntime(AspGuardOptions options, ILogger logger)
    {
        Options = options;
        _logger = logger;
        _baseline = string.IsNullOrEmpty(options.BaselineFile) ? null : new Baseline(options.BaselineFile);
    }

    public AspGuardOptions Options { get; }

    /// <summary>Disposed with the host's container; stops automatic DbContext routing to this runtime.</summary>
    public void Dispose() => EfDiagnosticsObserver.Detach(this);

    public bool IsBaselined(AspGuardFinding finding) =>
        _baseline is not null && !Options.UpdateBaseline && _baseline.Contains(finding);

    public void Record(AspGuardScope scope, DbCommand command, CommandEventData eventData)
    {
        var options = Options;
        scope.Bind(this);
        var shape = ShapeInfo.Cache.Get(command.CommandText);

        CallSite? site = null;
        if (options.Scope == GuardScopeMode.AttributedOnly)
        {
            site = CallSiteResolver.Capture(checkAttributes: true, out var attributed);
            if (!attributed)
            {
                return;
            }
        }

        var (stats, count, total) = scope.Add(shape);
        var threshold = options.NPlusOneThreshold;

        if (site is null && shape.IsQuery)
        {
            var capture = options.CaptureCallSite switch
            {
                CallSiteCapture.Always => true,
                CallSiteCapture.OnThreshold => (count >= Math.Max(1, threshold - 1) && count <= threshold) || total == options.MaxQueriesPerScope + 1,
                _ => false,
            };

            if (capture)
            {
                site = CallSiteResolver.Capture(checkAttributes: false, out _);
            }
        }

        if (site is { } captured)
        {
            scope.AttachSite(stats, captured);
            if (total == options.MaxQueriesPerScope + 1)
            {
                scope.AttachExcessiveSite(captured);
            }
        }

        if (options.IncludeParameterValues && scope.NeedsParameters(stats) && IsSensitiveLoggingEnabled(eventData.Context))
        {
            scope.SetParameters(stats, FormatParameters(command));
        }

        if (options.Mode != AspGuardMode.Throw)
        {
            return;
        }

        if (shape.IsQuery && count == threshold)
        {
            Throw(scope, scope.BuildNPlusOne(stats));
        }

        if (total == options.MaxQueriesPerScope + 1)
        {
            Throw(scope, scope.BuildExcessive());
        }
    }

    public void ReportAtScopeEnd(AspGuardScope scope)
    {
        var findings = scope.Analyze(includeReported: false, applyBaseline: true);
        if (findings.Count == 0)
        {
            return;
        }

        if (Options.UpdateBaseline)
        {
            _baseline?.Add(findings);
            return;
        }

        if (Options.Mode == AspGuardMode.Throw)
        {
            return;
        }

        foreach (var finding in findings)
        {
            Log(finding);
            try
            {
                Options.OnDetected?.Invoke(finding);
            }
            catch (Exception exception)
            {
                AspGuardLog.CallbackFailed(_logger, exception);
            }
        }
    }

    private void Throw(AspGuardScope scope, AspGuardFinding finding)
    {
        if (!scope.MarkReported(finding.Key) || IsBaselined(finding))
        {
            return;
        }

        if (Options.UpdateBaseline)
        {
            _baseline?.Add([finding]);
            return;
        }

        Options.OnDetected?.Invoke(finding);
        throw new AspGuardException(finding.Message, finding);
    }

    private void Log(AspGuardFinding finding)
    {
        var parameters = finding.ExampleParameters is null ? null : $" Parameters (example): {finding.ExampleParameters}";
        if (finding.Kind == FindingKind.NPlusOne)
        {
            AspGuardLog.NPlusOne(_logger, finding.ScopeName, finding.ShapeId, finding.Count, finding.Threshold, finding.SameCallSite, finding.CallSite, finding.Shape, parameters);
        }
        else
        {
            AspGuardLog.Excessive(_logger, finding.ScopeName, finding.Count, finding.Threshold, finding.CallSite);
        }
    }

    private static bool IsSensitiveLoggingEnabled(DbContext? context) =>
        context?.GetService<IDbContextOptions>().FindExtension<CoreOptionsExtension>()?.IsSensitiveDataLoggingEnabled == true;

    private static string FormatParameters(DbCommand command)
    {
        var sb = new StringBuilder();
        foreach (DbParameter parameter in command.Parameters)
        {
            if (sb.Length > 0)
            {
                sb.Append(", ");
            }

            var value = parameter.Value is null or DBNull ? "NULL" : Convert.ToString(parameter.Value, CultureInfo.InvariantCulture) ?? "NULL";
            if (value.Length > 100)
            {
                value = value[..100] + "…";
            }

            sb.Append(parameter.ParameterName).Append("='").Append(value).Append('\'');
        }

        return sb.ToString();
    }
}

internal static partial class AspGuardLog
{
    [LoggerMessage(EventId = 1, Level = LogLevel.Warning, Message = "N+1 suspected in scope '{Scope}': query shape {ShapeId} executed {Count} times (threshold {Threshold}, same call site: {SameCallSite}) at {CallSite}. Shape: {Shape}.{Parameters}")]
    public static partial void NPlusOne(ILogger logger, string scope, string? shapeId, int count, int threshold, bool sameCallSite, string? callSite, string? shape, string? parameters);

    [LoggerMessage(EventId = 2, Level = LogLevel.Warning, Message = "Excessive queries in scope '{Scope}': {Count} queries executed (max {Max}). Last threshold crossed at {CallSite}")]
    public static partial void Excessive(ILogger logger, string scope, int count, int max, string? callSite);

    [LoggerMessage(EventId = 3, Level = LogLevel.Error, Message = "The AspGuard OnDetected callback threw an exception.")]
    public static partial void CallbackFailed(ILogger logger, Exception exception);
}
