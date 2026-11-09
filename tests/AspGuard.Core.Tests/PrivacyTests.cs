using Microsoft.EntityFrameworkCore;

namespace AspGuard.Core.Tests;

public class PrivacyTests
{
    private const string ParameterSecret = "SecretValue123";
    private const string InlineSecret = "InlineSecret456";

    private static void Run(ShopContext context)
    {
        var title = ParameterSecret;
        for (var i = 1; i <= 6; i++)
        {
            var id = i;
            _ = context.Posts.Where(p => p.BlogId == id && p.Title != title && p.Title != InlineSecret).ToList();
        }
    }

    [Fact]
    public void Default_settings_never_expose_parameter_values_or_literals()
    {
        using var db = new TestDb();
        var scope = AspGuardScope.Begin();
        using var context = db.NewContext(sensitiveDataLogging: true);

        Run(context);
        var message = scope.GetFindings().Single().Message;
        scope.Dispose();

        var everything = db.Logs.AllText + message + string.Join("\n", db.Detected.Select(f => f.Message + f.Shape + f.ExampleParameters));
        Assert.Contains("N+1", db.Logs.AllText);
        Assert.DoesNotContain(ParameterSecret, everything);
        Assert.DoesNotContain(InlineSecret, everything);
    }

    [Fact]
    public void Exception_messages_do_not_expose_values()
    {
        using var db = new TestDb(o => o.Mode = AspGuardMode.Throw);
        using var scope = AspGuardScope.Begin();
        using var context = db.NewContext(sensitiveDataLogging: true);

        var exception = Assert.Throws<AspGuardException>(() => Run(context));

        Assert.DoesNotContain(ParameterSecret, exception.Message);
        Assert.DoesNotContain(InlineSecret, exception.Message);
    }

    [Fact]
    public void IncludeParameterValues_alone_is_not_enough_without_sensitive_data_logging()
    {
        using var db = new TestDb(o => o.IncludeParameterValues = true);
        var scope = AspGuardScope.Begin();
        using var context = db.NewContext(sensitiveDataLogging: false);

        Run(context);
        scope.Dispose();

        Assert.DoesNotContain(ParameterSecret, db.Logs.AllText);
        Assert.Null(db.Detected.Single().ExampleParameters);
    }

    [Fact]
    public void Parameter_values_appear_only_when_both_switches_are_on()
    {
        using var db = new TestDb(o => o.IncludeParameterValues = true);
        var scope = AspGuardScope.Begin();
        using var context = db.NewContext(sensitiveDataLogging: true);

        Run(context);
        scope.Dispose();

        Assert.Contains(ParameterSecret, db.Detected.Single().ExampleParameters);
        Assert.Contains(ParameterSecret, db.Logs.Entries.Single(e => e.Text.Contains("N+1", StringComparison.Ordinal)).Text);
        Assert.DoesNotContain(InlineSecret, db.Logs.AllText.Split('\n').Single(l => l.Contains("N+1", StringComparison.Ordinal)));
    }
}
