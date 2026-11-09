namespace AspGuard;

/// <summary>How findings are surfaced.</summary>
public enum AspGuardMode
{
    /// <summary>Findings are written to <see cref="Microsoft.Extensions.Logging.ILogger"/> when the scope ends.</summary>
    Log,

    /// <summary>A <see cref="AspGuardException"/> is thrown from the query that crosses a threshold. Intended for tests and CI.</summary>
    Throw,
}

/// <summary>Controls when the (expensive) stack trace is captured to find the offending call site.</summary>
public enum CallSiteCapture
{
    /// <summary>Never capture a call site.</summary>
    Never,

    /// <summary>Capture only for queries that bring a query shape close to, or to, the N+1 threshold.</summary>
    OnThreshold,

    /// <summary>Capture on every query. Most precise, most expensive.</summary>
    Always,
}

/// <summary>Which code is monitored.</summary>
public enum GuardScopeMode
{
    /// <summary>All queries executed inside a scope are monitored.</summary>
    AllServices,

    /// <summary>Only queries issued from code marked with <see cref="GuardQueriesAttribute"/> are monitored.</summary>
    AttributedOnly,
}

/// <summary>Configuration for AspGuard: EF Core query detection and (in the AspGuard package) security middleware.</summary>
public sealed class AspGuardOptions
{
    /// <summary>
    /// Turns N+1 / excessive query detection on or off. When <c>false</c> nothing is recorded and the cost is close to zero.
    /// Default <c>true</c>; <c>services.AddAspGuard()</c> in the AspGuard package defaults it to <c>true</c> only in Development.
    /// </summary>
    public bool EnableQueryDetection { get; set; } = true;

    /// <summary>
    /// Watch every DbContext automatically (through EF Core's diagnostic events), so no <c>UseAspGuard()</c> call is needed
    /// on the DbContext options. Contexts that add the AspGuard interceptor explicitly are not counted twice. Default <c>true</c>.
    /// Only applies to <c>services.AddAspGuard()</c> in the AspGuard package.
    /// </summary>
    public bool AutoAttachDbContexts { get; set; } = true;

    /// <summary>Security middleware settings, used by <c>services.AddAspGuard()</c> in the AspGuard package.</summary>
    public SecurityOptions Security { get; } = new();

    /// <summary>A query shape executed this many times (or more) in one scope is reported as a suspected N+1. Default 5.</summary>
    public int NPlusOneThreshold { get; set; } = 5;

    /// <summary>More than this many queries in one scope is reported as excessive. Default 30.</summary>
    public int MaxQueriesPerScope { get; set; } = 30;

    /// <summary>How findings are surfaced. Default <see cref="AspGuardMode.Log"/>.</summary>
    public AspGuardMode Mode { get; set; } = AspGuardMode.Log;

    /// <summary>When to capture the call site. Default <see cref="CallSiteCapture.OnThreshold"/>.</summary>
    public CallSiteCapture CaptureCallSite { get; set; } = CallSiteCapture.OnThreshold;

    /// <summary>Optional handler invoked for every finding, in addition to <see cref="Mode"/>.</summary>
    public Action<AspGuardFinding>? OnDetected { get; set; }

    /// <summary>
    /// Include example parameter values in findings. Default <c>false</c>. Values are only shown when
    /// this is <c>true</c> <b>and</b> <c>EnableSensitiveDataLogging</c> is enabled on the DbContext.
    /// </summary>
    public bool IncludeParameterValues { get; set; }

    /// <summary>Which code is monitored. Default <see cref="GuardScopeMode.AllServices"/>.</summary>
    public GuardScopeMode Scope { get; set; } = GuardScopeMode.AllServices;

    /// <summary>
    /// Optional path of a baseline file. Findings already present in it are not reported
    /// (unless <see cref="UpdateBaseline"/> is set).
    /// </summary>
    public string? BaselineFile { get; set; }

    /// <summary>When <c>true</c>, findings are written to <see cref="BaselineFile"/> instead of being reported.</summary>
    public bool UpdateBaseline { get; set; }

    /// <summary>Add <c>X-AspGuard-Query-Count</c> and <c>X-AspGuard-Query-Findings</c> response headers (ASP.NET Core). Default <c>false</c>.</summary>
    public bool AddResponseHeaders { get; set; }
}
