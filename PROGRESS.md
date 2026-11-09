# Progress

Resume point if the session is interrupted. Update after every phase.

| Phase | Status |
|---|---|
| 0 Skeleton | done |
| 1 Core runtime detector | done (53 tests x 2 TFMs) |
| 2 ASP.NET Core integration + sample | done (7 tests x 2 TFMs) |
| 3 Roslyn analyzer (AG001, AG002) | done (33 tests) |
| 4 Release preparation (benchmarks, packaging, CI, README) | done |
| 5 Opt-in and legacy support | done (runtime + analyzer scope filter, tests) |
| 6 Extra analyzer rules and code fixes | done (100 analyzer tests) |
| 7 AspGuard expansion (rename, one-line setup, security middleware, AG030 docs) | done (42 web x 2 TFMs, 62 core x 2 TFMs, 161 analyzer tests) |
| 8 .NET Framework 4.6.2+ (AspGuard.SystemWeb, AspGuard.EntityFramework6, EF6 analyzers, Roslyn 4.8) | done (34 net48 tests, 177 analyzer tests); version 0.2.0-preview.1 |

## Known issues

- Before publishing: add `assets/icon.png` and the analyzer screenshot to the README.
- The `packages.config` install script (`src/AspGuard.SystemWeb/tools/install.ps1`) has not been tried in Visual Studio.
- The ASP.NET 4.x module is tested through `HttpContextBase` fakes, not under IIS / IIS Express.





