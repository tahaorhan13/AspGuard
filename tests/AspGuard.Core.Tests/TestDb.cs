using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace AspGuard.Core.Tests;

public class Blog
{
    public int Id { get; set; }

    public string Title { get; set; } = "";

    public List<Post> Posts { get; set; } = [];
}

public class Post
{
    public int Id { get; set; }

    public int BlogId { get; set; }

    public string Title { get; set; } = "";
}

public class LazyBlog
{
    private ICollection<LazyPost>? _posts;

    public int Id { get; set; }

    public string Title { get; set; } = "";

    public ICollection<LazyPost> Posts
    {
        get => LazyLoader.Load(this, ref _posts)!;
        set => _posts = value;
    }

    private ILazyLoader LazyLoader { get; set; } = null!;

    public LazyBlog()
    {
    }

    public LazyBlog(ILazyLoader lazyLoader)
    {
        LazyLoader = lazyLoader;
    }
}

public class LazyPost
{
    public int Id { get; set; }

    public int LazyBlogId { get; set; }

    public string Title { get; set; } = "";
}

public class ShopContext(DbContextOptions<ShopContext> options) : DbContext(options)
{
    public DbSet<Blog> Blogs => Set<Blog>();

    public DbSet<Post> Posts => Set<Post>();

    public DbSet<LazyBlog> LazyBlogs => Set<LazyBlog>();

    public DbSet<LazyPost> LazyPosts => Set<LazyPost>();
}

/// <summary>Collects log entries, including their structured state, so tests can search them.</summary>
public sealed class ListLoggerProvider : ILoggerProvider
{
    private readonly List<(LogLevel Level, string Text)> _entries = [];

    public IReadOnlyList<(LogLevel Level, string Text)> Entries
    {
        get
        {
            lock (_entries)
            {
                return [.. _entries];
            }
        }
    }

    public string AllText => string.Join("\n", Entries.Select(e => e.Text));

    public ILogger CreateLogger(string categoryName) => new Logger(this, categoryName);

    public void Dispose()
    {
    }

    private sealed class Logger(ListLoggerProvider owner, string category) : ILogger
    {
        public IDisposable? BeginScope<TState>(TState state)
            where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            var text = $"[{category}] {formatter(state, exception)}";
            if (state is IEnumerable<KeyValuePair<string, object?>> pairs)
            {
                text += " | " + string.Join(", ", pairs.Select(p => $"{p.Key}={p.Value}"));
            }

            lock (owner._entries)
            {
                owner._entries.Add((logLevel, text));
            }
        }
    }
}

/// <summary>A seeded shared in-memory SQLite database plus an AspGuard-enabled DI container.</summary>
public sealed class TestDb : IDisposable
{
    private readonly SqliteConnection _keeper;
    private readonly string _connectionString;
    private readonly ServiceProvider _services;

    public TestDb(Action<AspGuardOptions>? configure = null, int blogs = 10)
    {
        _connectionString = $"Data Source={Guid.NewGuid():N};Mode=Memory;Cache=Shared";
        _keeper = new SqliteConnection(_connectionString);
        _keeper.Open();

        _services = new ServiceCollection()
            .AddLogging(b => b.AddProvider(Logs))
            .AddAspGuardCore(o =>
            {
                o.OnDetected = Detected.Add;
                configure?.Invoke(o);
            })
            .BuildServiceProvider();

        using var context = NewContext();
        context.Database.EnsureCreated();
        for (var i = 1; i <= blogs; i++)
        {
            context.Blogs.Add(new Blog { Title = $"blog-{i}", Posts = [new Post { Title = $"post-{i}-a" }, new Post { Title = $"post-{i}-b" }] });
            context.LazyBlogs.Add(new LazyBlog { Title = $"lazy-{i}", Posts = [new LazyPost { Title = $"lp-{i}-a" }, new LazyPost { Title = $"lp-{i}-b" }] });
        }

        context.SaveChanges();
    }

    public ListLoggerProvider Logs { get; } = new();

    public List<AspGuardFinding> Detected { get; } = [];

    public ShopContext NewContext(bool sensitiveDataLogging = false)
    {
        var builder = new DbContextOptionsBuilder<ShopContext>()
            .UseSqlite(_connectionString)
            .EnableServiceProviderCaching(false);
        builder.UseAspGuard(_services);
        if (sensitiveDataLogging)
        {
            builder.EnableSensitiveDataLogging();
        }

        return new ShopContext(builder.Options);
    }

    public void Dispose()
    {
        _services.Dispose();
        _keeper.Dispose();
    }
}
