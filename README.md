# NPlusGuard

Catch EF Core N+1 queries before they reach production.

> Status: Phase 0 skeleton. Nothing is usable yet.

NPlusGuard is an open-source library for .NET applications using EF Core. It has two parts:

- **Runtime detector:** an EF Core interceptor that tracks queries within a scope and reports repeated query shapes together with the offending call site.
- **Roslyn analyzer:** compile-time warnings for database queries executed inside loops.

More documentation arrives with the later phases.

## License

MIT
