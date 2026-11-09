using static AspGuard.Analyzers.Tests.AnalyzerTestHelper;

namespace AspGuard.Analyzers.Tests;

public class MaterializeThenFilterAnalyzerTests
{
    private static Task Verify(string statements, bool isAsync = false) =>
        VerifyAsync<MaterializeThenFilterAnalyzer>(InMethod(statements, isAsync));

    // ---- AG020: true positives ----

    [Fact]
    public Task ToList_then_Where() => Verify("""
        _ = {|AG020:db.Orders.ToList().Where(o => o.Id > 1)|}.ToList();
        """);

    [Fact]
    public Task ToArray_then_Select() => Verify("""
        _ = {|AG020:db.Orders.ToArray().Select(o => o.Name)|}.ToList();
        """);

    [Fact]
    public Task AsEnumerable_then_Where() => Verify("""
        _ = {|AG020:db.Orders.AsEnumerable().Where(o => o.Name.Length > 3)|}.ToList();
        """);

    [Fact]
    public Task Awaited_ToListAsync_then_Where() => Verify("""
        _ = {|AG020:(await db.Orders.ToListAsync()).Where(o => o.Id > 1)|}.ToList();
        """, isAsync: true);

    [Fact]
    public Task Partly_filtered_query_still_filters_the_rest_in_memory() => Verify("""
        _ = {|AG020:db.Orders.Where(o => o.Id > 0).Include(o => o.Items).ToList().Where(o => o.Name == "a")|}.ToList();
        """);

    // ---- AG020: false positives ----

    [Fact]
    public Task Filter_inside_the_query() => Verify("""
        _ = db.Orders.Where(o => o.Id > 1).ToList();
        _ = db.Orders.Select(o => o.Name).ToList();
        """);

    [Fact]
    public Task In_memory_lists_only() => Verify("""
        var list = new List<int> { 1, 2 };
        _ = list.ToList().Where(x => x > 1).ToList();
        _ = list.ToArray().Select(x => x).ToList();
        _ = list.AsEnumerable().Where(x => x > 1).ToList();
        """);

    [Fact]
    public Task Bounded_query_is_fine() => Verify("""
        _ = db.Orders.OrderBy(o => o.Id).Take(100).ToList().Where(o => o.Name.Length > 3).ToList();
        """);

    [Fact]
    public Task Materialized_once_and_filtered_later_through_a_variable() => Verify("""
        var all = db.Orders.ToList();
        _ = all.Where(o => o.Id > 1).ToList();
        """);

    [Fact]
    public Task Materialization_without_a_filter() => Verify("""
        _ = db.Orders.ToList().Count();
        _ = db.Orders.ToList().First();
        """);

    [Fact]
    public Task Non_ef_queryable_materialization() => Verify("""
        _ = new[] { 1, 2 }.AsQueryable().ToList().Where(x => x > 1).ToList();
        """);
}

public class BlockingCallAnalyzerTests
{
    private static Task Verify(string statements, bool isAsync = false) =>
        VerifyAsync<BlockingCallAnalyzer>(InMethod(statements, isAsync));

    // ---- AG021: true positives ----

    [Fact]
    public Task Result_on_a_query() => Verify("""
        _ = {|AG021:db.Orders.ToListAsync().Result|};
        _ = {|AG021:db.Orders.CountAsync().Result|};
        _ = {|AG021:db.Orders.Where(o => o.Id > 1).FirstOrDefaultAsync().Result|};
        """);

    [Fact]
    public Task Wait_on_SaveChanges_and_queries() => Verify("""
        {|AG021:db.SaveChangesAsync().Wait()|};
        {|AG021:db.Orders.AnyAsync().Wait()|};
        """);

    [Fact]
    public Task GetAwaiter_GetResult() => Verify("""
        _ = {|AG021:db.Orders.ToListAsync().GetAwaiter().GetResult()|};
        {|AG021:db.SaveChangesAsync().GetAwaiter().GetResult()|};
        """);

    [Fact]
    public Task FindAsync_Result_and_raw_sql() => Verify("""
        _ = {|AG021:db.Orders.FindAsync(1).Result|};
        _ = {|AG021:db.Database.ExecuteSqlRawAsync("DELETE FROM Orders").Result|};
        """);

    // ---- AG021: false positives ----

    [Fact]
    public Task Awaited_calls() => Verify("""
        _ = await db.Orders.ToListAsync();
        await db.SaveChangesAsync();
        """, isAsync: true);

    [Fact]
    public Task Blocking_on_unrelated_tasks() => Verify("""
        Task.Delay(1).Wait();
        _ = Task.FromResult(1).Result;
        _ = Task.Run(() => 1).GetAwaiter().GetResult();
        """);

    [Fact]
    public Task Task_stored_in_a_variable_is_not_traced() => Verify("""
        var task = db.Orders.ToListAsync();
        _ = task.Result;
        """);

    [Fact]
    public Task Result_property_of_a_user_type() => VerifyAsync<BlockingCallAnalyzer>(Prelude + """
        public class Box { public int Result => 1; public void Wait() { } }
        public class P { void M(AppDb db) { var b = new Box(); _ = b.Result; b.Wait(); } }
        """);
}
