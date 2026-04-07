using System.Text.RegularExpressions;

namespace VoxScript.Core.Dictionary;

public sealed partial class AutoVocabularyService : IAutoVocabularyService
{
    private readonly IVocabularyRepository _repo;
    private readonly ICommonWordList _commonWords;

    public AutoVocabularyService(IVocabularyRepository repo, ICommonWordList commonWords)
    {
        _repo = repo;
        _commonWords = commonWords;
    }

    public async Task ProcessTranscriptionAsync(string text, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(text)) return;

        var existingWords = await _repo.GetWordsAsync(ct);
        var existingSet = new HashSet<string>(existingWords, StringComparer.OrdinalIgnoreCase);

        var tokens = WordSplitRegex().Split(text)
            .Where(t => !string.IsNullOrWhiteSpace(t))
            .ToList();

        var added = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var token in tokens)
        {
            if (token.Length <= 1) continue;
            if (token.All(char.IsDigit)) continue;
            if (_commonWords.Contains(token)) continue;
            if (existingSet.Contains(token)) continue;
            if (!added.Add(token)) continue;

            await _repo.AddWordAsync(token, ct);
        }
    }

    [GeneratedRegex(@"[\s\p{P}]+", RegexOptions.Compiled)]
    private static partial Regex WordSplitRegex();
}
