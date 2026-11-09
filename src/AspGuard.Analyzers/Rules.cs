using Microsoft.CodeAnalysis;

namespace AspGuard.Analyzers;

internal static class Rules
{
    // Replace with the final repository URL before publishing.
    private const string HelpBase = "https://github.com/AspGuard/AspGuard/blob/main/docs/rules/";

    public static readonly DiagnosticDescriptor QueryInLoop = new(
        "AG001",
        "Database query executed inside a loop",
        "EF Core query '{0}' is executed inside a loop; load the data with a single query (Include, Contains) before the loop",
        "Performance",
        DiagnosticSeverity.Warning,
        isEnabledByDefault: true,
        description: "Running a database query on every iteration of a loop is the classic N+1 problem.",
        helpLinkUri: HelpBase + "AG001.md");

    public static readonly DiagnosticDescriptor QueryInLambda = new(
        "AG002",
        "Database query executed inside an in-memory Select/ForEach lambda",
        "EF Core query '{0}' is executed once per element inside a lambda over an in-memory collection; load the data with a single query before iterating",
        "Performance",
        DiagnosticSeverity.Warning,
        isEnabledByDefault: true,
        description: "Select, SelectMany, Where and ForEach over an in-memory collection run their lambda per element, so a query inside it runs N times.",
        helpLinkUri: HelpBase + "AG002.md");

    public static readonly DiagnosticDescriptor DynamicRawSql = new(
        "AG010",
        "Dynamically built SQL passed to a raw SQL method",
        "'{0}' receives a dynamically built SQL string, which risks SQL injection; use the interpolated FromSql/ExecuteSql/SqlQuery or pass values as parameters",
        "Security",
        DiagnosticSeverity.Warning,
        isEnabledByDefault: true,
        description: "string.Format, StringBuilder, concatenation or interpolation stored in a variable make FromSqlRaw/ExecuteSqlRaw/SqlQueryRaw vulnerable to SQL injection. Direct interpolation and concatenation are reported by EF Core's own EF1002 and are not repeated here.",
        helpLinkUri: HelpBase + "AG010.md");

    public static readonly DiagnosticDescriptor MaterializeThenFilter = new(
        "AG020",
        "Filtering in memory after loading a whole table",
        "'{0}' filters in memory after '{1}' loaded every row of the query; move the filter into the query before it is materialized",
        "Performance",
        DiagnosticSeverity.Warning,
        isEnabledByDefault: true,
        description: "ToList/ToArray/AsEnumerable followed by Where/Select pulls the whole table (or query result) into memory first.",
        helpLinkUri: HelpBase + "AG020.md");

    public static readonly DiagnosticDescriptor BlockingOnQuery = new(
        "AG021",
        "Blocking on an EF Core async call",
        "Blocking on EF Core async call '{0}' wastes a thread and can deadlock; await it instead",
        "Performance",
        DiagnosticSeverity.Warning,
        isEnabledByDefault: true,
        description: ".Result, .Wait() and .GetAwaiter().GetResult() on an EF Core async call block the calling thread.",
        helpLinkUri: HelpBase + "AG021.md");

    public static readonly DiagnosticDescriptor MissingDocumentation = new(
        "AG030",
        "Member has no XML documentation comment",
        "'{0}' has no XML documentation comment; one can be generated from its name",
        "Documentation",
        DiagnosticSeverity.Info,
        isEnabledByDefault: true,
        description: "Methods and constructors without a /// comment. The code fix writes a summary derived from the member name (AddPerson → \"Kişi ekler.\" / \"Adds the person.\"). Language: aspguard.doc_language = tr | en in .editorconfig (default tr).",
        helpLinkUri: HelpBase + "AG030.md");
}
