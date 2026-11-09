using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace AspGuard.Core.Tests;

public class DetectionTests
{
    private static void LoopPosts(ShopContext context, int times)
    {
        for (var i = 1; i <= times; i++)
        {
            var id = i;
            _ = context.Posts.Where(p => p.BlogId == id).ToList();
        }
    }

    [Fact]
    public void Query_in_loop_is_reported_as_NPlusOne_with_call_site()
    {
        using var db = new TestDb();
        using var scope = AspGuardScope.Begin("loop");
        using var context = db.NewContext();

        foreach (var blog in context.Blogs.ToList())
        {
            _ = context.Posts.Where(p => p.BlogId == blog.Id).ToList();
        }

        var finding = Assert.Single(scope.GetFindings());
        Assert.Equal(FindingKind.NPlusOne, finding.Kind);
        Assert.Equal(10, finding.Count);
        Assert.Equal("loop", finding.ScopeName);
        Assert.True(finding.SameCallSite);
        Assert.Contains(nameof(Query_in_loop_is_reported_as_NPlusOne_with_call_site), finding.CallSite);
        Assert.Contains("DetectionTests.cs", finding.CallSite);
        Assert.Contains("FROM \"Posts\"", finding.Shape);
        Assert.Throws<AspGuardException>(scope.AssertNoNPlusOne);
    }

    [Fact]
    public async Task Async_query_in_loop_is_reported_with_call_site()
    {
        using var db = new TestDb();
        using var scope = AspGuardScope.Begin();
        await using var context = db.NewContext();

        foreach (var blog in await context.Blogs.ToListAsync())
        {
            _ = await context.Posts.Where(p => p.BlogId == blog.Id).ToListAsync();
        }

        var finding = Assert.Single(scope.GetFindings());
        Assert.Equal(10, finding.Count);
        Assert.Contains(nameof(Async_query_in_loop_is_reported_with_call_site), finding.CallSite);
    }

    [Fact]
    public void Include_fixes_the_problem()
    {
        using var db = new TestDb();
        using var scope = AspGuardScope.Begin();
        using var context = db.NewContext();

        foreach (var blog in context.Blogs.Include(b => b.Posts).ToList())
        {
            Assert.Equal(2, blog.Posts.Count);
        }

        Assert.Equal(1, scope.QueryCount);
        Assert.Empty(scope.GetFindings());
        scope.AssertNoNPlusOne();
        scope.Dispose();
        Assert.Empty(db.Detected);
    }

    [Fact]
    public void Lazy_loading_N_plus_one_is_detected_at_the_code_that_touched_the_navigation()
    {
        using var db = new TestDb();
        using var scope = AspGuardScope.Begin();
        using var context = db.NewContext();

        foreach (var blog in context.LazyBlogs.ToList())
        {
            Assert.Equal(2, blog.Posts.Count);
        }

        var finding = Assert.Single(scope.GetFindings());
        Assert.Equal(10, finding.Count);
        Assert.Contains("LazyPosts", finding.Shape);
        Assert.Contains(nameof(Lazy_loading_N_plus_one_is_detected_at_the_code_that_touched_the_navigation), finding.CallSite);
    }

    [Theory]
    [InlineData(4, false)]
    [InlineData(5, true)]
    [InlineData(6, true)]
    public void Threshold_is_inclusive(int times, bool expected)
    {
        using var db = new TestDb();
        using var scope = AspGuardScope.Begin();
        using var context = db.NewContext();

        LoopPosts(context, times);

        Assert.Equal(expected, scope.GetFindings().Count == 1);
    }

    [Fact]
    public void Suppressed_queries_are_neither_counted_nor_reported()
    {
        using var db = new TestDb();
        using var scope = AspGuardScope.Begin();
        using var context = db.NewContext();

        using (AspGuardScope.Suppress())
        {
            LoopPosts(context, 10);
        }

        Assert.Equal(0, scope.QueryCount);
        Assert.Empty(scope.GetFindings());

        LoopPosts(context, 1);
        Assert.Equal(1, scope.QueryCount);
    }

