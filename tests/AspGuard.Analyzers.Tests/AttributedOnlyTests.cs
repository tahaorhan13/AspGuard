using static AspGuard.Analyzers.Tests.AnalyzerTestHelper;

namespace AspGuard.Analyzers.Tests;

public class AttributedOnlyTests
{
    private const string Attribute = """
        namespace AspGuard { [System.AttributeUsage(System.AttributeTargets.Class | System.AttributeTargets.Method)] public sealed class GuardQueriesAttribute : System.Attribute { } }

        """;

    private static readonly Dictionary<string, string> _attributedOnly = new() { ["aspguard.scope"] = "attributed_only" };

    private static string Source(string classes) => Prelude + Attribute + classes;

    private const string Classes = """
        public class Plain
        {
            void M(AppDb db, List<int> ids) { foreach (var id in ids) { _ = db.Orders.Find(id); } }
        }

        [AspGuard.GuardQueries]
        public class MarkedClass
        {
            void M(AppDb db, List<int> ids) { foreach (var id in ids) { _ = {|AG001:db.Orders.Find(id)|}; } }
            void Lambda(AppDb db, List<int> ids) { _ = ids.Select(id => {|AG002:db.Orders.Find(id)|}).ToList(); }
        }

        public class MarkedMethod
        {
            [AspGuard.GuardQueries]
            void Marked(AppDb db, List<int> ids) { foreach (var id in ids) { _ = {|AG001:db.Orders.Find(id)|}; } }

            void Unmarked(AppDb db, List<int> ids) { foreach (var id in ids) { _ = db.Orders.Find(id); } }
        }
        """;

    [Fact]
    public Task Only_marked_code_is_checked_in_attributed_only_mode() =>
        VerifyAsync<QueryInLoopAnalyzer>(Source(Classes), _attributedOnly);

    [Fact]
    public Task Everything_is_checked_without_the_setting() =>
        VerifyAsync<QueryInLoopAnalyzer>(Source("""
            public class Plain
            {
                void M(AppDb db, List<int> ids) { foreach (var id in ids) { _ = {|AG001:db.Orders.Find(id)|}; } }
            }
            """));

    [Fact]
    public Task Other_values_of_the_setting_check_everything() =>
        VerifyAsync<QueryInLoopAnalyzer>(
            Source("""
                public class Plain
                {
                    void M(AppDb db, List<int> ids) { foreach (var id in ids) { _ = {|AG001:db.Orders.Find(id)|}; } }
                }
                """),
            new Dictionary<string, string> { ["aspguard.scope"] = "all_services" });
}
