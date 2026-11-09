using static AspGuard.Analyzers.Tests.AnalyzerTestHelper;

namespace AspGuard.Analyzers.Tests;

public class RawSqlAnalyzerTests
{
    private static Task Verify(string statements, bool isAsync = false) =>
        VerifyAsync<RawSqlAnalyzer>(InMethod(statements, isAsync));

    // ---- AG010: true positives (not covered by EF1002) ----

    [Fact]
    public Task String_Format_into_FromSqlRaw() => Verify("""
        var id = ids[0];
        _ = {|AG010:db.Orders.FromSqlRaw(string.Format("SELECT * FROM Orders WHERE Id = {0}", id))|};
        """);

    [Fact]
    public Task String_Format_into_ExecuteSqlRaw_and_SqlQueryRaw() => Verify("""
        var name = names[0];
        {|AG010:db.Database.ExecuteSqlRaw(string.Format("DELETE FROM Orders WHERE Name = '{0}'", name))|};
        _ = {|AG010:db.Database.SqlQueryRaw<int>(string.Format("SELECT {0}", names[0]))|};
        """);

    [Fact]
    public Task Async_execute_with_string_Format() => Verify("""
        await {|AG010:db.Database.ExecuteSqlRawAsync(string.Format("DELETE FROM Orders WHERE Id = {0}", ids[0]))|};
        """, isAsync: true);

    [Fact]
    public Task Local_built_with_interpolation() => Verify("""
        var sql = $"SELECT * FROM Orders WHERE Name = '{names[0]}'";
        _ = {|AG010:db.Orders.FromSqlRaw(sql)|};
        """);

    [Fact]
    public Task Local_built_with_concatenation() => Verify("""
        var sql = "SELECT * FROM Orders WHERE Name = '" + names[0] + "'";
        _ = {|AG010:db.Orders.FromSqlRaw(sql)|};
        """);

    [Fact]
    public Task Local_extended_with_plus_equals() => Verify("""
        var sql = "SELECT * FROM Orders WHERE Name = ";
        sql += names[0];
        _ = {|AG010:db.Orders.FromSqlRaw(sql)|};
        """);

    [Fact]
    public Task Local_reassigned_to_a_formatted_string() => Verify("""
        var sql = "SELECT * FROM Orders";
        sql = string.Format("SELECT * FROM Orders WHERE Id = {0}", ids[0]);
        _ = {|AG010:db.Orders.FromSqlRaw(sql)|};
        """);

    [Fact]
    public Task StringBuilder_result() => Verify("""
        var sb = new System.Text.StringBuilder("SELECT * FROM Orders WHERE Name = ");
        sb.Append(names[0]);
        _ = {|AG010:db.Orders.FromSqlRaw(sb.ToString())|};
        """);

    [Fact]
    public Task Concat_and_Replace_with_runtime_values() => Verify("""
        _ = {|AG010:db.Orders.FromSqlRaw(string.Concat("SELECT * FROM Orders WHERE Name = ", names[0]))|};
        _ = {|AG010:db.Orders.FromSqlRaw("SELECT * FROM Orders WHERE Name = @n".Replace("@n", names[0]))|};
        """);

    [Fact]
    public Task Dynamic_string_even_when_parameters_are_also_passed() => Verify("""
        var sql = "SELECT * FROM Orders WHERE Id = " + ids[0] + " AND Name = {0}";
        _ = {|AG010:db.Orders.FromSqlRaw(sql, names[0])|};
        """);

    [Fact]
    public Task Conditional_with_a_dynamic_branch() => Verify("""
        var sql = ids.Count > 0 ? "SELECT * FROM Orders" : "SELECT * FROM Orders WHERE Name = " + names[0];
        _ = {|AG010:db.Orders.FromSqlRaw(sql)|};
        """);

    // ---- AG010: false positives ----

    [Fact]
    public Task Constant_sql() => Verify("""
        const string sql = "SELECT * FROM Orders";
        _ = db.Orders.FromSqlRaw("SELECT * FROM Orders");
        _ = db.Orders.FromSqlRaw(sql);
        _ = db.Orders.FromSqlRaw("SELECT * FROM " + "Orders");
        db.Database.ExecuteSqlRaw("DELETE FROM Orders");
        """);

    [Fact]
    public Task Parameterized_raw_sql() => Verify("""
        _ = db.Orders.FromSqlRaw("SELECT * FROM Orders WHERE Id = {0}", ids[0]);
        db.Database.ExecuteSqlRaw("DELETE FROM Orders WHERE Name = @p0", names[0]);
        """);

    [Fact]
    public Task Interpolated_FromSql_is_safe() => Verify("""
        _ = db.Orders.FromSql($"SELECT * FROM Orders WHERE Id = {ids[0]}");
        db.Database.ExecuteSql($"DELETE FROM Orders WHERE Name = {names[0]}");
        """);

    [Fact]
    public Task Direct_interpolation_and_concatenation_are_left_to_EF1002() => Verify("""
        _ = db.Orders.FromSqlRaw($"SELECT * FROM Orders WHERE Id = {ids[0]}");
        _ = db.Orders.FromSqlRaw("SELECT * FROM Orders WHERE Name = '" + names[0] + "'");
        """);

    [Fact]
    public Task Sql_that_is_only_passed_through_is_unknown() => RawSqlFromParameter();

    private static Task RawSqlFromParameter() => VerifyAsync<RawSqlAnalyzer>(Prelude + """
        public class P
        {
            void M(AppDb db, string sql) { _ = db.Orders.FromSqlRaw(sql); db.Database.ExecuteSqlRaw(sql); }
        }
        """);

    [Fact]
    public Task Formatting_of_constants_only() => Verify("""
        _ = db.Orders.FromSqlRaw(string.Format("SELECT * FROM Orders WHERE Id = {0}", 5));
        var sql = "SELECT * FROM Orders";
        _ = db.Orders.FromSqlRaw(sql);
        """);

    [Fact]
    public Task Non_ef_methods_with_the_same_name() => VerifyAsync<RawSqlAnalyzer>(Prelude + """
        public static class Fake { public static void FromSqlRaw(this string s, string sql) { } }
        public class P { void M(string x) { "a".FromSqlRaw("SELECT " + x); } }
        """);
}
