namespace AspGuard.Core.Tests;

public class SqlNormalizerTests
{
    [Theory]
    [InlineData("SELECT * FROM t WHERE id = 5", "SELECT * FROM t WHERE id = ?")]
    [InlineData("SELECT * FROM t LIMIT 1 OFFSET 20", "SELECT * FROM t LIMIT ? OFFSET ?")]
    [InlineData("SELECT 'a''b' FROM t", "SELECT ? FROM t")]
    [InlineData("SELECT N'abc' FROM t", "SELECT ? FROM t")]
    [InlineData("WHERE a IN (1, 2, 3)", "WHERE a IN (?)")]
    [InlineData("WHERE a IN ( 1 )", "WHERE a IN (?)")]
    [InlineData("WHERE a in (@p0, @p1, @p2)", "WHERE a in (?)")]
    [InlineData("SELECT\n  x\r\n FROM   t  ;", "SELECT x FROM t")]
    [InlineData("SELECT t0.Id1 FROM Table2 AS t0", "SELECT t0.Id1 FROM Table2 AS t0")]
    [InlineData("SELECT \"Id1\", [Name 2], `c3` FROM t", "SELECT \"Id1\", [Name 2], `c3` FROM t")]
    [InlineData("-- tag\nSELECT 1", "SELECT ?")]
    [InlineData("/* c */ SELECT 1.5e3, 0x1F, .5", "SELECT ?, ?, ?")]
    [InlineData("WHERE x = $1 AND y = :name AND z = @@ROWCOUNT", "WHERE x = ? AND y = ? AND z = @@ROWCOUNT")]
    [InlineData("SELECT a::int FROM t", "SELECT a::int FROM t")]
    [InlineData("WHERE Name = @__name_0 AND Age > @p1", "WHERE Name = ? AND Age > ?")]
    public void Normalize_produces_expected_shape(string sql, string expected)
    {
        Assert.Equal(expected, SqlNormalizer.Normalize(sql));
    }

    [Fact]
    public void Queries_differing_only_in_values_share_a_shape()
    {
        var a = SqlNormalizer.Normalize("SELECT * FROM Posts WHERE BlogId = 1 AND Title = 'x'");
        var b = SqlNormalizer.Normalize("SELECT * FROM Posts WHERE BlogId = 22 AND Title = 'a longer value'");
        Assert.Equal(a, b);
        Assert.Equal(SqlNormalizer.Hash(a), SqlNormalizer.Hash(b));
    }

    [Fact]
    public void Different_queries_have_different_hashes()
    {
        Assert.NotEqual(
            SqlNormalizer.Hash(SqlNormalizer.Normalize("SELECT * FROM A WHERE x = 1")),
            SqlNormalizer.Hash(SqlNormalizer.Normalize("SELECT * FROM B WHERE x = 1")));
    }

    [Theory]
    [InlineData("SELECT 1", true)]
    [InlineData("  select 1", true)]
    [InlineData("WITH c AS (SELECT 1) SELECT * FROM c", true)]
    [InlineData("(SELECT 1) UNION (SELECT 2)", true)]
    [InlineData("INSERT INTO t VALUES (1)", false)]
    [InlineData("UPDATE t SET a = 1", false)]
    [InlineData("DELETE FROM t", false)]
    public void IsQuery_detects_reads(string sql, bool expected)
    {
        Assert.Equal(expected, SqlNormalizer.IsQuery(sql));
    }
}
