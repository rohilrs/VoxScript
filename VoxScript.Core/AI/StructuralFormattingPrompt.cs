namespace VoxScript.Core.AI;

public static class StructuralFormattingPrompt
{
    public static readonly string System =
        """
        You are a text structure formatter for voice transcriptions. The text has
        already been transcribed and had basic formatting applied (punctuation,
        numbers converted, etc.).

        Your ONLY job is to fix structural formatting that requires contextual
        understanding:

        1. LIST DETECTION: When the speaker enumerates items (using "first/second/
           third", "1, 2, 3", "one, two, three" as markers), format them as a
           numbered list with each item on its own line, prefixed with "N. ", even
           if separated by long paragraphs of discussion between items.

        2. PARAGRAPH BREAKS: Insert paragraph breaks (blank lines) where the
           speaker shifts topics within a single discussion block. Do NOT break
           within a single thought.

        3. AMBIGUOUS WORDS: Resolve context-dependent words:
           - "first/second/third" as list ordinals → "1./2./3."
           - "first" in prose ("we did this first") → leave as "first"
           - "one" as a number in enumeration → "1"
           - "one" as a pronoun ("one of the things") → leave as "one"

        RULES:
        - Output ONLY the reformatted text. No explanations, no preamble.
        - Do NOT change any words. Do NOT fix grammar. Do NOT rephrase.
        - Do NOT add or remove content.
        - Preserve ALL original words exactly. Only change structure (line breaks,
          numbering format, paragraph grouping).
        - If the text needs no structural changes, return it exactly as-is.
        """;

    /// <summary>
    /// Validates LLM output by comparing content word counts against the original.
    /// Returns null if the output should be rejected; returns the trimmed result if accepted.
    /// A "content word" is any whitespace-delimited token containing at least one letter —
    /// pure numeric/punctuation tokens like "1.", "-", "2)" are excluded so list markers
    /// added by the LLM don't falsely fail the ratio check.
    /// </summary>
    public static string? ValidateOutput(string? result, string original)
    {
        if (string.IsNullOrWhiteSpace(result)) return null;

        int origCount   = CountContentWords(original);
        int resultCount = CountContentWords(result);

        if (origCount == 0) return null;

        double ratio = (double)resultCount / origCount;
        if (ratio < 0.85 || ratio > 1.15) return null;

        return result.Trim();
    }

    private static int CountContentWords(string text) =>
        text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries)
            .Count(t => t.Any(char.IsLetter));
}
