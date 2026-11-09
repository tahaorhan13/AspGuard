using System.Data.Entity;
using AspGuard.EntityFramework6;

namespace AspGuard.Framework.Tests;

public class Ef6DetectionTests
{
    [Fact]
    public void Query_in_a_loop_is_reported_with_the_call_site()
    {
        using var db = new Ef6TestDb();
        using (AspGuardScope.Begin("loop"))
        {
            using var context = db.NewContext();
            foreach (var blog in context.Blogs.ToList())
            {
                _ = context.Posts.Where(p => p.BlogId == blog.Id).ToList();
            }
        }

        var finding = Assert.Single(db.Detected);
        Assert.Equal(FindingKind.NPlusOne, finding.Kind);
        Assert.Equal(10, finding.Count);
        Assert.True(finding.SameCallSite);
        Assert.Contains(nameof(Query_in_a_loop_is_reported_with_the_call_site), finding.CallSite);
        Assert.Contains("Ef6DetectionTests.cs", finding.CallSite);
    }

    [Fact]
    public async Task Async_queries_in_a_loop_are_reported()
    {
        using var db = new Ef6TestDb();
        using (AspGuardScope.Begin("async"))
        {
            using var context = db.NewContext();
            foreach (var blog in await context.Blogs.ToListAsync())
            {
                _ = await context.Posts.Where(p => p.BlogId == blog.Id).ToListAsync();
            }
        }

        Assert.Equal(10, Assert.Single(db.Detected).Count);
    }

    [Fact]
    public void Include_runs_one_query_and_reports_nothing()
    {
        using var db = new Ef6TestDb();
        using (var scope = AspGuardScope.Begin("include"))
        {
            using var context = db.NewContext();
            var blogs = context.Blogs.Include(b => b.Posts).ToList();
            Assert.Equal(20, blogs.Sum(b => b.Posts.Count));
            Assert.Equal(1, scope.QueryCount);
        }

        Assert.Empty(db.Detected);
    }

    [Fact]
    public void Lazy_loading_n_plus_one_points_at_the_code_that_touched_the_navigation()
    {
        using var db = new Ef6TestDb();
        using (AspGuardScope.Begin("lazy"))
        {
            using var context = db.NewContext();
            var total = 0;
            foreach (var blog in context.Blogs.ToList())
            {
                total += blog.Posts.Count;
            }

            Assert.Equal(20, total);
        }

        var finding = Assert.Single(db.Detected);
        Assert.Equal(10, finding.Count);
        Assert.Contains(nameof(Lazy_loading_n_plus_one_points_at_the_code_that_touched_the_navigation), finding.CallSite);
    }

    [Fact]
    public void Below_the_threshold_nothing_is_reported()
    {
        using var db = new Ef6TestDb();
        using (AspGuardScope.Begin("few"))
        {
            using var context = db.NewContext();
            foreach (var id in new[] { 1, 2, 3, 4 })
            {
                _ = context.Posts.Where(p => p.BlogId == id).ToList();
            }
        }

        Assert.Empty(db.Detected);
    }

    [Fact]
    public void Suppressed_queries_are_not_counted()
    {
        using var db = new Ef6TestDb();
        using (var scope = AspGuardScope.Begin("bulk"))
        {
            using var context = db.NewContext();
            using (AspGuardScope.Suppress())
            {
                for (var id = 1; id <= 10; id++)
                {
                    _ = context.Posts.Where(p => p.BlogId == id).ToList();
                }
            }

            Assert.Equal(0, scope.QueryCount);
        }

        Assert.Empty(db.Detected);
    }

    [Fact]
    public void Throw_mode_throws_from_inside_the_loop()
    {
        using var db = new Ef6TestDb(o => o.Mode = AspGuardMode.Throw);
        using var scope = AspGuardScope.Begin("throw");
        using var context = db.NewContext();

        var exception = Assert.ThrowsAny<Exception>(() =>
        {
            for (var id = 1; id <= 10; id++)
            {
                _ = context.Posts.Where(p => p.BlogId == id).ToList();
            }
        });

        // EF6 may wrap interceptor exceptions; the AspGuard exception is the cause either way.
        Assert.True(exception is AspGuardException || exception.GetBaseException() is AspGuardException, exception.ToString());
    }

    [Fact]
    public void Without_a_scope_nothing_is_recorded()
    {
        using var db = new Ef6TestDb();
        using var context = db.NewContext();
        for (var id = 1; id <= 10; id++)
        {
            _ = context.Posts.Where(p => p.BlogId == id).ToList();
        }

        Assert.Empty(db.Detected);
    }

    [Fact]
    public void Disabled_detection_records_nothing()
    {
        using var db = new Ef6TestDb(o => o.EnableQueryDetection = false);
        using (var scope = AspGuardScope.Begin("off"))
        {
            using var context = db.NewContext();
            for (var id = 1; id <= 10; id++)
            {
                _ = context.Posts.Where(p => p.BlogId == id).ToList();
            }

            Assert.Equal(0, scope.QueryCount);
        }

        Assert.Empty(db.Detected);
    }

    [Fact]
    public void Parameter_values_are_never_written_even_when_requested()
    {
        using var db = new Ef6TestDb(o => o.IncludeParameterValues = true);
        using (AspGuardScope.Begin("privacy"))
        {
            using var context = db.NewContext();
            for (var i = 0; i < 6; i++)
            {
                var secret = "secret-value-" + i;
                _ = context.Posts.Where(p => p.Title == secret).ToList();
            }
        }

        var finding = Assert.Single(db.Detected);
        Assert.Null(finding.ExampleParameters);
        Assert.DoesNotContain("secret-value", finding.Shape);
        Assert.DoesNotContain(db.Logs.Messages, m => m.Contains("secret-value"));
        Assert.Contains(db.Logs.Messages, m => m.Contains("N+1 suspected"));
    }

    [Fact]
    public void Register_is_idempotent()
    {
        using var db = new Ef6TestDb();
        AspGuardEntityFramework6.Register();
        AspGuardEntityFramework6.Register();

        using (var scope = AspGuardScope.Begin("once"))
        {
            using var context = db.NewContext();
            _ = context.Blogs.ToList();
            Assert.Equal(1, scope.QueryCount);
        }
    }
}
