namespace AspGuard;

/// <summary>The kind of problem a <see cref="AspGuardFinding"/> describes.</summary>
public enum FindingKind
{
    /// <summary>The same query shape was executed many times.</summary>
    NPlusOne,

    /// <summary>The scope executed more queries than <see cref="AspGuardOptions.MaxQueriesPerScope"/>.</summary>
    ExcessiveQueries,
}

/// <summary>A problem detected inside an <see cref="AspGuardScope"/>. Never contains parameter values unless explicitly enabled.</summary>
public sealed class AspGuardFinding
{
    internal AspGuardFinding(
        FindingKind kind,
        string scopeName,
        int count,
        int threshold,
        string? shapeId,
        string? shape,
        string? callSite,
        string? callSiteMethod,
        bool sameCallSite,
        string? exampleParameters)
    {
        Kind = kind;
        ScopeName = scopeName;
        Count = count;
        Threshold = threshold;
        ShapeId = shapeId;
        Shape = shape;
        CallSite = callSite;
        CallSiteMethod = callSiteMethod;
        SameCallSite = sameCallSite;
        ExampleParameters = exampleParameters;
    }

    /// <summary>What was detected.</summary>
    public FindingKind Kind { get; }

    /// <summary>Name of the scope the finding belongs to.</summary>
    public string ScopeName { get; }

    /// <summary>Repetitions of the shape (<see cref="FindingKind.NPlusOne"/>) or total queries (<see cref="FindingKind.ExcessiveQueries"/>).</summary>
    public int Count { get; }

    /// <summary>The configured threshold that was reached.</summary>
    public int Threshold { get; }

    /// <summary>Stable short id of the query shape, or <c>null</c> for excessive-query findings.</summary>
    public string? ShapeId { get; }

    /// <summary>Normalized SQL (literals and parameters replaced by <c>?</c>), or <c>null</c> for excessive-query findings.</summary>
    public string? Shape { get; }

    /// <summary>First frame in user code as <c>file:line (Type.Method)</c>, when captured.</summary>
    public string? CallSite { get; }

    /// <summary>Type and method of <see cref="CallSite"/>, when captured.</summary>
    public string? CallSiteMethod { get; }

    /// <summary>All captured executions came from the same call site: a strong N+1 signal.</summary>
    public bool SameCallSite { get; }

    /// <summary>Example parameter values; only set when <see cref="AspGuardOptions.IncludeParameterValues"/> and sensitive data logging are both on.</summary>
    public string? ExampleParameters { get; }

    internal string Key => $"{Kind}|{ShapeId ?? ScopeName}|{CallSiteMethod}";

    /// <summary>Human-readable description.</summary>
    public string Message
    {
        get
        {
            var site = CallSite is null ? string.Empty : $" at {CallSite}";
            var parameters = ExampleParameters is null ? string.Empty : $" Parameters (example): {ExampleParameters}.";
            return Kind == FindingKind.NPlusOne
                ? $"Possible N+1: query shape {ShapeId} ran {Count} times (threshold {Threshold}) in scope '{ScopeName}'{site}"
                  + (SameCallSite ? " (same call site)" : string.Empty)
                  + $". Shape: {Shape}.{parameters}"
                : $"Excessive queries: {Count} queries (max {Threshold}) in scope '{ScopeName}'{site}.";
        }
    }

    /// <inheritdoc />
    public override string ToString() => Message;
}

/// <summary>Thrown in <see cref="AspGuardMode.Throw"/> mode and by the scope assertions.</summary>
public sealed class AspGuardException : Exception
{
    internal AspGuardException(string message, AspGuardFinding? finding = null)
        : base(message)
    {
        Finding = finding;
    }

    /// <summary>The finding that caused the exception, when there is one.</summary>
    public AspGuardFinding? Finding { get; }
}
