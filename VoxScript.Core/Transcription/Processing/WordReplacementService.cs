using VoxScript.Core.Dictionary;

namespace VoxScript.Core.Transcription.Processing;

public sealed class WordReplacementService
{
    private readonly IWordReplacementRepository _repo;

    public WordReplacementService(IWordReplacementRepository repo) => _repo = repo;

    public async Task<string> ApplyAsync(string text, CancellationToken ct)
    {
        var replacements = await _repo.GetAllAsync(ct);
        foreach (var r in replacements)
        {
            var comparison = r.CaseSensitive
                ? StringComparison.Ordinal
                : StringComparison.OrdinalIgnoreCase;
            text = ReplaceWholeWord(text, r.Original, r.Replacement, comparison);
        }
        return text;
    }

    private static string ReplaceWholeWord(string input, string original,
        string replacement, StringComparison comparison)
    {
        // Simple whole-word replacement using word boundary simulation
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
