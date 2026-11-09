using System.Collections.Concurrent;

namespace AspGuard;

internal sealed class ShapeInfo
{
    private ShapeInfo(string normalized)
    {
        Normalized = normalized;
        Id = SqlNormalizer.Hash(normalized);
        IsQuery = SqlNormalizer.IsQuery(normalized);
    }

    public string Normalized { get; }

    public string Id { get; }

    public bool IsQuery { get; }

    /// <summary>Normalizing allocates; EF produces the same command text for the same LINQ query, so cache by raw text.</summary>
    internal static class Cache
    {
        private const int MaxEntries = 4096;
        private static readonly ConcurrentDictionary<string, ShapeInfo> _entries = new(StringComparer.Ordinal);

        public static ShapeInfo Get(string commandText)
        {
            if (_entries.TryGetValue(commandText, out var cached))
            {
                return cached;
            }

            if (_entries.Count >= MaxEntries)
            {
                _entries.Clear();
            }

            var info = new ShapeInfo(SqlNormalizer.Normalize(commandText));
            _entries[commandText] = info;
            return info;
        }
    }
}
