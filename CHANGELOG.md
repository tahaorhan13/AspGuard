# Changelog

All notable changes to this project will be documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.1.0/),
and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [Unreleased]

## [0.2.0-preview.1]

### Added

- **.NET Framework 4.6.2+ support.** New packages:
  - `AspGuard.SystemWeb`: HTTP module for ASP.NET 4.x (MVC 5, Web API 2, WebForms) that registers itself
    (`PreApplicationStartMethod`): security headers, HSTS, CSRF protection, per-IP rate limiting, removal of
    technology headers, one query scope per request named after the route. Ships the analyzers, with install scripts
    for `packages.config` projects.
  - `AspGuard.EntityFramework6`: EF6 command interceptor that registers itself in ASP.NET 4.x applications
    (`AspGuardEntityFramework6.Register()` elsewhere). Lazy-loading N+1s are reported with the call site.
  - `AspGuard.Core` now also targets `net462` (the detection engine, without EF Core) and has `AspGuardFramework`
    for configuration without dependency injection.
- Analyzers recognize EF6 (`System.Data.Entity`): AG001, AG002, AG020, AG021, and AG010 for `Database.SqlQuery`,
  `ExecuteSqlCommand(Async)` and `DbSet.SqlQuery`, including direct interpolation and concatenation. The AG010 code fix
  turns `string.Format` into EF6 `{0}` parameters; the AG001 fix works on EF6 sets.

### Changed

- Analyzers are built against Roslyn 4.8, so they load in Visual Studio 2022 17.8+ and the .NET 8 SDK (was 17.14+).
- AG001/AG002/AG021 messages say "Database query" / "Entity Framework" instead of "EF Core".

### Changed

- **Renamed to AspGuard** (was NPlusGuard). The library now covers HTTP security and documentation as well as N+1
  detection. Packages: `AspGuard` (install this one) and `AspGuard.Core`. Analyzer IDs are now `AG*` (were `QG*`), the
  `.editorconfig` key is `aspguard.scope`, and the response headers are `X-AspGuard-Query-Count` / `X-AspGuard-Query-Findings`.
- `services.AddAspGuard()` is the whole setup: it registers the middleware through an `IStartupFilter` and watches every
  DbContext through EF Core's diagnostic events. `app.UseNPlusGuard()` and `UseNPlusGuard(sp)` on the DbContext are no
  longer needed (`UseAspGuard(sp)` still works and is not counted twice). The core registration is now `AddAspGuardCore()`.
- `Enabled` is now `EnableQueryDetection`; `AddAspGuard()` defaults it to `true` in Development only.

### Added

- Security middleware, on by default: security headers, HSTS and HTTPS redirection outside Development, cookie-aware
  CSRF protection (`Sec-Fetch-Site` / `Origin`), per-IP fixed-window rate limiting (300/min), removal of the Kestrel
  `Server` header. Configured through `AspGuardOptions.Security`.
- AG030 analyzer and code fix: generates a `/// <summary>` from the member name, in Turkish (default) or English
  (`aspguard.doc_language`), rule-based and offline.
- `AutoAttachDbContexts` option and a benchmark for the auto-attach path.

## [0.1.0-preview.1]

First preview.

### Added

- Runtime N+1 and excessive-query detection through an EF Core command interceptor, with `AsyncLocal` scopes
  (`AspGuardScope.Begin`, `Suppress`), SQL shape normalization and call-site capture (`CallSiteCapture`).
- `Log` and `Throw` modes, `OnDetected` callback, test assertions (`AssertNoNPlusOne`, `AssertMaxQueries`).
- Privacy by default: no parameter values in output; `IncludeParameterValues` requires sensitive data logging.
- `AspGuard`: request-scoped middleware and `X-AspGuard-Query-*` response headers.
- Roslyn analyzers shipped in the `AspGuard` package: AG001 (query in loop), AG002 (query in in-memory lambda),
  AG010 (dynamically built raw SQL), AG020 (materialize then filter), AG021 (blocking on EF async calls).
- Code fixes: AG001 (`Contains` + `ToLookup`) and AG010 (`string.Format` to interpolated `FromSql`), with Fix All for `dotnet format`.
- `[GuardQueries]` with `Scope = AttributedOnly` (runtime and `aspguard.scope` in `.editorconfig` for analyzers).
- Baseline file support to report only new findings.
- Sample API with a deliberate N+1 endpoint and its fix; BenchmarkDotNet suite.
