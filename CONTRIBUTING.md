# Contributing

Thanks for helping! AspGuard is a small library with a high bar for correctness: a false positive in the analyzers
or a noisy runtime report costs users trust.

## Getting started

```bash
git clone <your fork>
cd AspGuard
dotnet build      # must finish with 0 warnings: warnings are errors
dotnet test
```

You need the .NET SDK pinned in `global.json` (10.0.x). The runtime libraries target `net8.0;net10.0`, so the .NET 8
runtime must be installed to run the tests for both targets. Tests use SQLite in memory: no Docker or database server.

## Repository layout

| Path | Contents |
|---|---|
| `src/AspGuard.Core` | Query detection without ASP.NET Core: interceptor, EF diagnostic observer, scopes, SQL normalization, reporting, options |
| `src/AspGuard` | The package users install: `AddAspGuard()`, startup filter, security middleware, request scope; packs the analyzers |
| `src/AspGuard.Analyzers` | Roslyn analyzers (`netstandard2.0`), including the rule-based XML documentation generator (`DocumentationText`, `TurkishText`); shipped inside the `AspGuard` package |
| `src/AspGuard.Analyzers.CodeFixes` | Code fix providers (`netstandard2.0`), a separate assembly because analyzers must not reference Roslyn Workspaces |
| `tests/` | One test project per source project |
| `samples/AspGuard.Sample.Api` | Minimal API with a deliberate N+1 endpoint and its fix |
| `benchmarks/` | BenchmarkDotNet suite |
| `docs/` | Rule documentation (`docs/rules`) and the competitor comparison |

## Rules of the road

- **Every behavior change comes with a test.** For analyzers, write false-positive tests at least as thoroughly as
  true-positive tests: LINQ-to-Objects must never be flagged.
- **Warnings are errors.** Do not suppress with `#pragma`; fix the cause.
- **Keep the public API minimal.** Anything that does not have to be public is `internal`; every public member has XML docs.
- **No EF Core internal APIs and no reflection tricks.**
- **The disabled path must stay free.** The interceptor checks `Enabled` before doing anything else.
- **Privacy:** reports must never contain parameter values or SQL literals unless `IncludeParameterValues` and
  sensitive data logging are both on. Add a test for any new place that prints SQL.
- Code, comments, XML docs and commit messages are in English.

## Adding an analyzer rule

1. Add the descriptor to `Rules.cs` (`helpLinkUri` points to `docs/rules/QGxxx.md`) and an entry to
   `AnalyzerReleases.Unshipped.md`.
2. Write `docs/rules/QGxxx.md` (cause, example, fix, when to suppress, known limitations).
3. Add tests in `tests/AspGuard.Analyzers.Tests` (true and false positives; code fix tests if there is a fix).
4. Verify types semantically (`ITypeSymbol`/`IOperation`); never decide by method name alone.

## Pull requests

Fill in the PR template, keep PRs focused, and update `CHANGELOG.md`.
Publishing to NuGet is done by the maintainers via a `v*` tag.
