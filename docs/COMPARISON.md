# How AspGuard compares

Two existing .NET projects overlap with AspGuard. This comparison is based on their public NuGet page / README as of
October 2026. **We did not review their source code**, so "not documented" does not mean "not supported". Corrections
are welcome.

| | [EFCore.QueryGuard](https://www.nuget.org/packages/EFCore.QueryGuard) | [Benziza/queryguard-dotnet](https://github.com/Benziza/queryguard-dotnet) | AspGuard |
|---|---|---|---|
| Version at time of writing | 1.0.1 | 0.1.0-preview.1 | 0.1.0-preview.1 |
| License | MIT | MIT | MIT |
| Focus | Runtime interceptor | Counting/limiting queries in tests, CI reports | Runtime detector **and** compile-time analyzers |
| N+1 detection | Same query repeated above a threshold (default 3) | SQL fingerprint repeated above a limit (allow-list with reasons) | Normalized query shape repeated at or above a threshold (default 5) |
| Excessive query count | Yes (default 10 per scope) | Limits via policy | Yes (default 30 per scope) |
| Slow queries | Yes | Not documented | Out of scope |
| **Call site (file:line)** | Not documented | Not documented | **Yes**: first user frame, captured only near the threshold by default, "same call site" flag |
| **Roslyn analyzers** | No | No | **Yes**: AG001, AG002, AG010, AG020, AG021, AG030, with code fixes |
| Setup | Not reviewed | Not reviewed | `services.AddAspGuard()` only: middleware via `IStartupFilter`, every DbContext via EF Core diagnostic events |
| HTTP security middleware | No | No | Security headers, HSTS, HTTPS redirection, CSRF protection, rate limiting (AspGuard's own scope beyond N+1) |
| XML documentation generation | No | No | AG030 code fix, rule-based, Turkish or English |
| **Test assertions** | No | **Yes**, several test frameworks | `AssertNoNPlusOne()`, `AssertMaxQueries(n)` on a scope (framework-agnostic exceptions) |
| **Lazy-loading N+1** | Not documented | Not documented | **Yes**, covered by tests; call site points at the code that touched the navigation |
| Scopes | Per request (middleware) | `TrackQueries` around `WebApplicationFactory` requests, services, background jobs | `AsyncLocal` scope: request, manual `Begin`, `Suppress` for deliberate bulk work |
| Opt-in / baseline | Not documented | Allow-list per fingerprint | `[GuardQueries]` + `AttributedOnly`; baseline file to report only new findings |
| ASP.NET Core | Separate package, `X-QueryGuard-Violations` header | Separate package | Same repository, `X-AspGuard-Query-Count` / `X-AspGuard-Query-Findings` headers |
| Privacy | Not documented | Parameter values and connection strings not captured, SQL redacted | Normalized SQL only; parameter values need `IncludeParameterValues` **and** sensitive data logging |
| CI integration | No | **JUnit, SARIF, Markdown, CLI, GitHub Action** | No (baseline file only); use `Mode = Throw` in tests |
| Target frameworks | net8.0 | .NET 8 and 10 | net8.0 and net10.0 |

## Where the others are ahead

- Benziza/queryguard-dotnet has far richer **CI reporting** (JUnit, SARIF, CLI, GitHub Action) and supports several
  test frameworks and database providers out of the box. AspGuard intentionally stops at exceptions/logging.
- EFCore.QueryGuard is the only one of the three that reports **slow queries**.

## Where AspGuard is different

1. **Compile-time + runtime.** The analyzers catch the loop before it runs; the runtime detector catches what static
   analysis cannot (lazy loading, queries hidden behind method calls).
2. **It tells you where.** Findings carry the file, line and method of the offending code.
3. **Assertions in tests** that read like the rest of your test, with `Throw` mode for "fail on first detection".
4. **Safe by default.** No parameter values in logs; the disabled interceptor returns on its first line.