    [Fact]
    public async Task Scopes_are_isolated_between_parallel_flows()
    {
        using var db = new TestDb();

        async Task<(int Count, int Findings)> Run(string name, int times)
        {
            using var scope = AspGuardScope.Begin(name);
            await using var context = db.NewContext();
            for (var i = 1; i <= times; i++)
            {
                var id = i;
                await Task.Yield();
                _ = await context.Posts.Where(p => p.BlogId == id).ToListAsync();
            }

            return (scope.QueryCount, scope.GetFindings().Count);
        }

        var results = await Task.WhenAll(Task.Run(() => Run("a", 10)), Task.Run(() => Run("b", 2)), Task.Run(() => Run("c", 7)));

        Assert.Equal([(10, 1), (2, 0), (7, 1)], results);
        Assert.Null(AspGuardScope.Current);
    }

    [Fact]
    public async Task One_scope_collects_queries_from_child_tasks()
    {
        using var db = new TestDb();
        using var scope = AspGuardScope.Begin();

        await Task.WhenAll(Enumerable.Range(0, 4).Select(async worker =>
        {
            await using var context = db.NewContext();
            for (var i = 1; i <= 3; i++)
            {
                var id = i;
                _ = await context.Posts.Where(p => p.BlogId == id).ToListAsync();
            }
        }));

        Assert.Equal(12, scope.QueryCount);
        Assert.Equal(12, Assert.Single(scope.GetFindings()).Count);
    }

    [Fact]
    public void Scopes_nest_and_restore_the_previous_one()
    {
        var outer = AspGuardScope.Begin("outer");
        using (var inner = AspGuardScope.Begin("inner"))
        {
            Assert.Same(inner, AspGuardScope.Current);
        }

        Assert.Same(outer, AspGuardScope.Current);
        outer.Dispose();
        Assert.Null(AspGuardScope.Current);
    }

    [Fact]
    public void Throw_mode_throws_from_the_query_that_reaches_the_threshold()
    {
        using var db = new TestDb(o => o.Mode = AspGuardMode.Throw);
        using var scope = AspGuardScope.Begin();
        using var context = db.NewContext();

        var exception = Assert.Throws<AspGuardException>(() => LoopPosts(context, 10));

        Assert.Equal(5, exception.Finding!.Count);
        Assert.Contains(nameof(LoopPosts), exception.Finding.CallSite);
        Assert.Equal(5, scope.QueryCount);
        Assert.Single(db.Detected);
    }

    [Fact]
    public void Excessive_query_count_is_reported_and_logged_when_the_scope_ends()
    {
        using var db = new TestDb(o => o.MaxQueriesPerScope = 3);
        var scope = AspGuardScope.Begin("busy");
        using var context = db.NewContext();

        _ = context.Blogs.Count();
        _ = context.Posts.Count();
        _ = context.Blogs.Any();
        _ = context.Posts.Any();

        var finding = Assert.Single(scope.GetFindings());
        Assert.Equal(FindingKind.ExcessiveQueries, finding.Kind);
        Assert.Equal(4, finding.Count);
        Assert.Throws<AspGuardException>(() => scope.AssertMaxQueries(3));
        scope.AssertMaxQueries(4);

        scope.Dispose();
        Assert.Contains(db.Logs.Entries, e => e.Level == LogLevel.Warning && e.Text.Contains("Excessive queries in scope 'busy'", StringComparison.Ordinal));
    }

    [Fact]
    public void Log_mode_writes_a_structured_warning_when_the_scope_ends()
    {
        using var db = new TestDb();
        var scope = AspGuardScope.Begin("logged");
        using var context = db.NewContext();

        LoopPosts(context, 8);
        Assert.DoesNotContain(db.Logs.Entries, e => e.Text.Contains("N+1", StringComparison.Ordinal));
        scope.Dispose();

        var entry = Assert.Single(db.Logs.Entries, e => e.Text.Contains("N+1", StringComparison.Ordinal));
        Assert.Equal(LogLevel.Warning, entry.Level);
        Assert.Contains("Count=8", entry.Text);
        Assert.Contains("Scope=logged", entry.Text);
        Assert.Contains("DetectionTests.cs", entry.Text);
        Assert.Contains(nameof(LoopPosts), entry.Text);
        Assert.Single(db.Detected);
    }

