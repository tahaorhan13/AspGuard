using System.Collections.Immutable;
using Microsoft.CodeAnalysis.CodeFixes;
using Microsoft.CodeAnalysis.CSharp.Testing;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Testing;

namespace AspGuard.Analyzers.Tests;

internal static class AnalyzerTestHelper
{
    public const string Prelude = """
        using System;
        using System.Collections.Generic;
        using System.Linq;
        using System.Threading.Tasks;
        using Microsoft.EntityFrameworkCore;

        public class Order { public int Id { get; set; } public string Name { get; set; } = ""; public List<Item> Items { get; set; } = new(); }
        public class Item { public int Id { get; set; } public int OrderId { get; set; } }
        public class AppDb : DbContext
        {
            public DbSet<Order> Orders => Set<Order>();
            public DbSet<Item> Items => Set<Item>();
        }

        """;

    private static readonly ReferenceAssemblies _references = ReferenceAssemblies.Net.Net80.AddPackages(
        ImmutableArray.Create(new PackageIdentity("Microsoft.EntityFrameworkCore.Relational", "8.0.32")));

    /// <summary>Wraps statements into a method with <c>db</c>, <c>ids</c> and <c>names</c> in scope.</summary>
    public static string InMethod(string statements, bool isAsync = false) =>
        Prelude + $$"""
        public class C
        {
            {{(isAsync ? "async Task" : "void")}} M(AppDb db, List<int> ids, List<string> names)
            {
        {{Indent(statements, 8)}}
            }
        }
        """;

    private static string Indent(string text, int spaces)
    {
        var padding = new string(' ', spaces);
        return string.Join("\n", text.Split('\n').Select(line => line.Length == 0 ? line : padding + line));
    }

    public static Task VerifyAsync<TAnalyzer>(string source, Dictionary<string, string>? editorConfig = null)
        where TAnalyzer : DiagnosticAnalyzer, new()
    {
        var test = new CSharpAnalyzerTest<TAnalyzer, DefaultVerifier>
        {
            TestCode = source,
            ReferenceAssemblies = _references,
        };
        AddEditorConfig(test, editorConfig);
        return test.RunAsync();
    }

    public static Task VerifyFixAsync<TAnalyzer, TFix>(string source, string fixedSource, int fixIndex = 0)
        where TAnalyzer : DiagnosticAnalyzer, new()
        where TFix : CodeFixProvider, new()
    {
        var test = new CSharpCodeFixTest<TAnalyzer, TFix, DefaultVerifier>
        {
            TestCode = source,
            FixedCode = fixedSource,
            ReferenceAssemblies = _references,
            CodeActionIndex = fixIndex,
        };
        return test.RunAsync();
    }

    private static void AddEditorConfig(AnalyzerTest<DefaultVerifier> test, Dictionary<string, string>? editorConfig)
    {
        if (editorConfig is null)
        {
            return;
        }

        var lines = "is_global = true\n" + string.Join("\n", editorConfig.Select(p => $"{p.Key} = {p.Value}"));
        test.TestState.AnalyzerConfigFiles.Add(("/.globalconfig", lines));
    }
}
