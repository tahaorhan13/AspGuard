using System.Text;
using System.Text.RegularExpressions;

namespace AspGuard;

/// <summary>Turns SQL text into a "shape": literals and parameters become <c>?</c>, whitespace is collapsed, IN lists shrink to one element.</summary>
internal static partial class SqlNormalizer
{
    public static string Normalize(string sql)
    {
        var sb = new StringBuilder(sql.Length);
        var n = sql.Length;
        var i = 0;
        var pendingSpace = false;

        while (i < n)
        {
            var c = sql[i];

            if (char.IsWhiteSpace(c))
            {
                pendingSpace = true;
                i++;
                continue;
            }

            if (c == '-' && i + 1 < n && sql[i + 1] == '-')
            {
                while (i < n && sql[i] != '\n')
                {
                    i++;
                }

                pendingSpace = true;
                continue;
            }

            if (c == '/' && i + 1 < n && sql[i + 1] == '*')
            {
                var end = sql.IndexOf("*/", i + 2, StringComparison.Ordinal);
                i = end < 0 ? n : end + 2;
                pendingSpace = true;
                continue;
            }

            if (pendingSpace && sb.Length > 0)
            {
                sb.Append(' ');
            }

            pendingSpace = false;

            if (c == '\'')
            {
                i = SkipQuoted(sql, i, '\'');
                sb.Append('?');
            }
            else if ((c == 'N' || c == 'n') && i + 1 < n && sql[i + 1] == '\'' && !IsIdentifierChar(Last(sb)))
            {
                i = SkipQuoted(sql, i + 1, '\'');
                sb.Append('?');
            }
            else if (c == '"' || c == '`')
            {
                var end = SkipQuoted(sql, i, c);
                sb.Append(sql, i, end - i);
                i = end;
            }
            else if (c == '[')
            {
                var end = sql.IndexOf(']', i + 1);
                end = end < 0 ? n : end + 1;
                sb.Append(sql, i, end - i);
                i = end;
            }
            else if (IsParameterStart(sql, i))
            {
                i++;
                while (i < n && IsIdentifierChar(sql[i]))
                {
                    i++;
                }

                sb.Append('?');
            }
            else if (char.IsDigit(c) || (c == '.' && i + 1 < n && char.IsDigit(sql[i + 1]) && !IsIdentifierChar(Last(sb))))
            {
                i = SkipNumber(sql, i);
                sb.Append('?');
            }
            else if (IsIdentifierChar(c))
            {
                var start = i;
                while (i < n && IsIdentifierChar(sql[i]))
                {
                    i++;
                }

                sb.Append(sql, start, i - start);
            }
            else
            {
                sb.Append(c);
                i++;
            }
        }

        while (sb.Length > 0 && (sb[sb.Length - 1] == ';' || sb[sb.Length - 1] == ' '))
        {
            sb.Length--;
        }

        return InList().Replace(sb.ToString(), "$1 (?)");
    }

    public static bool IsQuery(string normalized)
    {
        var span = normalized.AsSpan().TrimStart('(').TrimStart();
        return span.StartsWith("SELECT", StringComparison.OrdinalIgnoreCase)
            || span.StartsWith("WITH", StringComparison.OrdinalIgnoreCase);
    }

    public static string Hash(string normalized)
    {
        var hash = 14695981039346656037UL;
        foreach (var ch in normalized)
        {
            hash = (hash ^ ch) * 1099511628211UL;
        }

        return hash.ToString("x16", System.Globalization.CultureInfo.InvariantCulture);
    }

    private static char Last(StringBuilder sb) => sb.Length == 0 ? ' ' : sb[sb.Length - 1];

    private static bool IsIdentifierChar(char c) => char.IsLetterOrDigit(c) || c == '_' || c == '$' || c == '#';

    private static bool IsParameterStart(string sql, int i)
    {
        var c = sql[i];
        if (i + 1 >= sql.Length)
        {
            return false;
        }

        var next = sql[i + 1];
        switch (c)
        {
            case '@':
                return next != '@' && (i == 0 || sql[i - 1] != '@') && IsIdentifierChar(next);
            case ':':
                return (i == 0 || sql[i - 1] != ':') && next != ':' && (char.IsLetter(next) || next == '_');
            case '$':
                return char.IsDigit(next);
            default:
                return false;
        }
    }

    private static int SkipQuoted(string sql, int start, char quote)
    {
        var i = start + 1;
        while (i < sql.Length)
        {
            if (sql[i] == quote)
            {
                if (i + 1 < sql.Length && sql[i + 1] == quote)
                {
                    i += 2;
                    continue;
                }

                return i + 1;
            }

            i++;
        }

        return sql.Length;
    }

    private static int SkipNumber(string sql, int start)
    {
        var i = start;
        var n = sql.Length;
        if (sql[i] == '0' && i + 1 < n && (sql[i + 1] == 'x' || sql[i + 1] == 'X'))
        {
            i += 2;
            while (i < n && Uri.IsHexDigit(sql[i]))
            {
                i++;
            }

            return i;
        }

        while (i < n && char.IsDigit(sql[i]))
        {
            i++;
        }

        if (i < n && sql[i] == '.')
        {
            i++;
            while (i < n && char.IsDigit(sql[i]))
            {
                i++;
            }
        }

        if (i < n && (sql[i] == 'e' || sql[i] == 'E'))
        {
            var j = i + 1;
            if (j < n && (sql[j] == '+' || sql[j] == '-'))
            {
                j++;
            }

            if (j < n && char.IsDigit(sql[j]))
            {
                while (j < n && char.IsDigit(sql[j]))
                {
                    j++;
                }

                i = j;
            }
        }

        return i;
    }

    private const string InListPattern = @"\b(IN)\s*\(\s*\?(?:\s*,\s*\?)*\s*\)";

#if NETFRAMEWORK
    private static readonly Regex InListRegex = new(InListPattern, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled);

    private static Regex InList() => InListRegex;
#else
    [GeneratedRegex(InListPattern, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex InList();
#endif
}
