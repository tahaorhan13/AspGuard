namespace AspGuard;

/// <summary>
/// A unit of work whose queries are tracked (an HTTP request, a test, a manual block). Based on <see cref="AsyncLocal{T}"/>,
/// so it flows across <c>await</c> and is isolated between parallel flows. Dispose the scope to report findings in
/// <see cref="AspGuardMode.Log"/> mode.
/// </summary>
public sealed class AspGuardScope : IDisposable
{
    private static readonly AsyncLocal<AspGuardScope?> _current = new();
    private static readonly AsyncLocal<bool> _suppressed = new();

    private readonly AspGuardScope? _previous;
    private readonly object _gate = new();
    private readonly Dictionary<string, ShapeStats> _shapes = new(StringComparer.Ordinal);
    private readonly HashSet<string> _reported = new(StringComparer.Ordinal);
    private AspGuardRuntime? _runtime;
    private int _total;
    private CallSite? _excessiveSite;
    private int _disposed;

    private AspGuardScope(string name)
    {
        Name = name;
        _previous = _current.Value;
        _current.Value = this;
    }

    /// <summary>Name given to <see cref="Begin(string?)"/>.</summary>
    public string Name { get; private set; }

    /// <summary>Number of commands executed (and not suppressed) in this scope so far.</summary>
    public int QueryCount
    {
        get
        {
            lock (_gate)
            {
                return _total;
            }
        }
    }

    internal static AspGuardScope? Current => _current.Value;

    internal static bool IsSuppressed => _suppressed.Value;

    internal AspGuardRuntime? Runtime => Volatile.Read(ref _runtime);

    /// <summary>Starts a scope. The previous scope (if any) is restored when this one is disposed.</summary>
    /// <param name="name">A label used in reports.</param>
    public static AspGuardScope Begin(string? name = null) => new(name ?? "default");

    /// <summary>Excludes queries executed until the returned handle is disposed (deliberate bulk work).</summary>
    public static IDisposable Suppress()
    {
        var handle = new SuppressionHandle(_suppressed.Value);
        _suppressed.Value = true;
        return handle;
    }

    /// <summary>Returns the findings detected so far, using the thresholds of the DbContext's AspGuard configuration.</summary>
    public IReadOnlyList<AspGuardFinding> GetFindings() => Analyze(includeReported: true, applyBaseline: true);

    /// <summary>Throws <see cref="AspGuardException"/> if a suspected N+1 was detected.</summary>
    public void AssertNoNPlusOne()
    {
        var found = GetFindings().Where(f => f.Kind == FindingKind.NPlusOne).ToList();
        if (found.Count > 0)
        {
            throw new AspGuardException(string.Join(Environment.NewLine, found.Select(f => f.Message)), found[0]);
        }
    }

    /// <summary>Throws <see cref="AspGuardException"/> if more than <paramref name="max"/> queries were executed.</summary>
    public void AssertMaxQueries(int max)
    {
        var count = QueryCount;
        if (count > max)
        {
            throw new AspGuardException($"Expected at most {max} queries in scope '{Name}' but {count} were executed.");
        }
    }

    /// <inheritdoc />
    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
        {
            return;
        }

        _current.Value = _previous;
        _runtime?.ReportAtScopeEnd(this);
    }

    internal void Bind(AspGuardRuntime runtime) => Interlocked.CompareExchange(ref _runtime, runtime, null);

    /// <summary>Starts a scope already bound to <paramref name="runtime"/> (used by the request middleware).</summary>
    internal static AspGuardScope Begin(string name, AspGuardRuntime runtime)
    {
        var scope = new AspGuardScope(name);
        scope.Bind(runtime);
        return scope;
    }

    /// <summary>Renames the scope, e.g. to the route pattern once routing has run. Findings built later use the new name.</summary>
    internal void Rename(string name) => Name = name;

    internal (ShapeStats Stats, int Count, int Total) Add(ShapeInfo shape)
    {
        lock (_gate)
        {
            _total++;
            if (!_shapes.TryGetValue(shape.Normalized, out var stats))
            {
                stats = new ShapeStats(shape);
                _shapes[shape.Normalized] = stats;
            }

            stats.Count++;
            return (stats, stats.Count, _total);
        }
    }

    internal void AttachSite(ShapeStats stats, CallSite site)
    {
        lock (_gate)
        {
            if (stats.LastSite is { } previous && previous.Display != site.Display)
            {
                stats.SameSite = false;
            }

            stats.LastSite = site;
            stats.SiteSamples++;
        }
    }

    internal void AttachExcessiveSite(CallSite site)
    {
        lock (_gate)
        {
            _excessiveSite = site;
        }
    }

    internal void SetParameters(ShapeStats stats, string parameters)
    {
        lock (_gate)
        {
            stats.ExampleParameters ??= parameters;
        }
    }

    internal bool NeedsParameters(ShapeStats stats)
    {
        lock (_gate)
        {
            return stats.ExampleParameters is null;
        }
    }

    internal bool MarkReported(string key)
    {
        lock (_gate)
        {
            return _reported.Add(key);
        }
    }

    internal AspGuardFinding BuildNPlusOne(ShapeStats stats)
    {
        lock (_gate)
        {
            return NPlusOneFrom(stats);
        }
    }

    internal AspGuardFinding BuildExcessive()
    {
        lock (_gate)
        {
            return ExcessiveFrom();
        }
    }

    internal IReadOnlyList<AspGuardFinding> Analyze(bool includeReported, bool applyBaseline)
    {
        var runtime = _runtime;
        var threshold = runtime?.Options.NPlusOneThreshold ?? 5;
        var max = runtime?.Options.MaxQueriesPerScope ?? 30;
        var result = new List<AspGuardFinding>();

        lock (_gate)
        {
            foreach (var stats in _shapes.Values.Where(s => s.Shape.IsQuery && s.Count >= threshold).OrderByDescending(s => s.Count))
            {
                result.Add(NPlusOneFrom(stats));
            }

            if (_total > max)
            {
                result.Add(ExcessiveFrom());
            }

            if (!includeReported)
            {
                result.RemoveAll(f => _reported.Contains(f.Key));
            }
        }

        if (applyBaseline && runtime is not null)
        {
            result.RemoveAll(runtime.IsBaselined);
        }

        return result;
    }

    // Callers hold _gate.
    private AspGuardFinding NPlusOneFrom(ShapeStats stats) =>
        new(
            FindingKind.NPlusOne,
            Name,
            stats.Count,
            _runtime?.Options.NPlusOneThreshold ?? 5,
            stats.Shape.Id,
            stats.Shape.Normalized,
            stats.LastSite?.Display,
            stats.LastSite?.Method,
            stats.SameSite && stats.SiteSamples >= 2,
            stats.ExampleParameters);

    private AspGuardFinding ExcessiveFrom() =>
        new(
            FindingKind.ExcessiveQueries,
            Name,
            _total,
            _runtime?.Options.MaxQueriesPerScope ?? 30,
            null,
            null,
            _excessiveSite?.Display,
            _excessiveSite?.Method,
            false,
            null);

    internal sealed class ShapeStats(ShapeInfo shape)
    {
        public ShapeInfo Shape { get; } = shape;

        public int Count { get; set; }

        public CallSite? LastSite { get; set; }

        public bool SameSite { get; set; } = true;

        public int SiteSamples { get; set; }

        public string? ExampleParameters { get; set; }
    }

    private sealed class SuppressionHandle(bool previous) : IDisposable
    {
        public void Dispose() => _suppressed.Value = previous;
    }
}
