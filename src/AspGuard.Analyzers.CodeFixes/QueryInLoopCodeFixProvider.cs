using System.Collections.Immutable;
using System.Composition;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CodeActions;
using Microsoft.CodeAnalysis.CodeFixes;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Formatting;

namespace AspGuard.Analyzers;

/// <summary>
/// AG001 code fix for the one shape that can be rewritten safely:
/// <c>foreach (var id in ids) { db.Set.FirstOrDefault(x =&gt; x.Prop == id) }</c> (also <c>SingleOrDefault</c> and the async versions).
/// One query with <c>Contains</c> is inserted before the loop and the call becomes a lookup. <c>Find</c> is never rewritten
/// because the key property cannot be known from syntax.
/// </summary>
[ExportCodeFixProvider(LanguageNames.CSharp, Name = nameof(QueryInLoopCodeFixProvider))]
[Shared]
public sealed class QueryInLoopCodeFixProvider : CodeFixProvider
{
    private const string Title = "Load the data once before the loop (Contains + ToLookup)";

    /// <inheritdoc />
    public override ImmutableArray<string> FixableDiagnosticIds { get; } = ImmutableArray.Create("AG001");

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

        var plan = Plan.TryCreate(root, invocation, model, context.CancellationToken);
        if (plan is null)
        {
            return;
        }

