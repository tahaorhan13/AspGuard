using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace AspGuard.Core.Tests;

[GuardQueries]
internal static class AttributedRepository
{
    public static void Run(ShopContext context, int times)
    {
        for (var i = 1; i <= times; i++)
        {
            var id = i;
            _ = context.Posts.Where(p => p.BlogId == id).ToList();
        }
    }

    public static async Task RunAsync(ShopContext context, int times)
    {
        for (var i = 1; i <= times; i++)
        {
            var id = i;
            _ = await context.Posts.Where(p => p.BlogId == id).ToListAsync();
        }
    }
}

internal static class PlainRepository
{
    public static void Run(ShopContext context, int times)
    {
        for (var i = 1; i <= times; i++)
        {
            var id = i;
            _ = context.Posts.Where(p => p.BlogId == id).ToList();
        }
    }
}

internal static class MixedRepository
{
    [GuardQueries]
    public static void Marked(ShopContext context, int times) => PlainRepository.Run(context, times);

    public static void Unmarked(ShopContext context, int times) => PlainRepository.Run(context, times);

    [GuardQueries]
    public static async Task MarkedAsync(ShopContext context, int times)
    {
        await Task.Yield();
        for (var i = 1; i <= times; i++)
        {
            var id = i;
            _ = await context.Posts.Where(p => p.BlogId == id).ToListAsync();
        }
    }
}

public class AttributedOnlyTests
{
    [Fact]
    public void Unmarked_code_is_not_monitored()
    {
        using var db = new TestDb(o => o.Scope = GuardScopeMode.AttributedOnly);
        using var scope = AspGuardScope.Begin();
        using var context = db.NewContext();

        PlainRepository.Run(context, 10);

        Assert.Equal(0, scope.QueryCount);
        Assert.Empty(scope.GetFindings());
    }

    [Fact]
    public void Class_level_attribute_enables_monitoring()
    {
        using var db = new TestDb(o => o.Scope = GuardScopeMode.AttributedOnly);
        using var scope = AspGuardScope.Begin();
        using var context = db.NewContext();

        AttributedRepository.Run(context, 10);

        Assert.Equal(10, Assert.Single(scope.GetFindings()).Count);
    }

    [Fact]
    public async Task Class_level_attribute_works_for_async_methods()
    {
        using var db = new TestDb(o => o.Scope = GuardScopeMode.AttributedOnly);
        using var scope = AspGuardScope.Begin();
        await using var context = db.NewContext();

        await AttributedRepository.RunAsync(context, 10);

        Assert.Equal(10, Assert.Single(scope.GetFindings()).Count);
    }

    [Fact]
    public void Method_level_attribute_covers_only_that_method_and_its_callees()
    {
        using var db = new TestDb(o => o.Scope = GuardScopeMode.AttributedOnly);
        using var scope = AspGuardScope.Begin();
        using var context = db.NewContext();

        MixedRepository.Unmarked(context, 10);
        Assert.Equal(0, scope.QueryCount);

        MixedRepository.Marked(context, 10);
        Assert.Equal(10, scope.QueryCount);
    }

    [Fact]
    public async Task Method_level_attribute_works_across_awaits()
    {
        using var db = new TestDb(o => o.Scope = GuardScopeMode.AttributedOnly);
        using var scope = AspGuardScope.Begin();
        await using var context = db.NewContext();

        await MixedRepository.MarkedAsync(context, 10);

        Assert.Equal(10, Assert.Single(scope.GetFindings()).Count);
    }

    [Fact]
    public void AllServices_is_the_default()
    {
        Assert.Equal(GuardScopeMode.AllServices, new AspGuardOptions().Scope);
    }
}

public sealed class BaselineTests : IDisposable
{
    private readonly string _file = Path.Combine(Path.GetTempPath(), $"aspguard-baseline-{Guid.NewGuid():N}.json");

    public void Dispose()
    {
        if (File.Exists(_file))
        {
            File.Delete(_file);
        }
    }

    private static void LoopPosts(ShopContext context, int times)
    {
        for (var i = 1; i <= times; i++)
        {
            var id = i;
            _ = context.Posts.Where(p => p.BlogId == id).ToList();
        }
    }

    private static void LoopBlogs(ShopContext context, int times)
    {
        for (var i = 1; i <= times; i++)
        {
            var id = i;
            _ = context.Blogs.Where(b => b.Id == id).ToList();
        }
    }

    private void RecordBaseline()
    {
        using var db = new TestDb(o =>
        {
            o.BaselineFile = _file;
            o.UpdateBaseline = true;
        });
        var scope = AspGuardScope.Begin();
        using var context = db.NewContext();

        LoopPosts(context, 6);
        scope.Dispose();

        Assert.Empty(db.Detected);
        Assert.DoesNotContain("N+1", db.Logs.AllText, StringComparison.Ordinal);
    }

    [Fact]
    public void Update_mode_writes_findings_instead_of_reporting_them()
    {
        RecordBaseline();

        var json = File.ReadAllText(_file);
        Assert.Contains("NPlusOne", json, StringComparison.Ordinal);
        Assert.Contains("LoopPosts", json, StringComparison.Ordinal);
        Assert.DoesNotContain("blog-", json, StringComparison.Ordinal);
    }

    [Fact]
    public void Known_findings_are_skipped_and_new_ones_are_reported()
    {
        RecordBaseline();

        using var db = new TestDb(o => o.BaselineFile = _file);
        var scope = AspGuardScope.Begin();
        using var context = db.NewContext();

        LoopPosts(context, 6);
        Assert.Empty(scope.GetFindings());

        LoopBlogs(context, 6);
        var finding = Assert.Single(scope.GetFindings());
        Assert.Contains("Blogs", finding.Shape, StringComparison.Ordinal);

        scope.Dispose();
        Assert.Equal(finding.ShapeId, Assert.Single(db.Detected).ShapeId);
    }

    [Fact]
    public void Known_findings_do_not_throw_in_throw_mode()
    {
        RecordBaseline();

        using var db = new TestDb(o =>
        {
            o.BaselineFile = _file;
            o.Mode = AspGuardMode.Throw;
        });
        using var scope = AspGuardScope.Begin();
        using var context = db.NewContext();

        LoopPosts(context, 6);
        Assert.Throws<AspGuardException>(() => LoopBlogs(context, 6));
    }

    [Fact]
    public void Update_baseline_requires_a_file()
    {
        using var services = new ServiceCollection().AddAspGuardCore(o => o.UpdateBaseline = true).BuildServiceProvider();

        Assert.Throws<OptionsValidationException>(() => services.GetRequiredService<IOptions<AspGuardOptions>>().Value);
    }
}
