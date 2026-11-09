# Decisions

Short log of decisions taken without asking the maintainer, newest last. Each entry: decision, then why.

## Phase 0

- **Name: NPlusGuard.** `QueryGuard` collided with `EFCore.QueryGuard` (NuGet) and `Benziza/queryguard-dotnet` (GitHub).
- **Roslyn 4.14.0 for the analyzer.** Older than the newest 5.x on purpose, so the analyzer loads in older IDEs/SDKs.
- **xunit v2 (2.9.3).** Roslyn `Analyzer.Testing` targets the v2 runner.
- **Transitive pins** (`CentralPackageTransitivePinningEnabled`) for `Microsoft.CodeAnalysis.*` 4.14.0 and `System.Security.Cryptography.Pkcs` 10.0.13: `Analyzer.Testing` otherwise restores Roslyn 1.0.1 (net461-only) and a vulnerable Pkcs 9.0.6; warnings are errors here.
- **Static entry points live on `NPlusGuardScope`** (`Begin`, `Suppress`) because a type named like the namespace causes CS0118.

## Phase 1

- **Findings are final at scope end in `Log` mode; `Throw` mode throws at the query that crosses the threshold.** Logging at the end reports the true repeat count (not just "5"), while throwing at the crossing points the stack at the offending loop.
- **Only `SELECT`/`WITH` shapes can be N+1; every command counts toward `MaxQueriesPerScope`.** SQLite runs `INSERT ... RETURNING` through `ExecuteReader`, and per-row inserts are not the read problem this library targets.
- **Call-site capture (`OnThreshold`).** A stack trace costs microseconds and allocates. We capture only for the queries numbered `threshold-1` and `threshold` of a shape (and the one crossing `MaxQueriesPerScope`), which is enough to compare two call sites (strong "same call site" signal) and to name the offender, while the first `threshold-2` queries stay cheap. `Always` is available for exhaustive debugging; `Never` for the cheapest mode. Enabling is gated by `Enabled` (the sample enables it only in Development).
- **Property getters are skipped when picking the call site** (falling back to them if nothing else is found), so lazy-loading N+1 points at the code that touched the navigation, not at the entity getter.
- **Frames skipped:** `System.*`, `Microsoft.*`, `Castle.*`/`DynamicProxyGenAssembly2` (lazy-loading proxies), `NPlusGuard` and `NPlusGuard.AspNetCore` (exact names, so test assemblies such as `NPlusGuard.Tests` are kept).
- **Shape cache.** Normalized shapes are cached by raw command text (max 4096 entries, cleared when full); EF emits identical text for identical LINQ.
- **A scope binds to the runtime (options) of the first interceptor that records into it.** Static `NPlusGuardScope.Begin` has no options; this keeps the API at "3 lines".
- **`AddNPlusGuard` and `UseNPlusGuard(DbContextOptionsBuilder)` live in the core package**, so non-ASP.NET hosts can use them. `AspNetCore` adds the middleware. `AddResponseHeaders` is a core option for the same reason.
- **Standalone `UseNPlusGuard(Action<>)` overload** exists for tests/console apps; EF caches an internal service provider per interceptor instance, so tests set `EnableServiceProviderCaching(false)` to avoid `ManyServiceProvidersCreatedWarning` (which throws by default).
- **Lazy-loading test uses `ILazyLoader` injection**, not `Microsoft.EntityFrameworkCore.Proxies`: no extra dependency.
- **Privacy:** reports contain only normalized SQL (`?` for literals and parameters). `IncludeParameterValues` requires `EnableSensitiveDataLogging` as well; the first execution's values per shape are kept as the example.
- **Nested scopes** do not propagate findings to the parent; disposing restores the parent.

## Phase 2

- **Scope name = `METHOD route-pattern`** when routing already ran, otherwise the path; the query string is never included (privacy, low cardinality).
- **Response headers** (`X-NPlusGuard-Count`, `X-NPlusGuard-Findings`) are added in `OnStarting`, so queries issued after the response started are not counted. Off by default (`AddResponseHeaders`).
- **Integration tests use the sample API** through `WebApplicationFactory<Program>`; the sample uses a shared-cache named in-memory SQLite database so concurrent requests get their own connections.
- **Throw mode test accepts either a 500 or the exception** because `TestServer` rethrows unhandled exceptions to the caller.

## Phase 3

