using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Operations;

namespace AspGuard.Analyzers;

/// <summary>AG001: an EF Core query executed in a loop. AG002: one executed in a Select/ForEach lambda over an in-memory collection.</summary>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class QueryInLoopAnalyzer : DiagnosticAnalyzer
{
    /// <inheritdoc />
    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics { get; } =
        ImmutableArray.Create(Rules.QueryInLoop, Rules.QueryInLambda);

    /// <inheritdoc />
    public override void Initialize(AnalysisContext context)
    {
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.EnableConcurrentExecution();
        context.RegisterCompilationStartAction(start =>
        {
            var ef = EfSymbols.Create(start.Compilation);
            if (ef is null)
            {
                return;
            }

            start.RegisterOperationAction(c => Analyze(c, ef), OperationKind.Invocation);
        });
    }

    private static void Analyze(OperationAnalysisContext context, EfSymbols ef)
    {
        var invocation = (IInvocationOperation)context.Operation;
        if (!EfSymbols.IsExecutionName(invocation.TargetMethod.Name) && invocation.TargetMethod.Name is not ("Find" or "FindAsync"))
        {
            return;
        }

        if (!ef.IsQueryExecution(invocation))
        {
            return;
        }

        var descriptor = FindEnclosure(invocation);
        if (descriptor is null || !ScopeFilter.IsInScope(context.Options, invocation.Syntax.SyntaxTree, context.ContainingSymbol))
        {
            return;
        }

        context.ReportDiagnostic(Diagnostic.Create(descriptor, invocation.Syntax.GetLocation(), invocation.TargetMethod.Name));
    }

    /// <summary>Walks up from the query to the nearest loop body (AG001) or in-memory lambda (AG002); null when neither.</summary>
    private static DiagnosticDescriptor? FindEnclosure(IOperation query)
    {
        var child = query;
        for (var parent = query.Parent; parent is not null; child = parent, parent = parent.Parent)
        {
            switch (parent)
            {
                case ILocalFunctionOperation:
                    return null;

                case IAnonymousFunctionOperation lambda:
                    return IsInMemoryLambdaHost(lambda) ? Rules.QueryInLambda : null;

                case IForEachLoopOperation forEach when Same(child, forEach.Body):
                    return Rules.QueryInLoop;

                case IForLoopOperation forLoop when Same(child, forLoop.Body) || Same(child, forLoop.Condition) || forLoop.AtLoopBottom.Any(b => Same(child, b)):
                    return Rules.QueryInLoop;

                case IWhileLoopOperation whileLoop when Same(child, whileLoop.Body) || Same(child, whileLoop.Condition):
                    return Rules.QueryInLoop;
            }
        }

        return null;
    }

    private static bool Same(IOperation child, IOperation? other) =>
        other is not null && child.Syntax == other.Syntax && child.Kind == other.Kind;

    private static bool IsInMemoryLambdaHost(IAnonymousFunctionOperation lambda)
    {
        var parent = lambda.Parent;
        while (parent is IDelegateCreationOperation or IConversionOperation or IArgumentOperation)
        {
            parent = parent.Parent;
        }

        if (parent is not IInvocationOperation host)
        {
            return false;
        }

        var method = host.TargetMethod;
        var type = method.ContainingType.OriginalDefinition.ToDisplayString();
        return (type == "System.Linq.Enumerable" && method.Name is "Select" or "SelectMany" or "Where")
            || (method.Name == "ForEach" && type is "System.Collections.Generic.List<T>" or "System.Array");
    }
}
