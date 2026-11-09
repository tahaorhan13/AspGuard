using static AspGuard.Analyzers.Tests.AnalyzerTestHelper;

namespace AspGuard.Analyzers.Tests;

public class RawSqlCodeFixTests
{
    private static Task Fix(string before, string after) =>
        VerifyFixAsync<RawSqlAnalyzer, RawSqlCodeFixProvider>(InMethod(before), InMethod(after));

    [Fact]
    public Task FromSqlRaw_with_string_Format_becomes_interpolated_FromSql() => Fix(
        """
        var id = ids[0];
        var name = names[0];
        _ = {|AG010:db.Orders.FromSqlRaw(string.Format("SELECT * FROM Orders WHERE Id = {0} AND Name = {1}", id, name))|};
        """,
        """
        var id = ids[0];
        var name = names[0];
        _ = db.Orders.FromSql($"SELECT * FROM Orders WHERE Id = {id} AND Name = {name}");
        """);

    [Fact]
    public Task ExecuteSqlRaw_becomes_ExecuteSql() => Fix(
        """
        {|AG010:db.Database.ExecuteSqlRaw(string.Format("DELETE FROM Orders WHERE Id = {0}", ids[0]))|};
        """,
        """
        db.Database.ExecuteSql($"DELETE FROM Orders WHERE Id = {ids[0]}");
        """);

    [Fact]
    public Task SqlQueryRaw_keeps_its_type_argument() => Fix(
        """
        _ = {|AG010:db.Database.SqlQueryRaw<int>(string.Format("SELECT Id FROM Orders WHERE Id = {0}", ids[0]))|};
        """,
        """
        _ = db.Database.SqlQuery<int>($"SELECT Id FROM Orders WHERE Id = {ids[0]}");
        """);

    [Fact]
    public Task Reused_placeholder_with_a_simple_value() => Fix(
        """
        var id = ids[0];
        _ = {|AG010:db.Orders.FromSqlRaw(string.Format("SELECT * FROM Orders WHERE Id = {0} OR Id = {0}", id))|};
        """,
        """
        var id = ids[0];
        _ = db.Orders.FromSql($"SELECT * FROM Orders WHERE Id = {id} OR Id = {id}");
        """);

    [Fact]
    public Task Awkward_values_are_parenthesized() => Fix(
        """
        _ = {|AG010:db.Orders.FromSqlRaw(string.Format("SELECT * FROM Orders WHERE Id = {0}", ids.Count > 0 ? ids[0] : 0))|};
        """,
        """
        _ = db.Orders.FromSql($"SELECT * FROM Orders WHERE Id = {(ids.Count > 0 ? ids[0] : 0)}");
        """);

    [Theory]
    [InlineData("""_ = {|AG010:db.Orders.FromSqlRaw(string.Format("SELECT * FROM Orders WHERE Name = '{0}'", names[0]))|};""")]
    [InlineData("""_ = {|AG010:db.Orders.FromSqlRaw(string.Format("SELECT * FROM Orders WHERE Id = {0:D5}", ids[0]))|};""")]
    [InlineData("""_ = {|AG010:db.Orders.FromSqlRaw(string.Format("SELECT * FROM Orders WHERE Id = {0,5}", ids[0]))|};""")]
    [InlineData("""_ = {|AG010:db.Orders.FromSqlRaw(string.Format("SELECT '{{' FROM Orders WHERE Id = {0}", ids[0]))|};""")]
    [InlineData("""_ = {|AG010:db.Orders.FromSqlRaw(string.Format("SELECT * FROM Orders WHERE Id = {0}", ids[0]), 1)|};""")]
    [InlineData("""var f = "SELECT {0}"; _ = {|AG010:db.Orders.FromSqlRaw(string.Format(f, ids[0]))|};""")]
    [InlineData("""var sql = $"SELECT * FROM Orders WHERE Id = {ids[0]}"; _ = {|AG010:db.Orders.FromSqlRaw(sql)|};""")]
    public Task Unsafe_rewrites_are_not_offered(string statement) => Fix(statement, statement);
}

public class QueryInLoopCodeFixTests
{
    private static Task Fix(string before, string after, bool isAsync = false) =>
        VerifyFixAsync<QueryInLoopAnalyzer, QueryInLoopCodeFixProvider>(InMethod(before, isAsync), InMethod(after, isAsync));

    [Fact]
    public Task FirstOrDefault_by_key_becomes_one_query_and_a_lookup() => Fix(
        """
        foreach (var id in ids)
        {
            var order = {|AG001:db.Orders.FirstOrDefault(x => x.Id == id)|};
            Console.WriteLine(order);
        }
        """,
        """
        var ordersById = db.Orders.Where(x => ids.Contains(x.Id)).ToLookup(x => x.Id);
        foreach (var id in ids)
        {
            var order = ordersById[id].FirstOrDefault();
            Console.WriteLine(order);
        }
        """);

