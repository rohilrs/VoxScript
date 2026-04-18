namespace VoxScript.Core.Dictionary;

public sealed class CommonWordList : ICommonWordList
{
    private readonly Lazy<HashSet<string>> _words;

    public CommonWordList(string filePath)
    {
        _words = new Lazy<HashSet<string>>(() =>
        {
            if (!File.Exists(filePath))
                return new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            var lines = File.ReadAllLines(filePath)
                .Where(l => !string.IsNullOrWhiteSpace(l))
                .Select(l => l.Trim());
            return new HashSet<string>(lines, StringComparer.OrdinalIgnoreCase);
        });
    }

    public bool Contains(string word)
    {
        if (string.IsNullOrEmpty(word)) return false;
        var words = _words.Value;
        if (words.Contains(word)) return true;

        // Check common inflected forms by stripping suffixes
        // Order matters: check longer suffixes first
        ReadOnlySpan<string> suffixes = ["ing", "ies", "tion", "es", "ed", "ly", "er", "s"];
        foreach (var suffix in suffixes)
        {
            if (word.Length > suffix.Length + 2 && word.EndsWith(suffix, StringComparison.OrdinalIgnoreCase))
            {
                var stem = word[..^suffix.Length];
                if (words.Contains(stem)) return true;

                // "ies" → base + "y" (e.g., "berries" → "berry")
                if (suffix == "ies" && words.Contains(stem + "y")) return true;

                // "es" → base + "e" (e.g., "oranges" → "orange")
                // "ed" → base + "e" (e.g., "created" → "create")
                if (suffix is "es" or "ed" && words.Contains(stem + "e")) return true;

                // "ing" → base + "e" (e.g., "making" → "make")
                if (suffix == "ing" && words.Contains(stem + "e")) return true;
            }
        }

        return false;
    }
}
