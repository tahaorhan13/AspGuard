using System.Collections;
using System.Collections.Specialized;
using System.Data.Common;
using System.Data.Entity;
using System.Data.Entity.Core.Common;
using System.Data.SQLite;
using System.Data.SQLite.EF6;
using System.Web;
using System.Web.Routing;
using AspGuard.EntityFramework6;
using Microsoft.Extensions.Logging;

// AspGuardFramework, EF6 interception and the scope fallback are process-wide, so these tests run one at a time.
[assembly: CollectionBehavior(DisableTestParallelization = true)]

namespace AspGuard.Framework.Tests;

public class SqliteConfiguration : DbConfiguration
{
    public SqliteConfiguration()
    {
        SetProviderFactory("System.Data.SQLite", SQLiteFactory.Instance);
        SetProviderFactory("System.Data.SQLite.EF6", SQLiteProviderFactory.Instance);
        SetProviderServices("System.Data.SQLite", (DbProviderServices)SQLiteProviderFactory.Instance.GetService(typeof(DbProviderServices)));
    }
}

public class Blog
{
    public int Id { get; set; }

    public string Title { get; set; } = "";

    public virtual ICollection<Post> Posts { get; set; } = new List<Post>();
}

public class Post
{
    public int Id { get; set; }

    public int BlogId { get; set; }

    public string Title { get; set; } = "";

    public virtual Blog? Blog { get; set; }
}

[DbConfigurationType(typeof(SqliteConfiguration))]
public class ShopContext : DbContext
{
    static ShopContext() => Database.SetInitializer<ShopContext>(null);

    public ShopContext(DbConnection connection)
        : base(connection, contextOwnsConnection: false)
    {
    }

    public DbSet<Blog> Blogs => Set<Blog>();

    public DbSet<Post> Posts => Set<Post>();

    protected override void OnModelCreating(DbModelBuilder modelBuilder)
    {
        modelBuilder.HasDefaultSchema(string.Empty);
        modelBuilder.Entity<Blog>().ToTable("Blogs");
        modelBuilder.Entity<Post>().ToTable("Posts");
    }
}

/// <summary>An in-memory SQLite database with 10 blogs and 2 posts each, plus fresh AspGuard settings.</summary>
public sealed class Ef6TestDb : IDisposable
{
    private readonly SQLiteConnection _connection;

    public Ef6TestDb(Action<AspGuardOptions>? configure = null)
    {
        AspGuardFramework.Reset();
        AspGuardScope.AmbientFallback = null;
        AspGuardFramework.LoggerFactory = Logs;
        AspGuardFramework.Configure(o =>
        {
            o.OnDetected = Detected.Add;
            configure?.Invoke(o);
        });
        AspGuardEntityFramework6.Register();

        _connection = new SQLiteConnection("Data Source=:memory:");
        _connection.Open();
        using var command = _connection.CreateCommand();
        command.CommandText = """
            CREATE TABLE Blogs (Id INTEGER PRIMARY KEY AUTOINCREMENT, Title TEXT NOT NULL);
            CREATE TABLE Posts (Id INTEGER PRIMARY KEY AUTOINCREMENT, BlogId INTEGER NOT NULL, Title TEXT NOT NULL);
            """;
        command.ExecuteNonQuery();
        for (var i = 1; i <= 10; i++)
        {
            command.CommandText = $"INSERT INTO Blogs (Title) VALUES ('blog-{i}'); INSERT INTO Posts (BlogId, Title) VALUES ({i}, 'post-{i}-a'), ({i}, 'post-{i}-b');";
            command.ExecuteNonQuery();
        }
    }

    public List<AspGuardFinding> Detected { get; } = [];

    public ListLoggerFactory Logs { get; } = new();

    public ShopContext NewContext() => new(_connection);

    public void Dispose()
    {
        AspGuardEntityFramework6.Unregister();
        AspGuardScope.AmbientFallback = null;
        AspGuardFramework.Reset();
        _connection.Dispose();
    }
}

public sealed class ListLoggerFactory : ILoggerFactory
{
    public List<string> Messages { get; } = [];

    public ILogger CreateLogger(string categoryName) => new ListLogger(Messages);

    public void AddProvider(ILoggerProvider provider)
    {
    }

    public void Dispose()
    {
    }

    private sealed class ListLogger(List<string> messages) : ILogger, IDisposable
    {
        public IDisposable BeginScope<TState>(TState state)
            where TState : notnull => this;

        public void Dispose()
        {
        }

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            lock (messages)
            {
                messages.Add(formatter(state, exception));
            }
        }
    }
}

public sealed class FakeHttpContext : HttpContextBase
{
    private readonly Hashtable _items = new();

    public FakeHttpContext(string method = "GET", string url = "http://example.com/", NameValueCollection? headers = null, string clientIp = "10.0.0.1")
    {
        FakeRequest = new FakeRequest(method, new Uri(url), headers ?? new NameValueCollection(), clientIp);
    }

    public FakeRequest FakeRequest { get; }

    public FakeResponse FakeResponse { get; } = new();

    public override HttpRequestBase Request => FakeRequest;

    public override HttpResponseBase Response => FakeResponse;

    public override IDictionary Items => _items;
}

public sealed class FakeRequest(string method, Uri url, NameValueCollection headers, string clientIp) : HttpRequestBase
{
    public override string HttpMethod => method;

    public override Uri Url => url;

    public override string Path => url.AbsolutePath;

    public override string RawUrl => url.PathAndQuery;

    public override NameValueCollection Headers => headers;

    public override string UserHostAddress => clientIp;

    public override bool IsSecureConnection => url.Scheme == Uri.UriSchemeHttps;

    public RouteBase? Route { get; set; }

    public override RequestContext RequestContext
    {
        get
        {
            var data = new RouteData { Route = Route };
            return new RequestContext(new FakeHttpContext(), data);
        }
    }
}

public sealed class FakeResponse : HttpResponseBase
{
    public override int StatusCode { get; set; } = 200;

    public override string ContentType { get; set; } = "text/html";

    public override NameValueCollection Headers { get; } = new();

    public NameValueCollection Appended { get; } = new();

    public System.Text.StringBuilder Body { get; } = new();

    public override void Write(string s) => Body.Append(s);

    public override void AppendHeader(string name, string value) => Appended.Add(name, value);
}
