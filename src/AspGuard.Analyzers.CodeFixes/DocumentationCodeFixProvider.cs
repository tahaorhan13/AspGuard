using System.Collections.Immutable;
using System.Composition;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CodeActions;
using Microsoft.CodeAnalysis.CodeFixes;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace AspGuard.Analyzers;

/// <summary>
/// AG030 code fix: inserts <c>/// &lt;summary&gt;</c> with the summary the analyzer generated from the member name,
/// indented like the member and using the file's line endings. Fix All (and <c>dotnet format</c>) documents a whole
/// file, project or solution in one go.
/// </summary>
[ExportCodeFixProvider(LanguageNames.CSharp, Name = nameof(DocumentationCodeFixProvider))]
[Shared]
public sealed class DocumentationCodeFixProvider : CodeFixProvider
{
    private const string Title = "Add XML documentation comment";

    /// <inheritdoc />
    public override ImmutableArray<string> FixableDiagnosticIds { get; } = ImmutableArray.Create("AG030");

    /// <inheritdoc />
    public override FixAllProvider GetFixAllProvider() => WellKnownFixAllProviders.BatchFixer;

    /// <inheritdoc />
    public override async Task RegisterCodeFixesAsync(CodeFixContext context)
    {
        var root = await context.Document.GetSyntaxRootAsync(context.CancellationToken).ConfigureAwait(false);
        if (root is null)
        {
            return;
        }

        foreach (var diagnostic in context.Diagnostics)
        {
            if (!diagnostic.Properties.TryGetValue(MissingDocumentationAnalyzer.SummaryProperty, out var summary) || summary is null)
            {
                continue;
            }

            var member = root.FindToken(diagnostic.Location.SourceSpan.Start).Parent?.AncestorsAndSelf().OfType<BaseMethodDeclarationSyntax>().FirstOrDefault();
            if (member is null)
            {
                continue;
            }

            context.RegisterCodeFix(
                CodeAction.Create(
                    Title,
                    _ => Task.FromResult(context.Document.WithSyntaxRoot(root.ReplaceNode(member, AddComment(root, member, summary)))),
                    equivalenceKey: Title),
                diagnostic);
        }
    }

    private static SyntaxNode AddComment(SyntaxNode root, BaseMethodDeclarationSyntax member, string summary)
    {
        var leading = member.GetLeadingTrivia();
        var indent = leading.Count > 0 && leading[leading.Count - 1].IsKind(SyntaxKind.WhitespaceTrivia) ? leading[leading.Count - 1].ToString() : string.Empty;
        var newLine = root.DescendantTrivia().FirstOrDefault(t => t.IsKind(SyntaxKind.EndOfLineTrivia)).ToString();
        if (newLine.Length == 0)
        {
            newLine = "\n";
        }

        var comment = $"/// <summary>{newLine}{indent}/// {summary}{newLine}{indent}/// </summary>{newLine}{indent}";
        return member.WithLeadingTrivia(leading.AddRange(SyntaxFactory.ParseLeadingTrivia(comment)));
    }
}
