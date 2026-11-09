using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;

namespace AspGuard.Analyzers;

/// <summary>
/// AG030: a method or constructor without a <c>///</c> comment. The generated summary travels in the diagnostic's
/// properties so the code fix (and <c>dotnet format</c>) only has to insert it.
/// </summary>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class MissingDocumentationAnalyzer : DiagnosticAnalyzer
{
    /// <summary>Diagnostic property holding the generated, XML-escaped summary.</summary>
    public const string SummaryProperty = "summary";

    private const string LanguageOption = "aspguard.doc_language";

    /// <inheritdoc />
    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics { get; } = ImmutableArray.Create(Rules.MissingDocumentation);

    /// <inheritdoc />
    public override void Initialize(AnalysisContext context)
    {
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.EnableConcurrentExecution();
        context.RegisterSyntaxNodeAction(AnalyzeMethod, SyntaxKind.MethodDeclaration);
        context.RegisterSyntaxNodeAction(AnalyzeConstructor, SyntaxKind.ConstructorDeclaration);
    }

    private static void AnalyzeMethod(SyntaxNodeAnalysisContext context)
    {
        var method = (MethodDeclarationSyntax)context.Node;
        if (HasDocumentation(method)
            || method.ExplicitInterfaceSpecifier is not null
            || method.Modifiers.Any(SyntaxKind.OverrideKeyword)
            || method.Modifiers.Any(SyntaxKind.PartialKeyword))
        {
            return;
        }

        var summary = DocumentationText.ForMethod(method.Identifier.ValueText, Language(context));
        if (summary is not null)
        {
            Report(context, method.Identifier.GetLocation(), method.Identifier.ValueText, summary);
        }
    }

    private static void AnalyzeConstructor(SyntaxNodeAnalysisContext context)
    {
        var constructor = (ConstructorDeclarationSyntax)context.Node;
        if (HasDocumentation(constructor) || constructor.Modifiers.Any(SyntaxKind.StaticKeyword) || constructor.Parent is not TypeDeclarationSyntax type)
        {
            return;
        }

        var cref = type.Identifier.ValueText;
        if (type.TypeParameterList is { Parameters.Count: > 0 } typeParameters)
        {
            cref += "{" + string.Join(",", typeParameters.Parameters.Select(p => p.Identifier.ValueText)) + "}";
        }

        var kind = type switch
        {
            StructDeclarationSyntax => "struct",
            RecordDeclarationSyntax record when record.ClassOrStructKeyword.IsKind(SyntaxKind.StructKeyword) => "struct",
            RecordDeclarationSyntax => "record",
            _ => "class",
        };

        Report(context, constructor.Identifier.GetLocation(), constructor.Identifier.ValueText, DocumentationText.ForConstructor(cref, kind, Language(context)));
    }

    private static void Report(SyntaxNodeAnalysisContext context, Location location, string name, string summary) =>
        context.ReportDiagnostic(Diagnostic.Create(
            Rules.MissingDocumentation,
            location,
            ImmutableDictionary<string, string?>.Empty.Add(SummaryProperty, summary),
            name));

    private static bool HasDocumentation(SyntaxNode node) =>
        node.GetLeadingTrivia().Any(t => t.IsKind(SyntaxKind.SingleLineDocumentationCommentTrivia) || t.IsKind(SyntaxKind.MultiLineDocumentationCommentTrivia));

    private static string Language(SyntaxNodeAnalysisContext context)
    {
        var options = context.Options.AnalyzerConfigOptionsProvider.GetOptions(context.Node.SyntaxTree);
        return options.TryGetValue(LanguageOption, out var value) && string.Equals(value?.Trim(), DocumentationText.English, StringComparison.OrdinalIgnoreCase)
            ? DocumentationText.English
            : DocumentationText.Turkish;
    }
}