    [Fact]
    public Task SingleOrDefault_keeps_its_semantics() => Fix(
        """
        foreach (var id in ids)
        {
            var order = {|AG001:db.Orders.SingleOrDefault(x => x.Id == id)|};
        }
        """,
        """
        var ordersById = db.Orders.Where(x => ids.Contains(x.Id)).ToLookup(x => x.Id);
        foreach (var id in ids)
        {
            var order = ordersById[id].SingleOrDefault();
        }
        """);

    [Fact]
    public Task Async_version_awaits_one_ToListAsync() => Fix(
        """
        foreach (var id in ids)
        {
            var order = await {|AG001:db.Orders.FirstOrDefaultAsync(x => x.Id == id)|};
        }
        """,
        """
        var ordersById = (await db.Orders.Where(x => ids.Contains(x.Id)).ToListAsync()).ToLookup(x => x.Id);
        foreach (var id in ids)
        {
            var order = ordersById[id].FirstOrDefault();
        }
        """,
        isAsync: true);

    [Fact]
    public Task Operands_in_either_order() => Fix(
        """
        foreach (var id in ids)
        {
            var order = {|AG001:db.Orders.FirstOrDefault(x => id == x.Id)|};
        }
        """,
        """
        var ordersById = db.Orders.Where(x => ids.Contains(x.Id)).ToLookup(x => x.Id);
        foreach (var id in ids)
        {
            var order = ordersById[id].FirstOrDefault();
        }
        """);

    [Fact]
    public Task Name_does_not_clash_with_existing_identifiers() => Fix(
        """
        var ordersById = 1;
        foreach (var id in ids)
        {
            var order = {|AG001:db.Orders.FirstOrDefault(x => x.Id == id)|};
        }
        """,
        """
        var ordersById = 1;
        var ordersById2 = db.Orders.Where(x => ids.Contains(x.Id)).ToLookup(x => x.Id);
        foreach (var id in ids)
        {
            var order = ordersById2[id].FirstOrDefault();
        }
        """);

    [Fact]
    public async Task Adds_System_Linq_when_the_file_lacks_it()
    {
        const string Before = """
            using System.Collections.Generic;
            using System.Threading.Tasks;
            using Microsoft.EntityFrameworkCore;

            public class Order { public int Id { get; set; } }
            public class AppDb : DbContext { public DbSet<Order> Orders => Set<Order>(); }
            public class C
            {
                async Task M(AppDb db, List<int> ids)
                {
                    foreach (var id in ids)
                    {
                        var order = await {|AG001:db.Orders.FirstOrDefaultAsync(x => x.Id == id)|};
                    }
                }
            }
            """;
        const string After = """
            using System.Collections.Generic;
            using System.Threading.Tasks;
            using Microsoft.EntityFrameworkCore;
            using System.Linq;

            public class Order { public int Id { get; set; } }
            public class AppDb : DbContext { public DbSet<Order> Orders => Set<Order>(); }
            public class C
            {
                async Task M(AppDb db, List<int> ids)
                {
                    var ordersById = (await db.Orders.Where(x => ids.Contains(x.Id)).ToListAsync()).ToLookup(x => x.Id);
                    foreach (var id in ids)
                    {
                        var order = ordersById[id].FirstOrDefault();
                    }
                }
            }
            """;
        await VerifyFixAsync<QueryInLoopAnalyzer, QueryInLoopCodeFixProvider>(Before, After);
    }

    [Theory]
    [InlineData("""foreach (var id in ids) { _ = {|AG001:db.Orders.Find(id)|}; }""")]
    [InlineData("""foreach (var id in ids) { _ = {|AG001:db.Orders.FirstOrDefault(x => x.Id == id && x.Name != "")|}; }""")]
    [InlineData("""foreach (var id in ids) { _ = {|AG001:db.Orders.FirstOrDefault(x => x.Id > id)|}; }""")]
    [InlineData("""foreach (var id in ids) { _ = {|AG001:db.Orders.Where(x => x.Id == id).FirstOrDefault()|}; }""")]
    [InlineData("""foreach (var id in ids) { _ = {|AG001:db.Orders.First(x => x.Id == id)|}; }""")]
    [InlineData("""foreach (var id in ids.ToList()) { _ = {|AG001:db.Orders.FirstOrDefault(x => x.Id == id)|}; }""")]
    [InlineData("""foreach (var id in ids) { _ = {|AG001:db.Orders.Include(x => x.Items).FirstOrDefault(x => x.Id == id)|}; }""")]
    [InlineData("""for (var i = 0; i < ids.Count; i++) { _ = {|AG001:db.Orders.FirstOrDefault(x => x.Id == ids[i])|}; }""")]
    public Task Shapes_that_cannot_be_rewritten_safely_get_no_fix(string statement) => Fix(statement, statement);
}
