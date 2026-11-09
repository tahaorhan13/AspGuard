using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Operations;

namespace AspGuard.Analyzers;

/// <summary>
/// AG010: dynamically built SQL handed to a raw SQL method: EF Core's <c>FromSqlRaw</c>/<c>ExecuteSqlRaw</c>/<c>SqlQueryRaw</c>
/// (direct interpolation and concatenation are left to EF Core's own EF1002), and EF6's <c>Database.SqlQuery</c>,
/// <c>Database.ExecuteSqlCommand(Async)</c> and <c>DbSet.SqlQuery</c> (EF6 has no EF1002, so those are reported too).
/// </summary>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class RawSqlAnalyzer : DiagnosticAnalyzer
{
    private const string QueryableExtensions = "Microsoft.EntityFrameworkCore.RelationalQueryableExtensions";
    private const string FacadeExtensions = "Microsoft.EntityFrameworkCore.RelationalDatabaseFacadeExtensions";
    private const string Ef6Database = "System.Data.Entity.Database";

    /// <inheritdoc />
    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics { get; } = ImmutableArray.Create(Rules.DynamicRawSql);

    /// <inheritdoc />
    public override void Initialize(AnalysisContext context)
    {
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.EnableConcurrentExecution();
        context.RegisterCompilationStartAction(start =>
        {
            if (start.Compilation.GetTypeByMetadataName(QueryableExtensions) is null && start.Compilation.GetTypeByMetadataName(Ef6Database) is null)
            {
                return;
            }

            start.RegisterOperationAction(Analyze, OperationKind.Invocation);
        });
    }

    internal static bool IsRawSqlMethod(IMethodSymbol method) => IsEfCoreRawSqlMethod(method) || IsEf6RawSqlMethod(method);

    internal static bool IsEfCoreRawSqlMethod(IMethodSymbol method)
    {
        var type = method.ContainingType?.ToDisplayString();
        return (type == QueryableExtensions && method.Name == "FromSqlRaw")
            || (type == FacadeExtensions && method.Name is "ExecuteSqlRaw" or "ExecuteSqlRawAsync" or "SqlQueryRaw");
    }

    /// <summary>EF6 raw SQL entry points; their <c>{0}</c> placeholders become DbParameters.</summary>
    internal static bool IsEf6RawSqlMethod(IMethodSymbol method)
    {
        var type = method.ContainingType?.OriginalDefinition.ToDisplayString();
        return (type == Ef6Database && method.Name is "SqlQuery" or "ExecuteSqlCommand" or "ExecuteSqlCommandAsync")
            || (type is "System.Data.Entity.DbSet" or "System.Data.Entity.DbSet<TEntity>" && method.Name == "SqlQuery");
    }

    private static void Analyze(OperationAnalysisContext context)
    {
        var invocation = (IInvocationOperation)context.Operation;
        if (!IsRawSqlMethod(invocation.TargetMethod))
        {
            return;
        }

        // EF Core's EF1002 already reports direct interpolation/concatenation; EF6 has nothing similar.
        var sql = invocation.Arguments.FirstOrDefault(a => a.Parameter?.Name == "sql");
        if (sql is null || !IsDynamic(sql.Value, 0, topLevel: !IsEf6RawSqlMethod(invocation.TargetMethod)))
        {
            return;
        }

        context.ReportDiagnostic(Diagnostic.Create(Rules.DynamicRawSql, invocation.Syntax.GetLocation(), invocation.TargetMethod.Name));
    }

    private static bool IsDynamic(IOperation? operation, int depth, bool topLevel)
    {
        operation = EfSymbols.Unwrap(operation);
        if (operation is null || depth > 4 || operation.ConstantValue.HasValue)
        {
            return false;
        }

        switch (operation)
        {
            case IInterpolatedStringOperation:
                return !topLevel;

            case IBinaryOperation { OperatorKind: BinaryOperatorKind.Add } when operation.Type?.SpecialType == SpecialType.System_String:
                return !topLevel;

            case IInvocationOperation invocation:
                var method = invocation.TargetMethod;
                if (method.ContainingType.SpecialType == SpecialType.System_String && method.Name is "Format" or "Concat" or "Join" or "Replace" or "Insert")
                {
                    return invocation.Arguments.Any(a => !AllConstant(a.Value))
                        || (invocation.Instance is not null && IsDynamic(invocation.Instance, depth + 1, topLevel: false));
                }

                return method.Name == "ToString" && method.ContainingType.ToDisplayString() == "System.Text.StringBuilder";

            case IConditionalOperation conditional:
                return IsDynamic(conditional.WhenTrue, depth + 1, topLevel)
                    || IsDynamic(conditional.WhenFalse, depth + 1, topLevel);

            case ILocalReferenceOperation local:
                return AssignedValues(local).Any(v => v.Compound || IsDynamic(v.Value, depth + 1, topLevel: false));

            default:
                return false;
        }
    }

    private static bool AllConstant(IOperation? operation)
    {
        operation = EfSymbols.Unwrap(operation);
        return operation switch
        {
            null => true,
            IArrayCreationOperation array => array.Initializer?.ElementValues.All(AllConstant) ?? true,
            _ => operation.ConstantValue.HasValue,
        };
    }

    private static IEnumerable<(IOperation? Value, bool Compound)> AssignedValues(ILocalReferenceOperation reference)
    {
        var local = reference.Local;
        var declaration = local.DeclaringSyntaxReferences.FirstOrDefault()?.GetSyntax();
        var model = reference.SemanticModel;

        // Only locals declared in the same syntax tree can be followed with the model the analysis provides.
        if (declaration is null || model is null || declaration.SyntaxTree != reference.Syntax.SyntaxTree)
        {
            yield break;
        }

        var member = declaration.AncestorsAndSelf().OfType<MemberDeclarationSyntax>().FirstOrDefault();
        SyntaxNode root = member is null or GlobalStatementSyntax ? declaration.SyntaxTree.GetRoot() : member;

        foreach (var node in root.DescendantNodes())
        {
            if (node is VariableDeclaratorSyntax { Initializer: { } initializer } declarator
                && SymbolEqualityComparer.Default.Equals(model.GetDeclaredSymbol(declarator), local))
            {
                yield return (model.GetOperation(initializer.Value), false);
            }
            else if (node is AssignmentExpressionSyntax assignment
                && SymbolEqualityComparer.Default.Equals(model.GetSymbolInfo(assignment.Left).Symbol, local))
            {
                if (assignment.IsKind(SyntaxKind.AddAssignmentExpression))
                {
                    yield return (null, !model.GetConstantValue(assignment.Right).HasValue);
                }
                else
                {
                    yield return (model.GetOperation(assignment.Right), false);
                }
            }
        }
    }
}