- **Analyzers ship inside the `NPlusGuard` package** (`analyzers/dotnet/cs`), not as a separate package: one install enables runtime + compile-time checks, and two packages shipping the same analyzer would double-report. `NPlusGuard.Analyzers` has `IsPackable=false`.
- **Query recognition is semantic.** EF Core's own `*Async`/`Load`/`Execute*` extensions are always queries; sync `Queryable`/`Enumerable` calls count only if the receiver traces back (through deferred LINQ operators and local initializers, max depth 6) to a `DbSet<T>`. A materialized result (`ToList()` etc.) ends the trace, so `list.Where(...).ToList()` in a loop is in-memory and not reported. Parameters of type `IQueryable<T>` are not traced (false negative, chosen over false positives).
- **QG001 loop rules:** only the loop body counts (plus a `for` condition and increment); the `foreach` collection expression and `for` initializer run once. The walk stops at local functions and at lambdas, so a loop around `Task.Run(() => query)` is a known false negative.
- **QG002 hosts:** `Enumerable.Select/SelectMany/Where`, `List<T>.ForEach`, `Array.ForEach`.
- **Release-tracking files** (`AnalyzerReleases.*.md`) and `EnforceExtendedAnalyzerRules` are on because `Microsoft.CodeAnalysis.Analyzers` enforces them (warnings are errors).
- **`HelpLinkUri` base** is `https://github.com/NPlusGuard/NPlusGuard/blob/main/docs/rules/` as a placeholder until the real repository exists; one constant in `Rules.cs`.
- **Added `Microsoft.CodeAnalysis.CSharp.CodeFix.Testing` 1.1.4** (needed for code fix tests in Phase 6). Verifier is `DefaultVerifier`, so no xunit adapter package is needed.

## Phase 4

- **Benchmarks use BenchmarkDotNet `ShortRun`** (3 iterations) to keep the session short; the README says the margin is wide. `--job default` gives tighter numbers. They run against in-memory SQLite, so absolute figures are indicative; the *delta* per query is what matters.
- **README states that the "disabled" overhead is EF Core's interceptor dispatch** (~4 µs, ~1 KB per query here), not NPlusGuard logic. Users who need true zero can attach the interceptor conditionally.
- **Version `0.1.0-preview.1`** from the root `Directory.Build.props`. Package metadata, SourceLink (built into the SDK), embedded untracked sources and `snupkg` symbols are in `src/Directory.Build.props`. Owner in URLs is a placeholder (`NPlusGuard/NPlusGuard`) to replace before publishing. The icon is picked up automatically from `assets/icon.png` if present (none was created).
- **Middleware reuses an ambient scope.** Without this the CLAUDE.md test example (`Begin()` -> `client.GetAsync` -> `AssertNoNPlusOne()`) would see nothing, because the request got its own scope. It also needs `TestServer.PreserveExecutionContext = true`, which the README documents (covered by a test). `NPlusGuard` grants `InternalsVisibleTo` to `NPlusGuard.AspNetCore` for this instead of widening the public API.
- **Release workflow packs the two packable projects explicitly** (packing the whole solution warns about non-packable projects). Workflows were written, not run.
- **Local feed flow was verified** with a consumer project outside the repo: QG001 fires from the package's `analyzers/dotnet/cs` and the runtime loads.
- **`COMPARISON.md`** is based on public pages only and says so.

## Phase 5

- **`AttributedOnly` runtime check is stack-based**: each query captures a stack and looks for `[GuardQueries]` on the method or any enclosing type (compiler-generated async/lambda frames are mapped back to the user method). It is therefore noticeably more expensive than `AllServices` and documented as an adoption aid.
- **Analyzer scope filter applies to the performance rules** (QG001, QG002, and later QG020/QG021) via `nplusguard.scope = attributed_only`. The security rule QG010 is **not** filtered: a SQL injection risk should not be hidden by an opt-in setting.
- **Baseline keys** are `Kind|ShapeId or ScopeName|CallSiteMethod` (no line numbers) so they survive edits. `UpdateBaseline = true` writes findings and suppresses reporting; it requires `BaselineFile` (validated).
- **Baselined findings are excluded from `GetFindings()`** and therefore from assertions.

## Phase 6

