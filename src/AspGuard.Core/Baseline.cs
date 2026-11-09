using System.Text.Json;

namespace AspGuard;

/// <summary>A JSON file of known findings. Findings in it are skipped so only new problems are reported.</summary>
internal sealed class Baseline(string path)
{
    private static readonly JsonSerializerOptions _json = new() { WriteIndented = true };
    private readonly object _gate = new();
    private Dictionary<string, Entry>? _entries;

    public bool Contains(AspGuardFinding finding)
    {
        lock (_gate)
        {
            return Load().ContainsKey(finding.Key);
        }
    }

    public void Add(IEnumerable<AspGuardFinding> findings)
    {
        lock (_gate)
        {
            var entries = Load();
            foreach (var finding in findings)
            {
                entries[finding.Key] = new Entry(finding.Key, finding.Kind.ToString(), finding.Shape, finding.CallSite, finding.Count);
            }

            var directory = Path.GetDirectoryName(Path.GetFullPath(path));
            if (!string.IsNullOrEmpty(directory))
            {
                Directory.CreateDirectory(directory);
            }

            var document = new Document(1, entries.Values.OrderBy(e => e.Key, StringComparer.Ordinal).ToList());
            var temp = path + ".tmp";
            File.WriteAllText(temp, JsonSerializer.Serialize(document, _json));
            File.Move(temp, path, overwrite: true);
        }
    }

    private Dictionary<string, Entry> Load()
    {
        if (_entries is not null)
        {
            return _entries;
        }

        _entries = new Dictionary<string, Entry>(StringComparer.Ordinal);
        if (File.Exists(path))
        {
            var document = JsonSerializer.Deserialize<Document>(File.ReadAllText(path));
            foreach (var entry in document?.Findings ?? [])
            {
                _entries[entry.Key] = entry;
            }
        }

        return _entries;
    }

    private sealed record Entry(string Key, string Kind, string? Shape, string? CallSite, int Count);

    private sealed record Document(int Version, List<Entry> Findings);
}
