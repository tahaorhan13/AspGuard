using AspGuard;
using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Running;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

BenchmarkRunner.Run<InterceptorOverhead>(args: args);

public class Item
{
    public int Id { get; set; }

    public string Name { get; set; } = "";
}

public class BenchContext(DbContextOptions options) : DbContext(options)
{
    public DbSet<Item> Items => Set<Item>();
}

/// <summary>
/// Cost of AspGuard per query: 20 identical-shape SELECTs against in-memory SQLite inside one scope
/// (like a request that has an N+1). Results are per query (OperationsPerInvoke = 20).
/// </summary>
[MemoryDiagnoser]
public class InterceptorOverhead
{
    private const int QueriesPerScope = 20;

    private SqliteConnection _keeper = null!;
    private BenchContext _none = null!;
    private BenchContext _disabled = null!;
    private BenchContext _noCallSite = null!;
    private BenchContext _onThreshold = null!;
    private BenchContext _always = null!;
    private BenchContext _autoAttach = null!;
    private AspGuardRuntime? _autoRuntime;

    [GlobalSetup]
    public void Setup()
    {
        var connectionString = $"Data Source=bench-{Guid.NewGuid():N};Mode=Memory;Cache=Shared";
        _keeper = new SqliteConnection(connectionString);
        _keeper.Open();

        _none = Create(connectionString, null);
        _none.Database.EnsureCreated();
        _none.Items.AddRange(Enumerable.Range(1, 50).Select(i => new Item { Name = $"item-{i}" }));
        _none.SaveChanges();

        _disabled = Create(connectionString, o => o.EnableQueryDetection = false);
        _noCallSite = Create(connectionString, o => o.CaptureCallSite = CallSiteCapture.Never);
        _onThreshold = Create(connectionString, o => o.CaptureCallSite = CallSiteCapture.OnThreshold);
        _always = Create(connectionString, o => o.CaptureCallSite = CallSiteCapture.Always);
        _autoAttach = Create(connectionString, null);
    }

    /// <summary>
    /// The zero-config path of services.AddAspGuard(): no interceptor, commands arrive through EF Core's DiagnosticListener.
    /// Attached only for this benchmark (each case runs in its own process), so the other cases are not affected.
    /// </summary>
    [GlobalSetup(Target = nameof(AutoAttach_CallSiteOnThreshold))]
    public void SetupAutoAttach()
    {
        Setup();
        _autoRuntime = new AspGuardRuntime(new AspGuardOptions { NPlusOneThreshold = 5 }, NullLogger.Instance);
        EfDiagnosticsObserver.Attach(_autoRuntime);
    }

    [GlobalCleanup]
    public void Cleanup()
    {
        _autoRuntime?.Dispose();
        _none.Dispose();
        _disabled.Dispose();
        _noCallSite.Dispose();
        _onThreshold.Dispose();
        _always.Dispose();
        _autoAttach.Dispose();
        _keeper.Dispose();
    }

    [Benchmark(Baseline = true, OperationsPerInvoke = QueriesPerScope)]
    public void WithoutAspGuard() => Run(_none, scoped: false);

    [Benchmark(OperationsPerInvoke = QueriesPerScope)]
    public void Disabled() => Run(_disabled, scoped: true);

    [Benchmark(OperationsPerInvoke = QueriesPerScope)]
    public void Enabled_NoCallSite() => Run(_noCallSite, scoped: true);

    [Benchmark(OperationsPerInvoke = QueriesPerScope)]
    public void Enabled_CallSiteOnThreshold() => Run(_onThreshold, scoped: true);

    [Benchmark(OperationsPerInvoke = QueriesPerScope)]
    public void Enabled_CallSiteAlways() => Run(_always, scoped: true);

    [Benchmark(OperationsPerInvoke = QueriesPerScope)]
    public void AutoAttach_CallSiteOnThreshold() => Run(_autoAttach, scoped: true);

    private static BenchContext Create(string connectionString, Action<AspGuardOptions>? configure)
    {
        var builder = new DbContextOptionsBuilder<BenchContext>().UseSqlite(connectionString);
        if (configure is not null)
        {
            builder.UseAspGuard(o =>
            {
                o.NPlusOneThreshold = 5;
                configure(o);
            });
        }

        return new BenchContext(builder.Options);
    }

    private static void Run(BenchContext context, bool scoped)
    {
        using var scope = scoped ? AspGuardScope.Begin("bench") : null;
        for (var i = 1; i <= QueriesPerScope; i++)
        {
            var id = i;
            _ = context.Items.AsNoTracking().FirstOrDefault(x => x.Id == id);
        }
    }
}