    [Fact]
    public void Disabled_guard_records_nothing()
    {
        using var db = new TestDb(o => o.EnableQueryDetection = false);
        using var scope = AspGuardScope.Begin();
        using var context = db.NewContext();

        LoopPosts(context, 10);

        Assert.Equal(0, scope.QueryCount);
        Assert.Empty(scope.GetFindings());
    }

    [Fact]
    public void Queries_outside_a_scope_are_ignored()
    {
        using var db = new TestDb(o => o.Mode = AspGuardMode.Throw);
        using var context = db.NewContext();

        LoopPosts(context, 10);
    }

    [Fact]
    public void Writes_count_toward_the_total_but_are_not_N_plus_one()
    {
        using var db = new TestDb();
        using var scope = AspGuardScope.Begin();
        using var context = db.NewContext();

        for (var i = 0; i < 10; i++)
        {
            context.Blogs.Add(new Blog { Title = $"new-{i}" });
            context.SaveChanges();
        }

        Assert.True(scope.QueryCount >= 10);
        Assert.Empty(scope.GetFindings());
    }

    [Theory]
    [InlineData(CallSiteCapture.Never, false)]
    [InlineData(CallSiteCapture.OnThreshold, true)]
    [InlineData(CallSiteCapture.Always, true)]
    public void Call_site_capture_modes(CallSiteCapture mode, bool expectedCallSite)
    {
        using var db = new TestDb(o => o.CaptureCallSite = mode);
        using var scope = AspGuardScope.Begin();
        using var context = db.NewContext();

        LoopPosts(context, 6);

        var finding = Assert.Single(scope.GetFindings());
        Assert.Equal(expectedCallSite, finding.CallSite is not null);
    }

    [Fact]
    public void Repeats_from_different_call_sites_are_not_flagged_as_same_call_site()
    {
        using var db = new TestDb();
        using var scope = AspGuardScope.Begin();
        using var context = db.NewContext();

        static void FromA(ShopContext c) => _ = c.Posts.Where(p => p.BlogId == 1).ToList();
        static void FromB(ShopContext c) => _ = c.Posts.Where(p => p.BlogId == 2).ToList();

        for (var i = 0; i < 3; i++)
        {
            FromA(context);
            FromB(context);
        }

        var finding = Assert.Single(scope.GetFindings());
        Assert.Equal(6, finding.Count);
        Assert.False(finding.SameCallSite);
    }

    [Fact]
    public void Standalone_registration_without_dependency_injection_works()
    {
        using var db = new TestDb();
        using var connection = new Microsoft.Data.Sqlite.SqliteConnection("Data Source=:memory:");
        connection.Open();
        var builder = new DbContextOptionsBuilder<ShopContext>().UseSqlite(connection);
        builder.UseAspGuard(o => o.NPlusOneThreshold = 3);
        using var context = new ShopContext(builder.Options);
        context.Database.EnsureCreated();

        using var scope = AspGuardScope.Begin();
        LoopPosts(context, 3);

        Assert.Equal(3, Assert.Single(scope.GetFindings()).Count);
    }

    [Fact]
    public void Invalid_options_are_rejected()
    {
        using var services = new ServiceCollection().AddAspGuardCore(o => o.NPlusOneThreshold = 1).BuildServiceProvider();

        Assert.Throws<OptionsValidationException>(() => services.GetRequiredService<IOptions<AspGuardOptions>>().Value);
    }

    [Fact]
    public void UseAspGuard_requires_registration()
    {
        using var services = new ServiceCollection().BuildServiceProvider();

        Assert.Throws<InvalidOperationException>(() => new DbContextOptionsBuilder<ShopContext>().UseAspGuard(services));
    }
}