- **Code fixes live in `NPlusGuard.Analyzers.CodeFixes`**, a separate assembly. `Microsoft.CodeAnalysis.Analyzers` (RS1038) forbids analyzers in an assembly that references Roslyn Workspaces. Both DLLs are packed into `analyzers/dotnet/cs` of the `NPlusGuard` package. This deviates from the folder layout in CLAUDE.md (updated).
- **QG010 scope was measured, not guessed.** A scratch project with the EF Core package showed EF1002 reports only *direct* interpolation/concatenation in the argument; it does not report `string.Format`, `StringBuilder`, locals built with interpolation/`+`/`+=`. QG010 reports exactly those, so the two rules do not overlap. Unknown strings (method parameters, fields) are not reported.
- **QG010 is not subject to `nplusguard.scope = attributed_only`** (security rule); QG001/QG002/QG020/QG021 are.
- **QG010 code fix is conservative:** only `string.Format` with a constant format string and plain `{n}` placeholders, no placeholder inside a quoted literal (it would turn into a quoted parameter), no format specifiers or `{{`, no extra parameter arguments, duplicate placeholders only for simple values. Direct interpolation/concatenation have EF Core's own fix.
- **QG001 code fix uses `ToLookup` instead of a `Dictionary`.** `ToDictionary` throws on duplicate keys and `Dictionary[key]` throws on missing keys; `ILookup` never does and `lookup[key].FirstOrDefault()/SingleOrDefault()` keeps the original semantics. Only `FirstOrDefault`/`SingleOrDefault` (and `Async`) with a single `x.Prop == loopVar` predicate, a plain `foreach` collection, a plain `DbSet` receiver and identical property/loop-variable types are rewritten. `Find` is never rewritten (the key property cannot be known from syntax). Fix All uses the batch fixer; two identical rewrites in one loop may need a second pass.
- **QG020 looks only at the direct chain** and skips queries bounded by `Take`/`TakeLast`. **QG021 looks only at calls awaited directly** on the EF call (`.Result` on `Task<T>`/`ValueTask<T>`, `Wait()`, `GetAwaiter().GetResult()`).
- **`dotnet format analyzers` was verified end to end** on a consumer project installed from the local feed (QG010 and QG001 fixed in one run).

## Phase 7 (AspGuard expansion)

Asked of the maintainer up front: rule-based comment generation (no local AI), Turkish by default with English via
`.editorconfig`, a new umbrella name, and "safe defaults" for security.

- **Name: AspGuard.** No package on NuGet and no repository named `aspguard` on GitHub (checked November 2026).
  `NetWarden` was set aside because `Warden`/`Warden.NET` exist on NuGet. Earlier entries in this file use the old names.
- **Packages: `AspGuard` (web, install this) and `AspGuard.Core` (no ASP.NET Core).** The analyzers moved into the
  `AspGuard` package: a dependency's analyzers do not flow to consumers by default, and shipping them in both would
  double-report. The namespace is `AspGuard` in both assemblies. Rule IDs became `AG*`.
- **One line via `IStartupFilter`.** The filter puts the middleware in front of the app's pipeline, so no `app.Use...` is
  needed. Consequence: the middleware run before routing. The query scope is renamed to the route pattern once the
  request finishes (`GET /orders/{id:int}`); rate limiting and CSRF cannot read endpoint metadata, hence `ExcludedPaths`.
- **Every DbContext without configuration, through EF Core's public `DiagnosticListener`** (`RelationalEventId.CommandExecuting`).
  `IsEnabled` returns false unless an attached runtime has detection on, so EF does not build the payload when it is off.
  Contexts that also add the interceptor are detected through `CoreOptionsExtension.Interceptors` (public API, cached per
  options instance) and skipped. Several hosts per process (tests) are supported: the observer keeps a list of runtimes;
  the request middleware binds its scope to its own runtime; a runtime detaches when its container disposes it.
  `ConfigureDbContext<T>` was not an option: it needs the context type and does not exist in EF Core 8.
- **`Enabled` → `EnableQueryDetection`.** One options type now also holds `Security`, so a bare `Enabled` would be
  ambiguous. `AddAspGuard()` sets it to `IsDevelopment()` before the caller's configuration, so an explicit value wins.
- **Security defaults, chosen not to break apps:** headers only when the app did not set them; no CSP by default (it
  breaks front ends); HSTS without `includeSubDomains`/`preload`, never for localhost or in Development; built-in HTTPS
  redirection outside Development; rate limit 300/min per IP, own small middleware over `PartitionedRateLimiter` (not
  `AddRateLimiter`, which would clash with an app's own global limiter); no CORS (it relaxes security).
- **CSRF without tokens:** state-changing requests that carry a `Cookie` header must be same-origin by `Sec-Fetch-Site`
  (`same-origin`/`none`) or, for older browsers, by `Origin` matching `Host`; `TrustedOrigins` allow exceptions. No
  `Cookie` means no ambient credentials, so bearer-token APIs and server-to-server calls are never blocked. Antiforgery
  services were not registered: in .NET 8 that turns on token validation for form endpoints and could break them.
- **AG030 is rule-based**, as the maintainer chose. The summary is generated in the analyzer and passed in the diagnostic
  properties, so the code fix only inserts it and `dotnet format` works. Info severity: visible in the IDE, not noisy in
  builds. Turkish follows the maintainer's bare style ("Kişi ekler."), uses the accusative for plurals, compounds noun
  groups and turns prepositions into case suffixes with vowel harmony. Unknown English words keep their spelling and
  take suffixes after an apostrophe.
- **Workaround:** `Microsoft.AspNetCore.Analyzers.RouteHandlerAnalyzer` crashes (AD0001) on `MapGet(pattern, OtherAssemblyType.Method)`
  in the test project; the test wraps the method group in a lambda.
- Removed the Phase 0 placeholder `AssemblyMarker` types and smoke tests.
