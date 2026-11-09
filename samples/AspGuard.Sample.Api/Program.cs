using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using AspGuard.Sample.Api;

var builder = WebApplication.CreateBuilder(args);

// The only AspGuard line: security middleware + N+1 detection for every DbContext (detection is on in Development).
builder.Services.AddAspGuard(o => o.AddResponseHeaders = true);

// In-memory SQLite shared by all requests (the keeper connection keeps the database alive).
var connectionString = $"Data Source=sample-{Guid.NewGuid():N};Mode=Memory;Cache=Shared";
var keeper = new SqliteConnection(connectionString);
keeper.Open();
builder.Services.AddSingleton(keeper);

// No AspGuard call needed here: every DbContext is watched automatically.
builder.Services.AddDbContext<ShopDb>(options => options.UseSqlite(connectionString));

var app = builder.Build();

using (var scope = app.Services.CreateScope())
{
    SeedData.Create(scope.ServiceProvider.GetRequiredService<ShopDb>());
}

app.MapGet("/orders/n-plus-one", OrderEndpoints.NPlusOneAsync);
app.MapGet("/orders/fixed", OrderEndpoints.FixedAsync);
app.MapPost("/orders/{id:int}/note", (int id) => Results.Ok(new { id, saved = true }));

app.Run();

/// <summary>Entry point marker so tests can use <c>WebApplicationFactory&lt;Program&gt;</c>.</summary>
public partial class Program;