        context.RegisterCodeFix(
            CodeAction.Create(Title, _ => Task.FromResult(context.Document.WithSyntaxRoot(plan.Apply(root))), equivalenceKey: Title),
            context.Diagnostics);
    }

    private sealed class Plan
    {
        private readonly ForEachStatementSyntax _loop;
        private readonly SyntaxNode _target;
        private readonly string _lookupName;
        private readonly string _declaration;
        private readonly string _replacement;
        private readonly bool _needsLinq;

        private Plan(ForEachStatementSyntax loop, SyntaxNode target, string lookupName, string declaration, string replacement, bool needsLinq)
        {
            _loop = loop;
            _target = target;
            _lookupName = lookupName;
            _declaration = declaration;
            _replacement = replacement;
            _needsLinq = needsLinq;
        }

        public static Plan? TryCreate(SyntaxNode root, InvocationExpressionSyntax invocation, SemanticModel model, CancellationToken cancellationToken)
        {
            if (invocation.Expression is not MemberAccessExpressionSyntax { Name: IdentifierNameSyntax methodName } access)
            {
                return null;
            }

            var isAsync = methodName.Identifier.Text.EndsWith("Async", StringComparison.Ordinal);
            var linqName = isAsync ? methodName.Identifier.Text.Substring(0, methodName.Identifier.Text.Length - 5) : methodName.Identifier.Text;
            if (linqName is not ("FirstOrDefault" or "SingleOrDefault"))
            {
                return null;
            }

            var arguments = invocation.ArgumentList.Arguments;
            if (arguments.Count != 1 || arguments[0].NameColon is not null
                || arguments[0].Expression is not SimpleLambdaExpressionSyntax { Body: BinaryExpressionSyntax body } lambda
                || lambda.AsyncKeyword != default
                || !body.IsKind(SyntaxKind.EqualsExpression))
            {
                return null;
            }

            var loop = invocation.Ancestors().OfType<ForEachStatementSyntax>().FirstOrDefault();
            if (loop is null || !loop.Statement.Span.Contains(invocation.Span) || loop.Parent is not BlockSyntax)
            {
                return null;
            }

            // The query must not sit inside a nested lambda or local function: the rewrite hoists it out of there.
            if (invocation.Ancestors().TakeWhile(a => a != loop).Any(a => a is AnonymousFunctionExpressionSyntax or LocalFunctionStatementSyntax))
            {
                return null;
            }

            var parameter = lambda.Parameter.Identifier.Text;
            var loopVariable = loop.Identifier.Text;
            if (!TryGetProperty(body.Left, body.Right, parameter, loopVariable, out var property)
                && !TryGetProperty(body.Right, body.Left, parameter, loopVariable, out property))
            {
                return null;
            }

            if (!IsPlainReference(loop.Expression) || !IsPlainReference(access.Expression)
                || access.Expression.DescendantTokens().Any(t => t.Text == loopVariable))
            {
                return null;
            }

            if (model.GetTypeInfo(access.Expression, cancellationToken).Type?.BaseType is null
                || !IsDbSet(model.GetTypeInfo(access.Expression, cancellationToken).Type))
            {
                return null;
            }

            var propertyType = (model.GetSymbolInfo(property, cancellationToken).Symbol as IPropertySymbol)?.Type;
            var loopType = (model.GetDeclaredSymbol(loop, cancellationToken) as ILocalSymbol)?.Type;
            if (propertyType is null || loopType is null || !SymbolEqualityComparer.Default.Equals(propertyType, loopType))
            {
                return null;
            }

            SyntaxNode target = invocation;
            if (isAsync)
            {
                if (invocation.Parent is not AwaitExpressionSyntax await)
                {
                    return null;
                }

                target = await;
            }

            var set = access.Expression.WithoutTrivia().ToString();
            var collection = loop.Expression.WithoutTrivia().ToString();
            var propertyName = property.Name.Identifier.Text;
            var name = UniqueName(root, set, propertyName);
            var query = $"{set}.Where({parameter} => {collection}.Contains({parameter}.{propertyName}))";
            var declaration = isAsync
                ? $"var {name} = (await {query}.ToListAsync()).ToLookup({parameter} => {parameter}.{propertyName});"
                : $"var {name} = {query}.ToLookup({parameter} => {parameter}.{propertyName});";

            var probe = SyntaxFactory.ParseExpression($"{set}.Where(__x => true)");
            var needsLinq = model.GetSpeculativeSymbolInfo(loop.SpanStart, probe, SpeculativeBindingOption.BindAsExpression).Symbol is null;

            return new Plan(loop, target, name, declaration, $"{name}[{loopVariable}].{linqName}()", needsLinq);
        }

        public SyntaxNode Apply(SyntaxNode root)
        {
            var tracked = root.TrackNodes(_loop, _target);
            var target = tracked.GetCurrentNode(_target)!;
            var replacement = SyntaxFactory.ParseExpression(_replacement).WithTriviaFrom(target);
            tracked = tracked.ReplaceNode(target, replacement);

            var loop = tracked.GetCurrentNode(_loop)!;
            var endOfLine = root.DescendantTrivia().FirstOrDefault(t => t.IsKind(SyntaxKind.EndOfLineTrivia));
            var indentation = loop.GetLeadingTrivia().LastOrDefault(t => t.IsKind(SyntaxKind.WhitespaceTrivia));
            var statement = SyntaxFactory.ParseStatement(_declaration)
                .WithLeadingTrivia(indentation)
                .WithTrailingTrivia(endOfLine.IsKind(SyntaxKind.EndOfLineTrivia) ? endOfLine : SyntaxFactory.LineFeed)
                .WithAdditionalAnnotations(Formatter.Annotation);
            tracked = tracked.InsertNodesBefore(loop, new[] { statement });

            if (_needsLinq && tracked is CompilationUnitSyntax unit && !unit.Usings.Any(u => u.Name?.ToString() == "System.Linq"))
            {
                var directive = SyntaxFactory.UsingDirective(SyntaxFactory.ParseName("System.Linq"))
                    .WithTrailingTrivia(endOfLine.IsKind(SyntaxKind.EndOfLineTrivia) ? endOfLine : SyntaxFactory.LineFeed);
                tracked = unit.AddUsings(directive);
            }

            return tracked;
        }

        private static bool TryGetProperty(ExpressionSyntax propertySide, ExpressionSyntax loopSide, string parameter, string loopVariable, out MemberAccessExpressionSyntax property)
        {
            if (propertySide is MemberAccessExpressionSyntax { Expression: IdentifierNameSyntax owner, Name: IdentifierNameSyntax } member
                && owner.Identifier.Text == parameter
                && loopSide is IdentifierNameSyntax loop
                && loop.Identifier.Text == loopVariable)
            {
                property = member;
                return true;
            }

            property = null!;
            return false;
        }

        /// <summary>An identifier or a chain of member accesses: no calls, so evaluating it twice has no side effects.</summary>
        private static bool IsPlainReference(ExpressionSyntax expression) => expression switch
        {
            IdentifierNameSyntax => true,
            ThisExpressionSyntax => true,
            MemberAccessExpressionSyntax member => member.IsKind(SyntaxKind.SimpleMemberAccessExpression) && IsPlainReference(member.Expression),
            _ => false,
        };

        private static bool IsDbSet(ITypeSymbol? type)
        {
            for (var t = type; t is not null; t = t.BaseType)
            {
                if (t.OriginalDefinition.ToDisplayString() is "Microsoft.EntityFrameworkCore.DbSet<TEntity>" or "System.Data.Entity.DbSet<TEntity>" or "System.Data.Entity.IDbSet<TEntity>")
                {
                    return true;
                }
            }

            return false;
        }

        private static string UniqueName(SyntaxNode root, string set, string property)
        {
            var last = set.Substring(set.LastIndexOf('.') + 1);
            var stem = char.ToLowerInvariant(last[0]) + last.Substring(1) + "By" + property;
            var taken = new HashSet<string>(root.DescendantTokens().Where(t => t.IsKind(SyntaxKind.IdentifierToken)).Select(t => t.Text));
            var name = stem;
            for (var i = 2; taken.Contains(name); i++)
            {
                name = stem + i;
            }

            return name;
        }
    }
}
