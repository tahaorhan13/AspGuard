using System.Collections.Immutable;
using Microsoft.CodeAnalysis.CodeFixes;
using Microsoft.CodeAnalysis.CSharp.Testing;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Testing;

namespace AspGuard.Analyzers.Tests;

/// <summary>The same rules on Entity Framework 6 (System.Data.Entity) code targeting .NET Framework 4.8.</summary>
public class Ef6AnalyzerTests
{
    private const string Prelude = """
        using System;
        using System.Collections.Generic;
        using System.Data.Entity;
        using System.Linq;
        using System.Threading.Tasks;

        public class Order { public int Id { get; set; } public string Name { get; set; } public virtual List<Item> Items { get; set; } }
        public class Item { public int Id { get; set; } public int OrderId { get; set; } }
        public class AppDb : DbContext
        {
            public DbSet<Order> Orders { get; set; }
            public IDbSet<Item> Items { get; set; }
        }

        """;

    private static readonly ReferenceAssemblies _references = ReferenceAssemblies.NetFramework.Net48.Default.AddPackages(
        ImmutableArray.Create(new PackageIdentity("EntityFramework", "6.5.2")));

    private static string InMethod(string statements, bool isAsync = false) =>
        Prelude + $$"""
        public class C
        {
            {{(isAsync ? "async Task" : "void")}} M(AppDb db, List<int> ids, List<string> names)
            {
                {{statements.Replace("\n", "\n        ")}}
            }
        }
        """;

    private static Task Verify<TAnalyzer>(string statements, bool isAsync = false)
        where TAnalyzer : DiagnosticAnalyzer, new() =>
        new CSharpAnalyzerTest<TAnalyzer, DefaultVerifier> { TestCode = InMethod(statements, isAsync), ReferenceAssemblies = _references }.RunAsync();

    private static Task Fix<TAnalyzer, TFix>(string before, string after)
        where TAnalyzer : DiagnosticAnalyzer, new()
        where TFix : CodeFixProvider, new() =>
        new CSharpCodeFixTest<TAnalyzer, TFix, DefaultVerifier>
        {
            TestCode = InMethod(before),
            FixedCode = InMethod(after),
            ReferenceAssemblies = _references,
        }.RunAsync();

    // AG001: queries in loops.
    [Fact]
    public Task Query_in_foreach_is_reported() => Verify<QueryInLoopAnalyzer>("""
        foreach (var id in ids)
        {
            var order = {|AG001:db.Orders.FirstOrDefault(o => o.Id == id)|};
        }
        """);

    [Fact]
    public Task Async_query_and_Find_in_loops_are_reported() => Verify<QueryInLoopAnalyzer>(
        """
        foreach (var id in ids)
        {
            var items = await {|AG001:db.Orders.Where(o => o.Id == id).ToListAsync()|};
            var found = {|AG001:db.Orders.Find(id)|};
        }
        """,
        isAsync: true);

    [Fact]
    public Task Query_through_an_IDbSet_property_is_reported() => Verify<QueryInLoopAnalyzer>("""
        for (var i = 0; i < ids.Count; i++)
        {
            var count = {|AG001:db.Items.Count(x => x.OrderId == ids[i])|};
        }
        """);

    [Fact]
    public Task In_memory_list_in_loop_is_not_reported() => Verify<QueryInLoopAnalyzer>("""
        var orders = db.Orders.ToList();
        foreach (var id in ids)
        {
            var order = orders.FirstOrDefault(o => o.Id == id);
            var any = names.Any(n => n.Length == id);
        }
        """);

    [Fact]
    public Task Query_before_the_loop_and_composition_inside_are_not_reported() => Verify<QueryInLoopAnalyzer>("""
        var all = db.Orders.Include(o => o.Items).ToList();
        foreach (var order in db.Orders.AsNoTracking())
        {
            var query = db.Orders.Where(o => o.Id == order.Id);
        }
        """);

