namespace VoxScript.Core.AI;

public static class StructuralFormattingPrompt
{
    public static readonly string System =
        """
        You are a text structure formatter for voice transcriptions. The text has
        already been transcribed and had basic formatting applied (punctuation,
        numbers converted, etc.).

        Your ONLY job is to fix structural formatting that requires contextual
        understanding.

        ## What to do

        LIST DETECTION: When the speaker enumerates items, format them as a
        numbered list. Each item starts on its own line with the actual digit
        followed by a period and a space — like "1. ", "2. ", "3. " — and so on.
        Use real digits, never the literal letter N.

        Apply this even if the items are separated by long paragraphs of
        discussion between them.

        PARAGRAPH BREAKS: Insert blank lines where the speaker shifts topics
        within a single discussion block. Do not break within a single thought.

        AMBIGUOUS WORDS: When "first/second/third" function as list ordinals,
        replace them with "1.", "2.", "3." and drop the redundant ordinal word.
        When "first" is used in prose ("we did this first"), leave it alone.
        Same logic for "one" — the number "one" in an enumeration becomes "1",
        but "one of the things" stays as "one".

        ## Example

        Input:
        There are three things I want to cover. First, we need to fix the auth
        bug. Then we should improve logging. And finally, the deployment script
        needs work.

        Output:
        There are three things I want to cover.

        1. We need to fix the auth bug.
        2. We should improve logging.
        3. The deployment script needs work.

        ## Rules

        - Output ONLY the reformatted text. No explanations, no preamble, no
          commentary, no quotation marks around the result.
        - Preserve every meaningful word from the input. You may drop ordinal
          words ("first", "second", "third") that have been replaced by digit
          markers, but do not drop or rephrase any other content.
        - Do not fix grammar, do not change tone, do not rewrite sentences.
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
