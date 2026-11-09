using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Operations;

namespace AspGuard.Analyzers;

/// <summary>AG020: <c>ToList()/ToArray()/AsEnumerable()</c> of an EF Core query directly followed by <c>Where</c>/<c>Select</c>.</summary>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class MaterializeThenFilterAnalyzer : DiagnosticAnalyzer
{
    private static readonly string[] _boundingMethods = ["Take", "TakeLast"];

    /// <inheritdoc />
    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics { get; } = ImmutableArray.Create(Rules.MaterializeThenFilter);

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
        var filter = (IInvocationOperation)context.Operation;
        var method = filter.TargetMethod;
        if (method.Name is not ("Where" or "Select") || !ef.IsEnumerableType(method.ContainingType) || filter.Arguments.Length == 0)
        {
            return;
        }

        var source = EfSymbols.Unwrap(filter.Arguments[0].Value);
        while (source is IAwaitOperation await)
        {
            source = EfSymbols.Unwrap(await.Operation);
        }

        if (source is not IInvocationOperation materialization || materialization.Arguments.Length == 0)
        {
            return;
        }

        var materializer = materialization.TargetMethod;
        var materializes = (ef.IsEnumerableType(materializer.ContainingType) && materializer.Name is "ToList" or "ToArray" or "AsEnumerable")
            || (ef.IsEfQueryableExtensions(materializer.ContainingType) && materializer.Name is "ToListAsync" or "ToArrayAsync");
        if (!materializes)
        {
            return;
        }

        var query = materialization.Arguments[0].Value;
        if (!ef.TracesToDbSet(query, 0) || EfSymbols.ChainContains(query, _boundingMethods))
        {
            return;
        }

        if (!ScopeFilter.IsInScope(context.Options, filter.Syntax.SyntaxTree, context.ContainingSymbol))
        {
            return;
        }

        context.ReportDiagnostic(Diagnostic.Create(Rules.MaterializeThenFilter, filter.Syntax.GetLocation(), method.Name, materializer.Name));
    }
}
