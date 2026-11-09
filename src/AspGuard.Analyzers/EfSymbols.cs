using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Operations;

namespace AspGuard.Analyzers;

/// <summary>
/// Semantic recognition of EF Core queries. Names alone are never trusted: the receiver must trace back to a
/// <c>DbSet&lt;T&gt;</c> (or the method must live in an EF Core extension class).
/// </summary>
internal sealed class EfSymbols
{
    private static readonly HashSet<string> _executionNames = new(StringComparer.Ordinal)
    {
        "ToList", "ToArray", "ToDictionary", "ToHashSet", "ToLookup",
        "First", "FirstOrDefault", "Single", "SingleOrDefault", "Last", "LastOrDefault",
        "Count", "LongCount", "Any", "All", "Sum", "Min", "Max", "Average", "Contains",
        "ElementAt", "ElementAtOrDefault", "ForEach", "Load", "ExecuteDelete", "ExecuteUpdate",
    };

    private readonly Compilation _compilation;
    private readonly INamedTypeSymbol _dbSet;
    private readonly INamedTypeSymbol _dbContext;
    private readonly INamedTypeSymbol? _efQueryableExtensions;
    private readonly INamedTypeSymbol? _queryable;
    private readonly INamedTypeSymbol? _enumerable;

    private EfSymbols(Compilation compilation, INamedTypeSymbol dbSet, INamedTypeSymbol dbContext)
    {
        _compilation = compilation;
        _dbSet = dbSet;
        _dbContext = dbContext;
        _efQueryableExtensions = compilation.GetTypeByMetadataName("Microsoft.EntityFrameworkCore.EntityFrameworkQueryableExtensions");
        _queryable = compilation.GetTypeByMetadataName("System.Linq.Queryable");
        _enumerable = compilation.GetTypeByMetadataName("System.Linq.Enumerable");
    }

    public static EfSymbols? Create(Compilation compilation)
    {
        var dbSet = compilation.GetTypeByMetadataName("Microsoft.EntityFrameworkCore.DbSet`1");
        var dbContext = compilation.GetTypeByMetadataName("Microsoft.EntityFrameworkCore.DbContext");
        return dbSet is null || dbContext is null ? null : new EfSymbols(compilation, dbSet, dbContext);
    }

    public Compilation Compilation => _compilation;

    public static bool IsExecutionName(string name)
    {
        var trimmed = name.EndsWith("Async", StringComparison.Ordinal) ? name.Substring(0, name.Length - 5) : name;
        return _executionNames.Contains(trimmed);
    }

    public bool IsDbSet(ITypeSymbol? type)
    {
        for (var t = type; t is not null; t = t.BaseType)
        {
            if (SymbolEqualityComparer.Default.Equals(t.OriginalDefinition, _dbSet))
            {
                return true;
            }
        }

        return false;
    }

    public bool IsDbContext(ITypeSymbol? type)
    {
        for (var t = type; t is not null; t = t.BaseType)
        {
            if (SymbolEqualityComparer.Default.Equals(t, _dbContext))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>True when the invocation sends a query to the database (not when it merely composes one).</summary>
    public bool IsQueryExecution(IInvocationOperation invocation)
    {
        var method = invocation.TargetMethod;
        var name = method.Name;
        var containing = method.ContainingType;

        if (name is "Find" or "FindAsync")
        {
            return IsDbSet(containing) || IsDbContext(containing);
        }

        if (!IsExecutionName(name))
        {
            return false;
        }

        if (SymbolEqualityComparer.Default.Equals(containing, _efQueryableExtensions))
        {
            return true;
        }

        if (SymbolEqualityComparer.Default.Equals(containing, _queryable) || SymbolEqualityComparer.Default.Equals(containing, _enumerable))
        {
            return invocation.Arguments.Length > 0 && TracesToDbSet(invocation.Arguments[0].Value, 0);
        }

        return false;
    }

    /// <summary>Follows a deferred query expression back to a <c>DbSet&lt;T&gt;</c>. Materialized results (ToList etc.) stop the trace.</summary>
    public bool TracesToDbSet(IOperation? operation, int depth)
    {
        if (depth > 6)
        {
            return false;
        }

        operation = Unwrap(operation);
        if (operation is null)
        {
            return false;
        }

        if (IsDbSet(operation.Type))
        {
            return true;
        }

        switch (operation)
        {
            case IInvocationOperation invocation:
                var method = invocation.TargetMethod;
                var containing = method.ContainingType;
                var isLinq = SymbolEqualityComparer.Default.Equals(containing, _queryable)
                    || SymbolEqualityComparer.Default.Equals(containing, _enumerable)
                    || SymbolEqualityComparer.Default.Equals(containing, _efQueryableExtensions)
                    || containing.ToDisplayString() == "Microsoft.EntityFrameworkCore.RelationalQueryableExtensions";
                if (!isLinq || IsExecutionName(method.Name))
                {
                    return false;
                }

                return invocation.Arguments.Length > 0 && TracesToDbSet(invocation.Arguments[0].Value, depth + 1);

            case ILocalReferenceOperation local:
                var syntax = local.Local.DeclaringSyntaxReferences.FirstOrDefault()?.GetSyntax() as VariableDeclaratorSyntax;
                var initializer = syntax?.Initializer?.Value;
                if (initializer is null)
                {
                    return false;
                }

                var model = _compilation.GetSemanticModel(initializer.SyntaxTree);
                return TracesToDbSet(model.GetOperation(initializer), depth + 1);

            default:
                return false;
        }
    }

    public bool IsEnumerableType(ITypeSymbol? type) => SymbolEqualityComparer.Default.Equals(type, _enumerable);

    public bool IsEfQueryableExtensions(ITypeSymbol? type) => SymbolEqualityComparer.Default.Equals(type, _efQueryableExtensions);

    /// <summary>True for EF Core's async entry points (query extensions, DbSet/DbContext methods, raw SQL helpers).</summary>
    public bool IsEfAsyncCall(IInvocationOperation invocation)
    {
        var method = invocation.TargetMethod;
        if (!method.Name.EndsWith("Async", StringComparison.Ordinal))
        {
            return false;
        }

        var containing = method.ContainingType;
        return IsEfQueryableExtensions(containing)
            || IsDbSet(containing)
            || IsDbContext(containing)
            || containing.ToDisplayString() is "Microsoft.EntityFrameworkCore.RelationalDatabaseFacadeExtensions" or "Microsoft.EntityFrameworkCore.RelationalQueryableExtensions";
    }

    /// <summary>True when any method along the first-argument chain has one of the given names (e.g. <c>Take</c>).</summary>
    public static bool ChainContains(IOperation? operation, ICollection<string> names)
    {
        for (var depth = 0; depth < 12; depth++)
        {
            if (Unwrap(operation) is not IInvocationOperation invocation)
            {
                return false;
            }

            if (names.Contains(invocation.TargetMethod.Name))
            {
                return true;
            }

            operation = invocation.Arguments.Length > 0 ? invocation.Arguments[0].Value : null;
        }

        return false;
    }

    public static IOperation? Unwrap(IOperation? operation)
    {
        while (true)
        {
            switch (operation)
            {
                case IConversionOperation conversion:
                    operation = conversion.Operand;
                    break;
                case IParenthesizedOperation parenthesized:
                    operation = parenthesized.Operand;
                    break;
                default:
                    return operation;
            }
        }
    }
}
