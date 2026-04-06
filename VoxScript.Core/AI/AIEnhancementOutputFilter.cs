namespace VoxScript.Core.AI;

/// <summary>
/// Validates AI enhancement output. Rejects responses that are too different
/// from the original (model hallucinated new content) or clearly invalid.
/// </summary>
public sealed class AIEnhancementOutputFilter
{
    private const double MaxLengthRatio = 3.0;
    private const double MinLengthRatio = 0.3;

    public string? Filter(string enhanced, string original)
    {
        if (string.IsNullOrWhiteSpace(enhanced)) return null;

        var enhancedWords = enhanced.Split(' ', StringSplitOptions.RemoveEmptyEntries).Length;
        var originalWords = original.Split(' ', StringSplitOptions.RemoveEmptyEntries).Length;

        if (originalWords == 0) return enhanced;

        double ratio = (double)enhancedWords / originalWords;

        // Reject if AI added or removed too much content
        if (ratio > MaxLengthRatio || ratio < MinLengthRatio)
            return null;

        return enhanced.Trim();
    }
}