    // AG002: queries in in-memory lambdas.
    [Fact]
    public Task Query_inside_select_over_a_list_is_reported() => Verify<QueryInLoopAnalyzer>("""
        var orders = ids.Select(id => {|AG002:db.Orders.Find(id)|}).ToList();
        """);

    [Fact]
    public Task Select_inside_a_query_is_not_reported() => Verify<QueryInLoopAnalyzer>("""
        var orders = db.Orders.Select(o => new { o.Id, Count = o.Items.Count() }).ToList();
        """);

    // AG010: raw SQL.
    [Fact]
    public Task String_Format_in_SqlQuery_is_reported() => Verify<RawSqlAnalyzer>("""
        var orders = {|AG010:db.Database.SqlQuery<Order>(string.Format("SELECT * FROM Orders WHERE Id = {0}", ids[0]))|}.ToList();
        """);

    [Fact]
    public Task Direct_interpolation_and_concatenation_are_reported_because_EF6_has_no_EF1002() => Verify<RawSqlAnalyzer>("""
        {|AG010:db.Database.ExecuteSqlCommand($"DELETE FROM Orders WHERE Name = '{names[0]}'")|};
        var orders = {|AG010:db.Orders.SqlQuery("SELECT * FROM Orders WHERE Name = '" + names[0] + "'")|}.ToList();
        """);

    [Fact]
    public Task Parameterized_and_constant_sql_is_not_reported() => Verify<RawSqlAnalyzer>("""
        const string table = "Orders";
        var a = db.Database.SqlQuery<int>("SELECT Id FROM Orders WHERE Id = {0}", ids[0]).ToList();
        var b = db.Database.SqlQuery<int>("SELECT Id FROM " + table).ToList();
        db.Database.ExecuteSqlCommand("DELETE FROM Orders WHERE Id = @p0", ids[0]);
        """);

    [Fact]
    public Task Unrelated_SqlQuery_method_is_not_reported() => Verify<RawSqlAnalyzer>("""
        var raw = new Raw();
        raw.SqlQuery(string.Format("SELECT {0}", ids[0]));
        }

        class Raw
        {
            public void SqlQuery(string sql) { }
        """);

    // AG020 and AG021.
    [Fact]
    public Task Filtering_after_ToList_is_reported() => Verify<MaterializeThenFilterAnalyzer>("""
        var big = {|AG020:db.Orders.ToList().Where(o => o.Id > 10)|}.ToList();
        """);

    [Fact]
    public Task Blocking_on_async_calls_is_reported() => Verify<BlockingCallAnalyzer>("""
        var orders = {|AG021:db.Orders.ToListAsync().Result|};
        {|AG021:db.SaveChangesAsync().Wait()|};
        """);

    [Fact]
    public Task Blocking_on_unrelated_tasks_is_not_reported() => Verify<BlockingCallAnalyzer>("""
        var n = Task.FromResult(1).Result;
        Task.Delay(1).Wait();
        """);

    // Code fixes.
    [Fact]
    public Task String_Format_becomes_EF6_placeholders_with_parameters() => Fix<RawSqlAnalyzer, RawSqlCodeFixProvider>(
        """
        var orders = {|AG010:db.Database.SqlQuery<Order>(string.Format("SELECT * FROM Orders WHERE Id = {0} AND Name = {1}", ids[0], names[0]))|}.ToList();
        """,
        """
        var orders = db.Database.SqlQuery<Order>("SELECT * FROM Orders WHERE Id = {0} AND Name = {1}", ids[0], names[0]).ToList();
        """);

    [Fact]
    public Task Loop_lookup_becomes_one_query_with_a_lookup() => Fix<QueryInLoopAnalyzer, QueryInLoopCodeFixProvider>(
        """
        foreach (var id in ids)
        {
            var order = {|AG001:db.Orders.FirstOrDefault(o => o.Id == id)|};
        }
        """,
        """
        var ordersById = db.Orders.Where(o => ids.Contains(o.Id)).ToLookup(o => o.Id);
        foreach (var id in ids)
        {
            var order = ordersById[id].FirstOrDefault();
        }
        """);
}
