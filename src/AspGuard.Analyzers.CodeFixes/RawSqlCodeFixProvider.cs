using System.Collections.Immutable;
using System.Composition;
using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CodeActions;
using Microsoft.CodeAnalysis.CodeFixes;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Formatting;

namespace AspGuard.Analyzers;

/// <summary>
/// AG010 code fix: <c>FromSqlRaw(string.Format("... {0} ...", x))</c> becomes <c>FromSql($"... {x} ...")</c> (EF Core), and
/// EF6's <c>SqlQuery(string.Format("... {0} ...", x))</c> becomes <c>SqlQuery("... {0} ...", x)</c>, where EF6 turns the
/// placeholders into DbParameters.
/// Offered only when the format string is a constant with plain <c>{n}</c> placeholders that are not inside a quoted SQL literal.
/// </summary>
[ExportCodeFixProvider(LanguageNames.CSharp, Name = nameof(RawSqlCodeFixProvider))]
[Shared]
public sealed class RawSqlCodeFixProvider : CodeFixProvider
{
    private const string Title = "Use the interpolated, parameterized FromSql/ExecuteSql/SqlQuery";
    private const string Ef6Title = "Pass the values as SQL parameters ({0} placeholders)";

    /// <inheritdoc />
    public override ImmutableArray<string> FixableDiagnosticIds { get; } = ImmutableArray.Create("AG010");

    /// <inheritdoc />
    public override FixAllProvider GetFixAllProvider() => WellKnownFixAllProviders.BatchFixer;

    /// <inheritdoc />
    public override async Task RegisterCodeFixesAsync(CodeFixContext context)
    {
        var root = await context.Document.GetSyntaxRootAsync(context.CancellationToken).ConfigureAwait(false);
        var model = await context.Document.GetSemanticModelAsync(context.CancellationToken).ConfigureAwait(false);
        var invocation = root?.FindNode(context.Span).AncestorsAndSelf().OfType<InvocationExpressionSyntax>().FirstOrDefault();
        if (root is null || model is null || invocation is null)
        {
            return;
        }

        var replacement = TryConvert(invocation, model, context.CancellationToken);
        if (replacement is null)
        {
            return;
        }

        var title = model.GetSymbolInfo(invocation, context.CancellationToken).Symbol is IMethodSymbol target && RawSqlAnalyzer.IsEf6RawSqlMethod(target) ? Ef6Title : Title;
        context.RegisterCodeFix(
            CodeAction.Create(
                title,
                _ => Task.FromResult(context.Document.WithSyntaxRoot(root.ReplaceNode(invocation, replacement.WithAdditionalAnnotations(Formatter.Annotation)))),
                equivalenceKey: title),
            context.Diagnostics);
    }

