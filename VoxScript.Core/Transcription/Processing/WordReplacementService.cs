using System.Text.RegularExpressions;
using VoxScript.Core.Dictionary;

namespace VoxScript.Core.Transcription.Processing;

public sealed partial class WordReplacementService
{
    private readonly IWordReplacementRepository _repo;
    private readonly IVocabularyRepository _vocab;

    public WordReplacementService(IWordReplacementRepository repo, IVocabularyRepository vocab)
    {
        _repo = repo;
        _vocab = vocab;
    }

    public async Task<string> ApplyAsync(string text, CancellationToken ct)
    {
        // Step 1: Explicit word replacement rules
        var replacements = await _repo.GetAllAsync(ct);
        foreach (var r in replacements)
        {
            var comparison = r.CaseSensitive
                ? StringComparison.Ordinal
                : StringComparison.OrdinalIgnoreCase;
            text = ReplaceWholeWord(text, r.Original, r.Replacement, comparison);
        }

        // Step 2: Implicit vocabulary correction — fuzzy match words against vocabulary
        try
        {
            var vocabWords = await _vocab.GetWordsAsync(ct);
            if (vocabWords.Count > 0)
                text = ApplyVocabularyCorrections(text, vocabWords);
        }
        catch
        {
            // Non-critical — explicit replacements already applied
        }

        return text;
    }

    /// <summary>
    /// For each word in the text, if it's not an exact match to a vocabulary word
    /// but is a close fuzzy match (same first letter, edit distance ≤ 2, length ≥ 4),
    /// replace it with the vocabulary word.
    /// </summary>
    private static string ApplyVocabularyCorrections(string text, IReadOnlyList<string> vocabWords)
    {
        // Build a lookup for fast exact-match checking
        var vocabSet = new HashSet<string>(vocabWords, StringComparer.OrdinalIgnoreCase);

        return WordTokenRegex().Replace(text, match =>
        {
            var word = match.Value;

            // Skip short words
            if (word.Length < 4) return word;

            // Skip if already an exact vocabulary match
            if (vocabSet.Contains(word)) return word;

            // Find the best fuzzy match
            string? bestMatch = null;
            int bestDistance = int.MaxValue;

            foreach (var vocab in vocabWords)
            {
                if (vocab.Length < 4) continue;

                // Same first letter (case-insensitive)
                if (!char.ToLowerInvariant(word[0]).Equals(char.ToLowerInvariant(vocab[0])))
                    continue;

                // Length difference can't exceed max edit distance
                if (Math.Abs(word.Length - vocab.Length) > 2)
                    continue;

                var distance = LevenshteinDistance(word.ToLowerInvariant(), vocab.ToLowerInvariant());
                if (distance > 0 && distance <= 2 && distance < bestDistance)
                {
                    bestDistance = distance;
                    bestMatch = vocab;
                }
            }

            return bestMatch ?? word;
        });
    }

    public static int LevenshteinDistance(string a, string b)
    {
        if (a.Length == 0) return b.Length;
        if (b.Length == 0) return a.Length;

        var prev = new int[b.Length + 1];
        var curr = new int[b.Length + 1];

        for (int j = 0; j <= b.Length; j++)
            prev[j] = j;

        for (int i = 1; i <= a.Length; i++)
        {
            curr[0] = i;
            for (int j = 1; j <= b.Length; j++)
            {
                int cost = a[i - 1] == b[j - 1] ? 0 : 1;
                curr[j] = Math.Min(
                    Math.Min(curr[j - 1] + 1, prev[j] + 1),
                    prev[j - 1] + cost);
            }
            (prev, curr) = (curr, prev);
        }

        return prev[b.Length];
    }

    [GeneratedRegex(@"\b[a-zA-Z0-9]+\b")]
    private static partial Regex WordTokenRegex();

    private static string ReplaceWholeWord(string input, string original,
        string replacement, StringComparison comparison)
    {
        var result = new System.Text.StringBuilder();
        int start = 0;
        while (start < input.Length)
        {
            int idx = input.IndexOf(original, start, comparison);
            if (idx < 0)
            {
                result.Append(input, start, input.Length - start);
                break;
            }

            bool leftOk = idx == 0 || !char.IsLetterOrDigit(input[idx - 1]);
            bool rightOk = idx + original.Length >= input.Length
                        || !char.IsLetterOrDigit(input[idx + original.Length]);

            if (leftOk && rightOk)
            {
                result.Append(input, start, idx - start);
                result.Append(replacement);
                start = idx + original.Length;
            }
            else
            {
                result.Append(input, start, idx - start + 1);
                start = idx + 1;
            }
        }
        return result.ToString();
    }
}
