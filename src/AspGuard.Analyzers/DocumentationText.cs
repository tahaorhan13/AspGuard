using System.Text;

namespace AspGuard.Analyzers;

/// <summary>
/// Rule-based XML documentation summaries from member names (no AI, no network): <c>AddPerson</c> → "Kişi ekler." /
/// "Adds the person.". The identifier is split into words, the first word is matched against a verb table and the rest
/// against a noun/adjective table; Turkish case suffixes follow vowel harmony. Unknown words are kept as written.
/// </summary>
internal static class DocumentationText
{
    public const string Turkish = "tr";
    public const string English = "en";

    private static readonly HashSet<string> _prepositions = new(StringComparer.OrdinalIgnoreCase) { "by", "for", "from", "to", "into", "with", "in", "of", "at" };

    /// <summary>Summary for a method name, already XML-escaped. Returns <c>null</c> when nothing sensible can be said.</summary>
    public static string? ForMethod(string name, string language)
    {
        var words = Split(name);
        if (words.Count > 1 && string.Equals(words[words.Count - 1], "Async", StringComparison.Ordinal))
        {
            words.RemoveAt(words.Count - 1);
        }

        if (words.Count == 0)
        {
            return null;
        }

        var text = language == English ? EnglishText.Method(words) : TurkishText.Method(words);
        return Escape(Capitalize(text, language) + ".");
    }

    /// <summary>Summary for a constructor; <paramref name="cref"/> is the type's cref (e.g. <c>Repository{T}</c>).</summary>
    public static string ForConstructor(string cref, string kind, string language)
    {
        var see = $"<see cref=\"{Escape(cref)}\"/>";
        if (language == English)
        {
            return $"Initializes a new instance of the {see} {kind}.";
        }

        var turkishKind = kind == "struct" ? "yapısının" : kind == "record" ? "kaydının" : "sınıfının";
        return $"{see} {turkishKind} yeni bir örneğini oluşturur.";
    }

    internal static List<string> Split(string identifier)
    {
        var words = new List<string>();
        var current = new StringBuilder();

        void Flush()
        {
            if (current.Length > 0)
            {
                words.Add(current.ToString());
                current.Clear();
            }
        }

        for (var i = 0; i < identifier.Length; i++)
        {
            var c = identifier[i];
            if (c == '_' || !char.IsLetterOrDigit(c))
            {
                Flush();
                continue;
            }

            if (current.Length > 0)
            {
                var previous = identifier[i - 1];
                var next = i + 1 < identifier.Length ? identifier[i + 1] : '\0';
                var lowerToUpper = char.IsUpper(c) && (char.IsLower(previous) || char.IsDigit(previous));
                var acronymEnd = char.IsUpper(c) && char.IsUpper(previous) && char.IsLower(next);
                if (lowerToUpper || acronymEnd)
                {
                    Flush();
                }
            }

            current.Append(c);
        }

        Flush();
        return words;
    }

    internal static bool IsAcronym(string word) => word.Length > 1 && word.All(c => char.IsUpper(c) || char.IsDigit(c));

    private static string Capitalize(string text, string language)
    {
        if (text.Length == 0)
        {
            return text;
        }

        var first = text[0];
        var upper = language == Turkish && first == 'i' ? 'İ' : char.ToUpperInvariant(first);
        return upper + text.Substring(1);
    }

    private static string Escape(string text) => text.Replace("&", "&amp;").Replace("<", "&lt;").Replace(">", "&gt;");

