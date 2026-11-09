using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;

namespace AspGuard.Analyzers;

/// <summary>
/// Honors <c>aspguard.scope = attributed_only</c> (.editorconfig): the performance rules then only look at code
/// inside a class or method marked with <c>AspGuard.GuardQueriesAttribute</c>.
/// </summary>
internal static class ScopeFilter
{
    private const string OptionKey = "aspguard.scope";
    private const string AttributeName = "AspGuard.GuardQueriesAttribute";

    public static bool IsInScope(AnalyzerOptions options, SyntaxTree tree, ISymbol? containingSymbol)
    {
        var config = options.AnalyzerConfigOptionsProvider.GetOptions(tree);
        if (!config.TryGetValue(OptionKey, out var value) || !string.Equals(value?.Trim(), "attributed_only", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        for (var symbol = containingSymbol; symbol is not null && symbol is not INamespaceSymbol; symbol = symbol.ContainingSymbol)
        {
            foreach (var attribute in symbol.GetAttributes())
            {
                if (attribute.AttributeClass?.ToDisplayString() == AttributeName)
                {
                    return true;
                }
            }
        }

        return false;
    }
}
