using System.Text.RegularExpressions;

namespace VoxScript.Core.AI;

public static class StructuralFormattingPrompt
{
    public static readonly string System =
        """
        You are a text structure formatter for voice transcriptions. The text has
        already been transcribed and had basic formatting applied (punctuation,
        numbers converted, etc.).

        Your ONLY job is to fix structural formatting that requires contextual
        understanding. Be CONSERVATIVE — when in doubt, return the input unchanged.

        ## When to create a numbered list

        Only convert content into a numbered list when the speaker gave an
        EXPLICIT enumeration signal in the input. Valid signals:

        - The text contains the ordinal words "first", "second", "third" (or
          "fourth", "fifth", …) functioning as list markers, in the ORDER they
          enumerate items. Replace each ordinal word with "1.", "2.", "3." and
          drop the ordinal word itself.
        - The text already contains sequential numbered markers at line starts
          ("1.", "2.", "3." at the beginning of lines). Preserve them.
        - The text contains a direct list cue like "here are N things:",
          "the following:", "these are:" immediately followed by items.

        Do NOT create a list in any other situation. In particular:

        - "One thing I want to mention…" is NOT a list cue. It stays as prose.
        - "One other thing is…" is NOT a list cue. It stays as prose.
        - "I need to do X, Y, and Z" is NOT a list. It stays as prose.
        - A sentence that just happens to contain the word "one", "two", or "three"
          as a count is NOT enumeration.
        - If the speaker only gives one enumerated item (e.g. only says "first"
          with nothing matching "second"), do NOT create a single-item list.

        ## Paragraph breaks

        Insert a blank line between clearly separate topics within a longer
        dictation. Do not break mid-thought. If the text is short (under ~40
        words) or is a single coherent thought, leave it alone.

        ## Rules

        - Output ONLY the reformatted text. No explanations, no preamble, no
          commentary, no quotation marks around the result.
        - Preserve every meaningful word from the input. You may drop ordinal
          words ("first", "second", "third") that have been replaced by digit
          markers, but do not drop or rephrase any other content.
        - Do not fix grammar, do not change tone, do not rewrite sentences.
        - Preserve every existing line break and blank line from the input
          EXACTLY. If the input has a "\n" you must keep it — the speaker
          explicitly asked for that break via spoken punctuation. You may ADD
          additional blank lines between clearly separate topics (see above),
          but never remove or merge existing lines.
        - If the text needs no structural changes, return it EXACTLY as-is.

        ## Example — valid list conversion

        Input:
        There are three things I want to cover. First, we need to fix the auth
        bug. Second, we should improve logging. Third, the deployment script
        needs work.

        Output:
        There are three things I want to cover.

        1. We need to fix the auth bug.
        2. We should improve logging.
        3. The deployment script needs work.

        ## Example — NOT a list, leave unchanged

        Input:
        I think one other thing I want to make sure is that the harness branch
        is rebased on main so all the recent changes are caught. Also, we should
        double-check the test coverage before merging.

        Output:
        I think one other thing I want to make sure is that the harness branch
        is rebased on main so all the recent changes are caught. Also, we should
        double-check the test coverage before merging.
        """;

    /// <summary>
    /// Validates LLM output and returns the trimmed result if acceptable, otherwise null.
    ///
    /// Checks:
    ///  - Content-word-count ratio in [0.75, 1.15] (catches both hallucination and gutting).
    ///  - List-marker safety: if the LLM inserted numbered list markers, the input must
    ///    contain at least one enumeration signal (ordinal word, existing list marker, or
    ///    a cue phrase). Prevents the model from turning plain prose into a numbered list.
    ///  - Newline preservation: the output must have at least as many "\n" characters as
    ///    the input. The smart formatter inserts "\n" for spoken "new line" / "new paragraph"
    ///    cues, and the LLM was silently dropping them — word count stayed intact so the
    ///    ratio check didn't catch it. The LLM can still add blank lines between topics
    ///    (that only increases the count); it just can't remove existing ones.
    /// </summary>
    public static string? ValidateOutput(string? result, string original)
    {
        if (string.IsNullOrWhiteSpace(result)) return null;

        int origCount   = CountContentWords(original);
        int resultCount = CountContentWords(result);

        if (origCount == 0) return null;

        double ratio = (double)resultCount / origCount;
        if (ratio < 0.75 || ratio > 1.15) return null;

        // List-marker safety net.
        if (CountListMarkers(result) > 0 && !HasEnumerationSignal(original))
            return null;

        // Newline-preservation safety net.
        if (CountNewlines(result) < CountNewlines(original))
            return null;

        return result.Trim();
    }

    private static int CountContentWords(string text) =>
        text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries)
            .Count(t => t.Any(char.IsLetter));

    /// <summary>Count of lines that begin with a "N. " numbered list marker.</summary>
    private static int CountListMarkers(string text) =>
        Regex.Matches(text, @"(?m)^\s*\d+\.\s").Count;

    /// <summary>
    /// Counts LF characters. Treats CRLF as a single newline (since the LF is
    /// always present). Used to verify the LLM didn't drop any line breaks
    /// the smart formatter or the user explicitly inserted.
    /// </summary>
    private static int CountNewlines(string text)
    {
        if (string.IsNullOrEmpty(text)) return 0;
        int count = 0;
        foreach (var c in text)
            if (c == '\n') count++;
        return count;
    }

    /// <summary>
    /// True if the input contains an ordinal enumeration word, an existing list marker,
    /// or an explicit list cue phrase. "one" is intentionally excluded — it's too common
    /// in prose ("one thing", "one of", "one way") to be a reliable list signal.
    /// </summary>
    private static bool HasEnumerationSignal(string text)
    {
        if (CountListMarkers(text) > 0) return true;

        if (Regex.IsMatch(text,
                @"\b(first|second|third|fourth|fifth|sixth|seventh|eighth|ninth|tenth)\b",
                RegexOptions.IgnoreCase))
            return true;

        if (Regex.IsMatch(text,
                @"\b(the following|here are|these are)\b",
                RegexOptions.IgnoreCase))
            return true;

        return false;
    }
}
