using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Operations;

namespace AspGuard.Analyzers;

/// <summary>AG021: <c>.Result</c>, <c>.Wait()</c> or <c>.GetAwaiter().GetResult()</c> directly on an EF Core async call.</summary>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class BlockingCallAnalyzer : DiagnosticAnalyzer
{
    /// <inheritdoc />
    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics { get; } = ImmutableArray.Create(Rules.BlockingOnQuery);

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

            start.RegisterOperationAction(c => AnalyzeResult(c, ef), OperationKind.PropertyReference);
            start.RegisterOperationAction(c => AnalyzeInvocation(c, ef), OperationKind.Invocation);
        });
    }

    private static void AnalyzeResult(OperationAnalysisContext context, EfSymbols ef)
    {
        var property = (IPropertyReferenceOperation)context.Operation;
        if (property.Property.Name != "Result" || !IsTaskLike(property.Property.ContainingType))
        {
            return;
        }

        Report(context, ef, property.Instance, property.Syntax);
    }

    private static void AnalyzeInvocation(OperationAnalysisContext context, EfSymbols ef)
    {
        var invocation = (IInvocationOperation)context.Operation;
        var method = invocation.TargetMethod;
        if (method.Name == "Wait" && IsTaskLike(method.ContainingType))
        {
            Report(context, ef, invocation.Instance, invocation.Syntax);
        }
        else if (method.Name == "GetResult" && method.ContainingType.Name.Contains("Awaiter", StringComparison.Ordinal)
            && EfSymbols.Unwrap(invocation.Instance) is IInvocationOperation { TargetMethod.Name: "GetAwaiter" } getAwaiter)
        {
            Report(context, ef, getAwaiter.Instance, invocation.Syntax);
        }
    }

    private static void Report(OperationAnalysisContext context, EfSymbols ef, IOperation? task, SyntaxNode location)
    {
        if (EfSymbols.Unwrap(task) is not IInvocationOperation call || !ef.IsEfAsyncCall(call))
        {
            return;
        }

        if (!ScopeFilter.IsInScope(context.Options, location.SyntaxTree, context.ContainingSymbol))
        {
            return;
        }

        context.ReportDiagnostic(Diagnostic.Create(Rules.BlockingOnQuery, location.GetLocation(), call.TargetMethod.Name));
    }

    private static bool IsTaskLike(INamedTypeSymbol? type) =>
        type is not null && type.OriginalDefinition.ToDisplayString() is
            "System.Threading.Tasks.Task" or "System.Threading.Tasks.Task<TResult>" or "System.Threading.Tasks.ValueTask<TResult>" or "System.Threading.Tasks.ValueTask";
}
