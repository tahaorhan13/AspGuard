using static AspGuard.Analyzers.Tests.AnalyzerTestHelper;

namespace AspGuard.Analyzers.Tests;

public class QueryInLoopAnalyzerTests
{
    private static Task Verify(string statements, bool isAsync = false) =>
        VerifyAsync<QueryInLoopAnalyzer>(InMethod(statements, isAsync));

    // ---- AG001: true positives ----

    [Fact]
    public Task Foreach_ToList() => Verify("""
        foreach (var id in ids) { var x = {|AG001:db.Items.Where(i => i.OrderId == id).ToList()|}; }
        """);

    [Fact]
    public Task Foreach_embedded_statement() => Verify("""
        foreach (var id in ids) _ = {|AG001:db.Items.Count()|};
        """);

    [Fact]
    public Task For_loop() => Verify("""
        for (var i = 0; i < ids.Count; i++) { var x = {|AG001:db.Orders.FirstOrDefault(o => o.Id == ids[i])|}; }
        """);

    [Fact]
    public Task While_loop() => Verify("""
        var n = 0;
        while (n < 3) { n++; _ = {|AG001:db.Orders.Any()|}; }
        """);

    [Fact]
    public Task Do_while_loop() => Verify("""
        var n = 0;
        do { n++; _ = {|AG001:db.Orders.Single(o => o.Id == n)|}; } while (n < 3);
        """);

    [Fact]
    public Task Async_queries_in_loop() => Verify("""
        foreach (var id in ids)
        {
            _ = await {|AG001:db.Orders.FirstOrDefaultAsync(o => o.Id == id)|};
            _ = await {|AG001:db.Items.Where(i => i.OrderId == id).ToListAsync()|};
            _ = await {|AG001:db.Items.CountAsync()|};
        }
        """, isAsync: true);

    [Fact]
    public Task Find_and_FindAsync_in_loop() => Verify("""
        foreach (var id in ids)
        {
            _ = {|AG001:db.Orders.Find(id)|};
            _ = await {|AG001:db.Orders.FindAsync(id)|};
            _ = {|AG001:db.Find<Order>(id)|};
        }
        """, isAsync: true);

    [Fact]
    public Task Query_through_a_local_variable_in_loop() => Verify("""
        var query = db.Orders.Where(o => o.Id > 0);
        foreach (var id in ids) { _ = {|AG001:query.ToList()|}; }
        """);

    [Fact]
    public Task Query_through_Set_in_loop() => Verify("""
        foreach (var id in ids) { _ = {|AG001:db.Set<Order>().ToArray()|}; }
        """);

    [Fact]
    public Task Nested_loops_report_once() => Verify("""
        foreach (var a in ids) foreach (var b in ids) { _ = {|AG001:db.Orders.Any()|}; }
        """);

    [Fact]
    public Task Condition_of_for_loop_runs_per_iteration() => Verify("""
        for (var i = 0; i < {|AG001:db.Orders.Count()|}; i++) { }
        """);

    [Fact]
    public Task AsEnumerable_chain_in_loop() => Verify("""
        foreach (var id in ids) { _ = {|AG001:db.Orders.AsEnumerable().Where(o => o.Id == id).ToList()|}; }
        """);

    // ---- AG001: false positives ----

    [Fact]
    public Task Query_outside_any_loop() => Verify("""
        var all = db.Orders.ToList();
        var count = db.Items.Count();
        """);

    [Fact]
    public Task Collection_expression_of_foreach_runs_once() => Verify("""
        foreach (var o in db.Orders.ToList()) { }
        """);

    [Fact]
    public Task Initializer_of_for_loop_runs_once() => Verify("""
        for (var i = db.Orders.Count(); i > 0; i--) { }
        """);

    [Fact]
    public Task Linq_to_objects_in_loop() => Verify("""
        var list = new List<int> { 1, 2, 3 };
        foreach (var id in ids)
        {
            _ = list.Where(x => x > id).ToList();
            _ = list.First();
            _ = list.Count();
            _ = names.ToArray();
            _ = Enumerable.Range(0, 3).Select(x => x).ToList();
        }
        """);

    [Fact]
    public Task Materialized_results_in_loop_are_in_memory() => Verify("""
        var all = db.Orders.ToList();
        IEnumerable<Order> lazy = db.Orders.ToList();
        foreach (var id in ids)
        {
            _ = all.Where(o => o.Id == id).ToList();
            _ = all.FirstOrDefault(o => o.Id == id);
            _ = lazy.Count();
        }
        """);

