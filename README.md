# AspGuard

**One line in `Program.cs`, and your ASP.NET Core app gets baseline HTTP security, EF Core N+1 detection with the
offending file and line, and compile-time checks with code fixes, including generated XML documentation.**

> Status: preview, [on NuGet](https://www.nuget.org/packages/AspGuard). ASP.NET Core (.NET 8/10) **and**
> ASP.NET 4.x / EF6 on .NET Framework 4.6.2+ ([details](#net-framework-aspnet-4x-and-ef6)).

```csharp
builder.Services.AddAspGuard();
```

That is the whole setup. No `app.Use...()` call and no change to your `DbContext` registration.

| What you get | Where it runs | Details |
|---|---|---|
| **Security middleware**: security headers, HSTS, HTTPS redirection, CSRF protection, per-IP rate limiting, no `Server` header | every request | [Security](#security) |
| **N+1 and excessive-query detection** for every EF Core `DbContext`, one scope per request, with the call site | every request (Development by default) | [Query detection](#query-detection) |
| **Roslyn analyzers + code fixes**: queries in loops, raw SQL injection, blocking async calls, in-memory filtering | build / IDE | [Analyzers](#analyzers) |
| **Generated XML documentation** from method names: `AddPerson` → `/// Kişi ekler.` / `/// Adds the person.` | IDE lightbulb / `dotnet format` | [XML documentation](#xml-documentation-ag030) |

## Install

```bash
dotnet add package AspGuard --prerelease
```

`AspGuard` is the package for ASP.NET Core applications and the only one you need. `AspGuard.Core` (query detection
without ASP.NET Core, for console apps, workers and tests) comes with it automatically.

| Your application | Install |
|---|---|
| ASP.NET Core on .NET 8 / 10, EF Core | `AspGuard` |
| ASP.NET 4.x (MVC 5, Web API 2, WebForms) on .NET Framework 4.6.2+ | `AspGuard.SystemWeb`, plus `AspGuard.EntityFramework6` if it uses EF6 |
| Console app, Windows service or library with EF6 on .NET Framework | `AspGuard.EntityFramework6` |

## Configure (optional)

Every measure has a default that is safe for typical APIs and web apps, and every measure can be changed or turned off:

```csharp
builder.Services.AddAspGuard(o =>
{
    // Query detection
    o.EnableQueryDetection = builder.Environment.IsDevelopment(); // this is the default
    o.NPlusOneThreshold = 5;
    o.MaxQueriesPerScope = 30;
    o.Mode = AspGuardMode.Log;                 // or Throw (tests/CI)

    // Security
    o.Security.RateLimit.PermitLimit = 300;    // per client IP per minute
    o.Security.RateLimit.ExcludedPaths.Add("/health");
    o.Security.CrossSiteProtection.TrustedOrigins.Add("https://app.example.com");
    o.Security.Headers.ContentSecurityPolicy = "default-src 'none'; frame-ancestors 'none'"; // pure JSON API
});
```

## Security

`AddAspGuard()` puts these middleware in front of your own pipeline (through an `IStartupFilter`). They run in this order:

| Measure | Default | Notes |
|---|---|---|
| Security headers | on | `X-Content-Type-Options: nosniff`, `X-Frame-Options: DENY`, `Referrer-Policy: strict-origin-when-cross-origin`, `Permissions-Policy` (camera, microphone, geolocation, payment... off), `Cross-Origin-Opener-Policy: same-origin`, `X-Permitted-Cross-Domain-Policies: none`, `X-XSS-Protection: 0`. A header your app sets itself is kept. Set a value to `null` to drop that header. |
| `Content-Security-Policy` | off | One policy cannot fit every front end, so set `o.Security.Headers.ContentSecurityPolicy`. |
| `Server` header | removed | `KestrelServerOptions.AddServerHeader = false`. |
| HSTS | on outside Development | `max-age=31536000`. Only sent on HTTPS, never for `localhost`. `IncludeSubDomains` and `Preload` are opt-in. |
| HTTPS redirection | on outside Development | ASP.NET Core's `UseHttpsRedirection()`. It redirects only when an HTTPS port is known. |
| CSRF protection | on | A `POST`/`PUT`/`PATCH`/`DELETE` that **carries cookies** must be same-origin (`Sec-Fetch-Site: same-origin`/`none`, or an `Origin` matching `Host`) or come from a trusted origin; otherwise `403`. Requests without cookies (bearer tokens, API keys, server-to-server calls) and safe methods are never blocked. No tokens or form changes needed. |
| Rate limiting | on, 300 / min / IP | Fixed window. Rejected requests get `429` and `Retry-After`. `ExcludedPaths` are never limited. |

Disable everything at once with `o.Security.Enabled = false`, or each measure with its own `Enabled` flag.

What AspGuard deliberately does **not** do:

- **CORS:** CORS *relaxes* the browser's same-origin policy, so it is not a protection; configure it yourself only if a
  front end on another origin needs it.
- **Authentication and authorization:** these depend on your application.
- **Forwarded headers:** behind a reverse proxy, configure `UseForwardedHeaders` with your known proxies. Otherwise
  rate limiting sees the proxy's IP (every client shares one limit) and HTTPS redirection sees `http`.

## Query detection

Every HTTP request is a scope. Every `DbContext` is watched through EF Core's public diagnostic events, so no
`UseAspGuard()` is needed on the context. When the same query *shape* runs again and again, AspGuard reports it with
the call site:

```text
warn: AspGuard[1]
      N+1 suspected in scope 'GET /orders/n-plus-one': query shape 044b454cacfea837 executed 20 times
      (threshold 5, same call site: True) at samples/AspGuard.Sample.Api/OrderEndpoints.cs:13
      (AspGuard.Sample.Api.OrderEndpoints.NPlusOneAsync).
      Shape: SELECT "o"."Id", "o"."OrderId", "o"."Product" FROM "OrderItems" AS "o" WHERE "o"."OrderId" = ?.
```

That output comes from the [sample API](samples/AspGuard.Sample.Api) (`GET /orders/n-plus-one`). With
`AddResponseHeaders = true`, responses also carry `X-AspGuard-Query-Count: 21` and `X-AspGuard-Query-Findings: 1`
(`/orders/fixed` answers `1` and `0`). Lazy-loading N+1s are caught the same way.

Detection is **on in Development and off elsewhere** unless you set `EnableQueryDetection`. When it is off, EF Core is
told no one is listening, so it skips building the event, and the cost is close to zero.

### How it works

1. Each command's SQL is **normalized** (literals and parameters become `?`, whitespace is collapsed, `IN (...)` lists
   are shrunk) and hashed into a *shape*. Queries that differ only in values share a shape.
2. A scope that runs the same `SELECT` shape **`NPlusOneThreshold` times or more** (default 5) is reported as a
   suspected N+1. Total commands above `MaxQueriesPerScope` (default 30) are reported as excessive.
3. **Call site:** the first stack frame in your code. Capturing a stack trace is expensive, so by default
   (`CallSiteCapture.OnThreshold`) it is captured only for the queries just before and at the threshold. If they come
   from the same place, the finding is marked *same call site*, the strongest N+1 signal.
4. `Log` mode reports when the scope ends (with the real repeat count). `Throw` mode throws at the threshold, from
   inside your loop.

`using (AspGuardScope.Suppress()) { ... }` excludes deliberate bulk work.

### In tests

```csharp
factory.Server.PreserveExecutionContext = true;   // WebApplicationFactory: flow the test's scope into the request

using var scope = AspGuardScope.Begin();
await client.GetAsync("/orders");
scope.AssertNoNPlusOne();
scope.AssertMaxQueries(3);
```

### Without ASP.NET Core

```csharp
services.AddAspGuardCore(o => o.NPlusOneThreshold = 5);
services.AddDbContext<AppDb>((sp, options) => options.UseSqlServer(cs).UseAspGuard(sp));

using var scope = AspGuardScope.Begin("import-job");
// ... code that uses the DbContext ...
```

## .NET Framework (ASP.NET 4.x and EF6)

Legacy applications are where N+1 queries hide best: EF6 turns lazy loading on by default. AspGuard supports
**.NET Framework 4.6.2 and later**.

```powershell
Install-Package AspGuard.SystemWeb -Prerelease          # security + per-request scope + analyzers
Install-Package AspGuard.EntityFramework6 -Prerelease   # EF6 query detection
```

**There is no line to write.** Both packages register themselves before the application starts
(`PreApplicationStartMethod`): `AspGuard.SystemWeb` adds an HTTP module, and `AspGuard.EntityFramework6` adds an EF6
command interceptor. Every request becomes a scope; an N+1 is reported with its call site, lazy loading included, and the
scope is named after the route (`GET /api/orders/{id}`).

| | ASP.NET 4.x default |
|---|---|
| Query detection | on when `<compilation debug="true">` is set in web.config, off otherwise |
| Security headers, CSRF protection, rate limiting (300/min/IP) | on, same rules as ASP.NET Core |
| `Server`, `X-AspNet-Version`, `X-AspNetMvc-Version` headers | removed (integrated pipeline) |
| HSTS | on over HTTPS (or `X-Forwarded-Proto: https`) when not debugging, never for `localhost` |
| HTTPS redirection | **off**: the module cannot know the HTTPS port, and a load balancer often terminates TLS. Turn it on with `o.Security.HttpsRedirection = true` |
| Logging | `System.Diagnostics.Trace` (Visual Studio output window), or your own `ILoggerFactory` |

Change defaults in `Global.asax`:

```csharp
protected void Application_Start()
{
    AspGuardFramework.Configure(o =>
    {
        o.NPlusOneThreshold = 5;
        o.Security.RateLimit.ExcludedPaths.Add("/health");
    });
    // AspGuardFramework.LoggerFactory = myLoggerFactory;   // optional: route findings to Serilog, NLog, ...
}
```

Outside ASP.NET (console apps, services, tests), call `AspGuardEntityFramework6.Register()` once and wrap the work in a
scope:

```csharp
AspGuardEntityFramework6.Register();
using (AspGuardScope.Begin("nightly-import"))
{
    // ... code that uses your DbContext ...
}
```

The analyzers recognize EF6 (`System.Data.Entity`) too: queries in loops (AG001/AG002), raw SQL injection in
`Database.SqlQuery`, `ExecuteSqlCommand` and `DbSet.SqlQuery` (AG010; EF6 has no built-in check, so direct
interpolation and concatenation are reported as well), blocking calls (AG021) and generated documentation (AG030). They
need Visual Studio 2022 17.8 or later (or the .NET 8 SDK) to build the project; the project itself can target
.NET Framework 4.6.2 and use C# 7.3. For `packages.config` projects, the package's install script adds the analyzers.

**On .NET Framework 4.5.2 or older?** Microsoft no longer supports those versions. Moving to 4.6.2 or 4.8 is usually only
a change of the target framework in the project properties, and it is worth doing for the security updates alone.

## Analyzers

The analyzers ship inside the `AspGuard` and `AspGuard.SystemWeb` packages and need no registration. They work on
EF Core and EF6 code.

| Rule | Description | Default | Code fix |
|---|---|---|---|
| [AG001](docs/rules/AG001.md) | EF Core / EF6 query executed inside a `foreach`/`for`/`while`/`do` loop (including `Find`/`FindAsync`) | Warning | `FirstOrDefault`/`SingleOrDefault(x => x.Key == loopVar)` in a `foreach` becomes one `Contains` query + `ToLookup` |
| [AG002](docs/rules/AG002.md) | EF Core query executed inside an in-memory `Select`/`SelectMany`/`Where`/`ForEach` lambda | Warning | no |
| [AG010](docs/rules/AG010.md) | Dynamically built SQL passed to a raw SQL method: EF Core `FromSqlRaw`/`ExecuteSqlRaw`/`SqlQueryRaw` (direct interpolation is left to EF Core's EF1002), EF6 `Database.SqlQuery`/`ExecuteSqlCommand`/`DbSet.SqlQuery` | Warning | `string.Format` becomes the interpolated `FromSql`/`ExecuteSql`/`SqlQuery` (EF Core) or `{0}` placeholders with parameters (EF6) |
| [AG020](docs/rules/AG020.md) | `ToList`/`ToArray`/`AsEnumerable` of a query directly followed by `Where`/`Select` (the whole table is pulled into memory) | Warning | no |
| [AG021](docs/rules/AG021.md) | `.Result` / `.Wait()` / `.GetAwaiter().GetResult()` on an EF Core or EF6 async call | Warning | no |
| [AG030](docs/rules/AG030.md) | Method or constructor without an XML documentation comment | Info | generates `/// <summary>` from the name |

Queries are recognized **semantically** (`DbSet<T>`, EF Core extension methods), not by method name, so
LINQ-to-Objects code is not flagged. Fixes are **code fixes you apply yourself** (lightbulb or `dotnet format`);
AspGuard never rewrites code or queries at run time.

### XML documentation (AG030)

Every method and constructor without a `///` comment gets an Info diagnostic and a lightbulb fix,
**Add XML documentation comment**. The summary is generated from the name by rules: no AI model, no network, no cost,
the same result every time.

```csharp
/// <summary>
/// Kişi ekler.
/// </summary>
public void AddPerson(Person person) { ... }

/// <summary>
/// Kimliğe göre kullanıcı getirir.
/// </summary>
public Task<User?> GetUserByIdAsync(int id) { ... }
```

| Name | `tr` (default) | `en` |
|---|---|---|
| `DeleteClaim` | Tazminat siler. | Deletes the claim. |
| `GetAllUsers` | Tüm kullanıcıları getirir. | Gets all users. |
| `AddItemToCart` | Sepete öğe ekler. | Adds the item to cart. |
| `RemoveUserFromGroup` | Gruptan kullanıcı kaldırır. | Removes the user from group. |
| `GetOrderItems` | Sipariş öğelerini getirir. | Gets the order items. |
| `AddOrUpdateUser` | Kullanıcı ekler veya günceller. | Adds or updates the user. |
| `IsActive` | Aktif olup olmadığını belirtir. | Determines whether it is active. |
| `TryParseDate` | Tarih ayrıştırmayı dener. | Tries to parse the date. |
| Constructor of `Repository<T>` | `<see cref="Repository{T}"/>` sınıfının yeni bir örneğini oluşturur. | Initializes a new instance of the `<see cref="Repository{T}"/>` class. |

Choose the language in `.editorconfig`:

```ini
[*.cs]
aspguard.doc_language = tr   # or en
```

Document a whole solution at once (Info diagnostics need `--severity info`):

```bash
dotnet format analyzers --diagnostics AG030 --severity info
```

The generated text is a starting point, so read it. Words outside the built-in dictionary (about 300 common verbs,
nouns and adjectives, including insurance and e-commerce terms) are kept in English. Overrides, explicit interface
implementations, partial methods and static constructors are skipped. To turn the rule off:
`dotnet_diagnostic.AG030.severity = none`.

### Bulk fixes with `dotnet format`

```bash
dotnet format analyzers --diagnostics AG010 AG001 --severity warn --verify-no-changes   # preview
dotnet format analyzers --diagnostics AG010 AG001 --severity warn                       # apply
```

Review the diff afterwards. AG010 changes behavior on purpose (values become SQL parameters), and AG001's rewrite
assumes the rows do not change inside the loop.

## All options

| Option | Default | Meaning |
|---|---|---|
| `EnableQueryDetection` | Development only | `false`: nothing is recorded |
| `AutoAttachDbContexts` | `true` | Watch every DbContext without `UseAspGuard()`. A context that also adds the interceptor explicitly is not counted twice |
| `NPlusOneThreshold` | `5` | Repeats of one shape that count as N+1 (minimum 2) |
| `MaxQueriesPerScope` | `30` | Commands per scope before "excessive" is reported |
| `Mode` | `Log` | `Log` (ILogger warning at scope end) or `Throw` |
| `OnDetected` | `null` | Callback for every finding, in addition to `Mode` |
| `CaptureCallSite` | `OnThreshold` | `Never`, `OnThreshold`, `Always` |
| `IncludeParameterValues` | `false` | See [Privacy](#privacy) |
| `Scope` | `AllServices` | `AttributedOnly` restricts monitoring to `[GuardQueries]` code |
| `BaselineFile` / `UpdateBaseline` | `null` / `false` | Report only findings that are not in the baseline |
| `AddResponseHeaders` | `false` | `X-AspGuard-Query-Count`, `X-AspGuard-Query-Findings` |
| `Security.Enabled` | `true` | Master switch for all security middleware |
| `Security.Headers.*` | see [Security](#security) | One property per header, plus `Enabled` and `RemoveServerHeader` |
| `Security.Hsts.*` | `Enabled`, 365 days | `MaxAge`, `IncludeSubDomains`, `Preload` |
| `Security.HttpsRedirection` | `true` | Skipped in Development |
| `Security.CrossSiteProtection.*` | `Enabled` | `TrustedOrigins` |
| `Security.RateLimit.*` | `Enabled`, 300 per minute | `PermitLimit`, `Window`, `ExcludedPaths` |

### Opt-in for legacy code bases

- **Opt-in by attribute.** Set `o.Scope = GuardScopeMode.AttributedOnly` and mark the classes or methods you want
  monitored with `[GuardQueries]`. The performance analyzers follow the same rule with `aspguard.scope = attributed_only`
  in `.editorconfig` (AG010, the security rule, always runs). In this mode every query walks the stack, so use it while
  adopting the library, not as a permanent production setting.
- **Baseline.** Run once with `BaselineFile = "aspguard.baseline.json"` and `UpdateBaseline = true`, then commit the
  file. From then on only findings that are *not* in it are reported. Entries are keyed by query shape and call-site
  method (not line numbers), so they survive edits.

## Privacy

Logs, exception messages and findings contain **only the normalized SQL**, where literals and parameters are `?`.
Parameter values are never written by default. `IncludeParameterValues` (default `false`) can add an example of the
parameter values, but **only when EF Core's `EnableSensitiveDataLogging()` is also on for the DbContext**. Both
switches are required, so values cannot leak by flipping one option. Scope names use the route pattern
(`GET /orders/{id:int}`), never the query string. Security logs contain the method, path, `Origin`, `Sec-Fetch-Site`
and the client IP of rejected requests.

## Benchmarks

Cost per query, 20 queries of one shape per scope against in-memory SQLite
(`benchmarks/AspGuard.Benchmarks`; BenchmarkDotNet ShortRun job with 3 iterations: **indicative, expect a wide error
margin**):

| Configuration | Mean / query | Allocated / query | Ratio |
|---|---:|---:|---:|
| Without AspGuard | 70.9 µs | 14.6 KB | 1.00 |
| Interceptor attached, `EnableQueryDetection = false` | 76.9 µs | 15.7 KB | 1.09 |
| Interceptor, `CaptureCallSite = Never` | 74.9 µs | 15.6 KB | 1.06 |
| Interceptor, `OnThreshold` (default) | 80.4 µs | 17.6 KB | 1.13 |
| Interceptor, `Always` | 159.5 µs | 35.3 KB | 2.25 |
| **Auto-attach (`AddAspGuard()`), `OnThreshold`** | 82.4 µs | 16.8 KB | 1.16 |

.NET 10.0.14, AMD Ryzen 5 5600. The overhead is a few microseconds per query, small next to a real database round
trip. With `AddAspGuard()` and detection off (the default outside Development), EF Core is told that nobody listens, so
it does not even build the event. That case was not benchmarked separately, but by construction it costs the same as
"Without AspGuard". The "interceptor attached, disabled" row is EF Core's own cost of calling *any* command interceptor.

Run them yourself: `dotnet run -c Release --project benchmarks/AspGuard.Benchmarks`.

## Local testing

Before anything is published, try it in your own project in one of two ways.

**1. Local NuGet feed**

```bash
dotnet pack -c Release -o ./nupkg      # a warning about non-packable projects (tests, sample) is expected
cd /path/to/your-project
dotnet nuget add source /path/to/AspGuard/nupkg -n AspGuardLocal
dotnet add package AspGuard --version 0.2.0-preview.1
```

Then add `builder.Services.AddAspGuard();` to `Program.cs`. When you are done: `dotnet nuget remove source AspGuardLocal`.

**2. Project references**

```xml
<ItemGroup>
  <ProjectReference Include="..\AspGuard\src\AspGuard\AspGuard.csproj" />
  <!-- the analyzers and code fixes are separate assemblies in this mode: -->
  <ProjectReference Include="..\AspGuard\src\AspGuard.Analyzers\AspGuard.Analyzers.csproj"
                    OutputItemType="Analyzer" ReferenceOutputAssembly="false" />
  <ProjectReference Include="..\AspGuard\src\AspGuard.Analyzers.CodeFixes\AspGuard.Analyzers.CodeFixes.csproj"
                    OutputItemType="Analyzer" ReferenceOutputAssembly="false" />
</ItemGroup>
```

## Limitations (please read)

- **The N+1 detector is a heuristic.** It sees repeated query *shapes*, not intent. A legitimate batch that runs a query
  per item looks the same as an N+1; use `Suppress()` or a baseline. Alternating shapes are only caught by the
  total-count rule. Only `SELECT`/`WITH` shapes are checked for repetition.
- **No scope, no detection.** Code outside an HTTP request (a background service) is not monitored unless you
  `Begin` a scope yourself. Fire-and-forget work that outlives the request may be attributed to nothing.
- **Call sites** come from stack traces: they need PDBs, inlining can shift the line, and a lazy-loading finding points
  at the code that touched the navigation property.
- **Response headers** are added when the response starts; later queries are not counted in them.
- **Security defaults are a baseline, not a guarantee.** The middleware run before your own pipeline, so endpoint
  attributes such as `[DisableRateLimiting]` do not affect them; use `ExcludedPaths` or the options. CSRF protection
  relies on browsers sending `Sec-Fetch-Site` or `Origin` (all current browsers do). Very old browsers that send neither
  are let through. Rate limiting is per instance (in memory), not shared across servers.
- **Generated documentation is rule-based.** It reads names, not code, so it is only as good as the name. Unknown words
  stay in English, and Turkish suffixes on English words are attached with an apostrophe (`Server'e`).
- **Analyzer false negatives:** queries reached through `IQueryable<T>` parameters, `Task.Run` lambdas, local functions
  or methods called from the loop are not flagged (the runtime detector covers those).
- **Analyzer false positives:** a loop known to run once or twice is still flagged; suppress it locally.
- **.NET Framework specifics:** EF6 has no "sensitive data logging" switch, so parameter values are never included there.
  Without the integrated pipeline (classic mode) headers can only be appended and technology headers are not removed.
  `X-Powered-By` is added by IIS itself; remove it in web.config. The `packages.config` install script has not been
  tested in Visual Studio yet. The rate limiter and the query scope are per application instance.
- EF Core and EF6 only (no Dapper or raw ADO.NET). Tested with SQLite; the SQL normalizer handles SQL Server,
  PostgreSQL, MySQL and SQLite quoting and parameter syntax.

## License

MIT. See [LICENSE](LICENSE). Contributions are welcome: [CONTRIBUTING.md](CONTRIBUTING.md).
