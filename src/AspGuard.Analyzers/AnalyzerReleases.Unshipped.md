### New Rules

Rule ID | Category | Severity | Notes
--------|----------|----------|-------
AG001 | Performance | Warning | Database query executed inside a loop
AG002 | Performance | Warning | Database query executed inside an in-memory Select/ForEach lambda
AG010 | Security | Warning | Dynamically built SQL passed to a raw SQL method
AG020 | Performance | Warning | Filtering in memory after loading a whole table
AG021 | Performance | Warning | Blocking on an EF Core async call
AG030 | Documentation | Info | Member has no XML documentation comment