    [Fact]
    public Task Non_ef_queryable_in_loop() => Verify("""
        var q = new[] { 1, 2, 3 }.AsQueryable();
        foreach (var id in ids) { _ = q.ToList(); _ = q.Count(); }
        """);

    [Fact]
    public Task Expression_composition_without_execution_in_loop() => Verify("""
        foreach (var id in ids) { var q = db.Orders.Where(o => o.Id == id).Include(o => o.Items).AsNoTracking(); }
        """);

    [Fact]
    public Task Subquery_inside_queryable_select_is_translated_not_looped() => Verify("""
        _ = db.Orders.Select(o => new { o.Id, N = db.Items.Count(i => i.OrderId == o.Id) }).ToList();
        """);

    [Fact]
    public Task Lambda_that_is_not_a_linq_to_objects_host_is_ignored() => Verify("""
        foreach (var id in ids) { Func<int> f = () => db.Orders.Count(); Task.Run(() => db.Orders.ToList()); }
        """);

    [Fact]
    public Task Local_function_boundary_is_not_crossed() => Verify("""
        foreach (var id in ids) { int Count() => db.Orders.Count(); }
        """);

    [Fact]
    public async Task Types_named_like_ef_types_are_not_mistaken_for_ef()
    {
        await VerifyAsync<QueryInLoopAnalyzer>("""
            using System.Collections.Generic;
            using System.Linq;
            public class DbSet<T> : List<T> { public T Find(int id) => default!; }
            public class Fake { public DbSet<int> Numbers { get; } = new(); }
            public class C
            {
                void M(Fake fake, List<int> ids)
                {
                    foreach (var id in ids) { _ = fake.Numbers.Find(id); _ = fake.Numbers.ToList(); _ = fake.Numbers.FirstOrDefault(); }
                }
            }
            """);
    }

    [Fact]
    public async Task Compilations_without_ef_are_not_analyzed()
    {
        await VerifyAsync<QueryInLoopAnalyzer>("""
            using System.Linq;
            public class C { void M(int[] a) { foreach (var x in a) { _ = a.ToList(); } } }
            """);
    }

    // ---- AG002 ----

    [Fact]
    public Task Select_over_in_memory_collection() => Verify("""
        var result = ids.Select(id => {|AG002:db.Orders.Find(id)|}).ToList();
        """);

    [Fact]
    public Task Select_with_async_query() => Verify("""
        var tasks = ids.Select(id => {|AG002:db.Orders.FirstOrDefaultAsync(o => o.Id == id)|}).ToList();
        await Task.WhenAll(tasks);
        """, isAsync: true);

    [Fact]
    public Task SelectMany_Where_ForEach() => Verify("""
        _ = ids.SelectMany(id => {|AG002:db.Items.Where(i => i.OrderId == id).ToList()|}).ToList();
        _ = ids.Where(id => {|AG002:db.Orders.Any(o => o.Id == id)|}).ToList();
        ids.ForEach(id => {|AG002:db.Orders.Count()|});
        Array.ForEach(ids.ToArray(), id => {|AG002:db.Orders.Count()|});
        """);

    [Fact]
    public Task Select_over_AsEnumerable_of_a_query() => Verify("""
        _ = db.Orders.AsEnumerable().Select(o => {|AG002:db.Items.Count(i => i.OrderId == o.Id)|}).ToList();
        """);

    [Fact]
    public Task Select_with_block_lambda() => Verify("""
        _ = ids.Select(id => { var o = {|AG002:db.Orders.Single(x => x.Id == id)|}; return o.Name; }).ToList();
        """);

    [Fact]
    public Task Loop_inside_lambda_is_reported_as_loop() => Verify("""
        _ = ids.Select(id => { foreach (var n in names) { _ = {|AG001:db.Orders.Count()|}; } return id; }).ToList();
        """);

    [Fact]
    public Task Queryable_select_is_not_in_memory() => Verify("""
        _ = db.Orders.Select(o => o.Items.Count()).ToList();
        _ = db.Orders.Where(o => db.Items.Any(i => i.OrderId == o.Id)).ToList();
        """);

    [Fact]
    public Task Select_without_queries_is_fine() => Verify("""
        var all = db.Orders.ToList();
        _ = ids.Select(id => all.First(o => o.Id == id)).ToList();
        """);
}
