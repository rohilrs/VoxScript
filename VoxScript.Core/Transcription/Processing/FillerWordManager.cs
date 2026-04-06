using System.Text.RegularExpressions;

namespace VoxScript.Core.Transcription.Processing;

/// <summary>Removes common filler words (um, uh, etc.) from transcription output.</summary>
public sealed class FillerWordManager
{
    private static readonly HashSet<string> DefaultFillerWords = new(StringComparer.OrdinalIgnoreCase)
    {
        "um", "uh", "er", "ah", "like", "you know", "I mean",
        "sort of", "kind of", "basically", "actually", "literally",
    };

    private readonly HashSet<string> _fillerWords;

    public FillerWordManager() : this(DefaultFillerWords) { }

    public FillerWordManager(IEnumerable<string> fillerWords)
    {
        _fillerWords = new HashSet<string>(fillerWords, StringComparer.OrdinalIgnoreCase);
    }

    public IReadOnlySet<string> FillerWords => _fillerWords;

    public void Add(string word) => _fillerWords.Add(word);

    public void Remove(string word) => _fillerWords.Remove(word);

    public string RemoveFillers(string text)
    {
        if (string.IsNullOrWhiteSpace(text)) return string.Empty;

        // Process multi-word fillers first (longer phrases), then single words
        var ordered = _fillerWords.OrderByDescending(f => f.Length);

        foreach (var filler in ordered)
        {
            // Match whole word/phrase boundaries
            var pattern = $@"\b{Regex.Escape(filler)}\b[\s,]*";
            text = Regex.Replace(text, pattern, " ", RegexOptions.IgnoreCase);
        }

        // Clean up multiple spaces
        text = Regex.Replace(text.Trim(), @"\s{2,}", " ");

        return text;
    }
}