    /// <summary>Splits the words after the verb into the object and prepositional phrases (<c>ById</c>, <c>ToCart</c>...).</summary>
    internal static (List<string> Object, List<(string Preposition, List<string> Words)> Phrases) Parse(List<string> words, int start)
    {
        var obj = new List<string>();
        var phrases = new List<(string, List<string>)>();
        List<string>? target = obj;
        for (var i = start; i < words.Count; i++)
        {
            var word = words[i];
            if (_prepositions.Contains(word) && i + 1 < words.Count)
            {
                target = [];
                phrases.Add((word.ToLowerInvariant(), target));
                continue;
            }

            target.Add(word);
        }

        // "PriceOfProduct" reads as "product price".
        for (var i = phrases.Count - 1; i >= 0; i--)
        {
            if (phrases[i].Item1 == "of")
            {
                obj.InsertRange(0, phrases[i].Item2);
                phrases.RemoveAt(i);
            }
        }

        return (obj, phrases);
    }

    internal static bool IsVerb(string word) => TurkishText.Verbs.ContainsKey(word);
}

/// <summary>English summaries: the verb in third person plus the remaining words in lower case.</summary>
internal static class EnglishText
{
    public static string Method(List<string> words)
    {
        var first = words[0].ToLowerInvariant();
        var rest = words.Skip(1).ToList();

        switch (first)
        {
            case "is" when rest.Count > 0:
                return rest.Count == 1 ? $"determines whether it is {Lower(rest)}" : $"determines whether the {Lower(rest.Take(rest.Count - 1))} is {Lower(rest.Skip(rest.Count - 1))}";
            case "has" when rest.Count > 0:
                return $"determines whether it has {Lower(rest)}";
            case "can" or "should" when rest.Count > 0:
                return $"determines whether it {first} {VerbPhrase(rest)}";
            case "on" when rest.Count > 0:
                return $"handles the {Lower(rest)} event";
            case "try" when rest.Count > 0:
                return $"tries to {VerbPhrase(rest)}";
            case "to" when rest.Count > 0:
                return $"converts to {Lower(rest)}";
        }

        if (!DocumentationText.IsVerb(first))
        {
            return $"performs the {Lower(words)} operation";
        }

        // "AddOrUpdateUser" → "adds or updates the user".
        if (rest.Count >= 2 && rest[0].ToLowerInvariant() is "or" or "and" && DocumentationText.IsVerb(rest[1]))
        {
            var tail = rest.Skip(2).ToList();
            return $"{ThirdPerson(first)} {rest[0].ToLowerInvariant()} {ThirdPerson(rest[1].ToLowerInvariant())}{Object(tail)}";
        }

        return ThirdPerson(first) + Object(rest);
    }

    private static string VerbPhrase(List<string> words) =>
        DocumentationText.IsVerb(words[0]) ? words[0].ToLowerInvariant() + Object(words.Skip(1).ToList()) : Lower(words);

    private static string Object(List<string> words)
    {
        if (words.Count == 0)
        {
            return string.Empty;
        }

        var (obj, phrases) = DocumentationText.Parse(words, 0);
        var sb = new StringBuilder();
        if (obj.Count > 0)
        {
            sb.Append(obj[0].ToLowerInvariant() == "all" ? " " : " the ").Append(Lower(obj));
        }

        foreach (var (preposition, phraseWords) in phrases)
        {
            sb.Append(' ').Append(preposition).Append(' ').Append(Lower(phraseWords));
        }

        return sb.ToString();
    }

    private static string Lower(IEnumerable<string> words) =>
        string.Join(" ", words.Select(w => DocumentationText.IsAcronym(w) ? w : w.ToLowerInvariant()));

    internal static string ThirdPerson(string verb)
    {
        if (verb.EndsWith("s", StringComparison.Ordinal) || verb.EndsWith("x", StringComparison.Ordinal) || verb.EndsWith("z", StringComparison.Ordinal)
            || verb.EndsWith("ch", StringComparison.Ordinal) || verb.EndsWith("sh", StringComparison.Ordinal) || verb.EndsWith("o", StringComparison.Ordinal))
        {
            return verb + "es";
        }

        if (verb.Length > 1 && verb.EndsWith("y", StringComparison.Ordinal) && !"aeiou".Contains(verb[verb.Length - 2]))
        {
            return verb.Substring(0, verb.Length - 1) + "ies";
        }

        return verb + "s";
    }
}
