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

    public bool Contains(string word) =>
        !string.IsNullOrEmpty(word) && _words.Value.Contains(word);
}
