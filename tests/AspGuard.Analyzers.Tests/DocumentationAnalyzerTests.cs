using Microsoft.CodeAnalysis.CSharp.Testing;
using Microsoft.CodeAnalysis.Testing;

namespace AspGuard.Analyzers.Tests;

public class DocumentationAnalyzerTests
{
    private static Task Verify(string source, string? language = null)
    {
        var test = new CSharpAnalyzerTest<MissingDocumentationAnalyzer, DefaultVerifier> { TestCode = source };
        if (language is not null)
        {
            test.TestState.AnalyzerConfigFiles.Add(("/.globalconfig", $"is_global = true\naspguard.doc_language = {language}"));
        }

        return test.RunAsync();
    }

    private static Task Fix(string source, string fixedSource, string? language = null)
    {
        var test = new CSharpCodeFixTest<MissingDocumentationAnalyzer, DocumentationCodeFixProvider, DefaultVerifier>
        {
            TestCode = source,
            FixedCode = fixedSource,
        };
        if (language is not null)
        {
            test.TestState.AnalyzerConfigFiles.Add(("/.globalconfig", $"is_global = true\naspguard.doc_language = {language}"));
            test.FixedState.AnalyzerConfigFiles.Add(("/.globalconfig", $"is_global = true\naspguard.doc_language = {language}"));
        }

        return test.RunAsync();
    }

    [Fact]
    public Task Undocumented_methods_and_constructors_are_reported() => Verify("""
        public class PersonService
        {
            public {|AG030:PersonService|}() { }
            public void {|AG030:AddPerson|}() { }
            private int {|AG030:CountPeople|}() => 0;
            internal static void {|AG030:DeleteClaim|}(int id) { }
        }

        public interface IClaims
        {
            void {|AG030:ApproveClaim|}();
        }
        """);

    [Fact]
    public Task Documented_and_inherited_members_are_not_reported() => Verify("""
        using System;

        public abstract class Base
        {
            /// <summary>Exists.</summary>
            public abstract void Run();
        }

        /// <summary>Done.</summary>
        public partial class Worker : Base, IDisposable
        {
            /// <summary>Documented.</summary>
            public void AddPerson() { }

            /** <summary>Multi-line documented.</summary> */
            public void DeleteClaim() { }

            static Worker() { }

            /// <inheritdoc />
            public override void Run() { }

            void IDisposable.Dispose() { }

            partial void OnSaved();

            public void Local()
            {
                void Inner() { }
                Inner();
                Action lambda = () => { };
                lambda();
            }
        }
        """.Replace("public void Local()", "/// <summary>Has a local function.</summary>\n    public void Local()"));

    [Fact]
    public Task Fix_adds_a_turkish_summary_by_default() => Fix(
        """
        public class PersonService
        {
            public void {|AG030:AddPerson|}(string name) { }

            [System.Obsolete]
            public void {|AG030:DeleteClaim|}() { }
        }
        """,
        """
        public class PersonService
        {
            /// <summary>
            /// Kişi ekler.
            /// </summary>
            public void AddPerson(string name) { }

            /// <summary>
            /// Tazminat siler.
            /// </summary>
            [System.Obsolete]
            public void DeleteClaim() { }
        }
        """);

    [Fact]
    public Task Fix_uses_english_when_configured() => Fix(
        """
        public class Repository<T>
        {
            public {|AG030:Repository|}() { }

            public System.Threading.Tasks.Task {|AG030:GetUserByIdAsync|}(int id) => System.Threading.Tasks.Task.CompletedTask;
        }
        """,
        """
        public class Repository<T>
        {
            /// <summary>
            /// Initializes a new instance of the <see cref="Repository{T}"/> class.
            /// </summary>
            public Repository() { }

            /// <summary>
            /// Gets the user by id.
            /// </summary>
            public System.Threading.Tasks.Task GetUserByIdAsync(int id) => System.Threading.Tasks.Task.CompletedTask;
        }
        """,
        language: "en");

    [Fact]
    public Task Fix_keeps_existing_comments_above_the_member() => Fix(
        """
        public class Orders
        {
            // Called by the nightly job.
            public void {|AG030:ArchiveOrders|}() { }
        }
        """,
        """
        public class Orders
        {
            // Called by the nightly job.
            /// <summary>
            /// Siparişleri arşivler.
            /// </summary>
            public void ArchiveOrders() { }
        }
        """);
}
