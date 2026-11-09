# Changelog

All notable changes to this project will be documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.1.0/),
and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [Unreleased]

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

First preview. Not published to NuGet yet.

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