    private static InvocationExpressionSyntax? TryConvert(InvocationExpressionSyntax invocation, SemanticModel model, CancellationToken cancellationToken)
    {
        if (model.GetSymbolInfo(invocation, cancellationToken).Symbol is not IMethodSymbol method
            || !RawSqlAnalyzer.IsRawSqlMethod(method)
            || invocation.Expression is not MemberAccessExpressionSyntax member)
        {
            return null;
        }

        var arguments = invocation.ArgumentList.Arguments;
        if (arguments.Count != 1 || arguments[0].NameColon is not null)
        {
            return null;
        }

        var sqlArgument = arguments[0].Expression;
        while (sqlArgument is ParenthesizedExpressionSyntax parenthesized)
        {
            sqlArgument = parenthesized.Expression;
        }

        if (sqlArgument is not InvocationExpressionSyntax format
            || model.GetSymbolInfo(format, cancellationToken).Symbol is not IMethodSymbol { Name: "Format", ContainingType.SpecialType: SpecialType.System_String })
        {
            return null;
        }

        var formatArguments = format.ArgumentList.Arguments;
        if (formatArguments.Count < 2 || formatArguments.Any(a => a.NameColon is not null || !a.RefKindKeyword.IsKind(SyntaxKind.None)))
        {
            return null;
        }

        var constant = model.GetConstantValue(formatArguments[0].Expression, cancellationToken);
        if (!constant.HasValue || constant.Value is not string text)
        {
            return null;
        }

        if (formatArguments.Count == 2 && model.GetTypeInfo(formatArguments[1].Expression, cancellationToken).Type is IArrayTypeSymbol)
        {
            return null;
        }

        var interpolated = TryBuildInterpolation(text, formatArguments.Skip(1).Select(a => a.Expression).ToList());

        // EF6: the same safety checks, but the format string and its values become the method's own sql/parameters.
        if (RawSqlAnalyzer.IsEf6RawSqlMethod(method))
        {
            return interpolated is null
                ? null
                : invocation.WithArgumentList(invocation.ArgumentList.WithArguments(SyntaxFactory.SeparatedList(
                    formatArguments.Select((a, i) => i == 0 ? a.WithLeadingTrivia(arguments[0].GetLeadingTrivia()) : a))));
        }

        var newName = method.Name switch
        {
            "FromSqlRaw" => "FromSql",
            "ExecuteSqlRaw" => "ExecuteSql",
            "ExecuteSqlRawAsync" => "ExecuteSqlAsync",
            "SqlQueryRaw" => "SqlQuery",
            _ => null,
        };
        if (interpolated is null || newName is null)
        {
            return null;
        }

        SimpleNameSyntax renamed = member.Name is GenericNameSyntax generic
            ? generic.WithIdentifier(SyntaxFactory.Identifier(newName))
            : SyntaxFactory.IdentifierName(newName);
        renamed = renamed.WithTriviaFrom(member.Name);

        var newArgument = arguments[0].WithExpression(interpolated.WithTriviaFrom(arguments[0].Expression));
        return invocation
            .WithExpression(member.WithName(renamed))
            .WithArgumentList(invocation.ArgumentList.WithArguments(SyntaxFactory.SingletonSeparatedList(newArgument)));
    }

    private static InterpolatedStringExpressionSyntax? TryBuildInterpolation(string format, List<ExpressionSyntax> values)
    {
        var builder = new StringBuilder("$\"");
        var uses = new int[values.Count];
        var quotes = 0;

        for (var i = 0; i < format.Length; i++)
        {
            var c = format[i];
            if (c == '{')
            {
                var end = i + 1;
                while (end < format.Length && char.IsDigit(format[end]))
                {
                    end++;
                }

                // Rejects "{{", alignment/format specifiers ("{0,5}", "{0:N2}") and unterminated holes.
                if (end == i + 1 || end >= format.Length || format[end] != '}' || !int.TryParse(format.Substring(i + 1, end - i - 1), out var index) || index >= values.Count)
                {
                    return null;
                }

                // A placeholder inside a quoted SQL literal ('{0}') would become a quoted parameter, which changes meaning.
                if (quotes % 2 == 1)
                {
                    return null;
                }

                var value = values[index];
                uses[index]++;
                if (uses[index] > 1 && value is not (IdentifierNameSyntax or MemberAccessExpressionSyntax))
                {
                    return null;
                }

                var text = value.WithoutTrivia().ToString();
                if (value is ConditionalExpressionSyntax or SwitchExpressionSyntax or AssignmentExpressionSyntax or AnonymousFunctionExpressionSyntax or BinaryExpressionSyntax)
                {
                    text = "(" + text + ")";
                }

                builder.Append('{').Append(text).Append('}');
                i = end;
                continue;
            }

            if (c == '}')
            {
                return null;
            }

            if (c == '\'')
            {
                quotes++;
            }

            AppendEscaped(builder, c);
        }

        builder.Append('"');
        var parsed = SyntaxFactory.ParseExpression(builder.ToString()) as InterpolatedStringExpressionSyntax;
        return parsed is null || parsed.GetDiagnostics().Any(d => d.Severity == DiagnosticSeverity.Error) ? null : parsed;
    }

    private static void AppendEscaped(StringBuilder builder, char c)
    {
        switch (c)
        {
            case '\\':
                builder.Append("\\\\");
                break;
            case '"':
                builder.Append("\\\"");
                break;
            case '\n':
                builder.Append("\\n");
                break;
            case '\r':
                builder.Append("\\r");
                break;
            case '\t':
                builder.Append("\\t");
                break;
            default:
                if (c < ' ')
                {
                    builder.Append("\\u").Append(((int)c).ToString("x4", System.Globalization.CultureInfo.InvariantCulture));
                }
                else
                {
                    builder.Append(c);
                }

                break;
        }
    }
}
