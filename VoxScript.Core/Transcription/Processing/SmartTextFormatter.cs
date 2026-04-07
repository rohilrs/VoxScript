using System.Text;
using System.Text.RegularExpressions;

namespace VoxScript.Core.Transcription.Processing;

/// <summary>
/// Formats transcription output with optional smart transforms (spoken punctuation, etc.)
/// and always-on basic cleanup (whitespace normalization, sentence capitalization, punctuation spacing).
/// Designed to replace <see cref="WhisperTextFormatter"/>.
/// </summary>
public sealed partial class SmartTextFormatter
{
    public string Format(string text, bool smartFormattingEnabled)
    {
        if (string.IsNullOrWhiteSpace(text)) return string.Empty;

        if (smartFormattingEnabled)
        {
            text = ApplySpokenPunctuation(text);
            // Future transforms inserted here (Tasks 3-6)
        }

        text = ApplyBasicCleanup(text);
        return text;
    }

    // ── Spoken Punctuation ─────────────────────────────────────────────

    private string ApplySpokenPunctuation(string text)
    {
        // Multi-word phrases first (order matters — "new paragraph" before "new line")
        text = NewParagraphRegex().Replace(text, "\n\n");
        text = NewLineRegex().Replace(text, "\n");
        text = FullStopRegex().Replace(text, ". ");
        text = QuestionMarkRegex().Replace(text, "? ");
        text = ExclamationPointRegex().Replace(text, "! ");
        text = ExclamationMarkRegex().Replace(text, "! ");

        // Single words
        text = CommaRegex().Replace(text, ", ");
        text = PeriodRegex().Replace(text, ". ");
        text = ColonRegex().Replace(text, ": ");
        text = SemicolonRegex().Replace(text, "; ");

        // Clean up whitespace around inserted punctuation: collapse spaces before punctuation
        text = SpaceBeforeInsertedPunctRegex().Replace(text, "$1");

        // Capitalize after sentence-ending punctuation
        text = CapitalizeAfterSentenceEndRegex().Replace(text, m =>
            m.Groups[1].Value + char.ToUpper(m.Groups[2].Value[0]));

        // Capitalize after newlines
        text = CapitalizeAfterNewlineRegex().Replace(text, m =>
            m.Groups[1].Value + char.ToUpper(m.Groups[2].Value[0]));

        // Trim trailing space from punctuation at end of string
        text = text.TrimEnd();

        return text;
    }

    // Multi-word phrase patterns (match whole words, case-insensitive)
    // Surrounding whitespace is consumed so punctuation attaches to previous word
    [GeneratedRegex(@"\s*\bnew\s+paragraph\b\s*", RegexOptions.IgnoreCase)]
    private static partial Regex NewParagraphRegex();

    [GeneratedRegex(@"\s*\bnew\s+line\b\s*", RegexOptions.IgnoreCase)]
    private static partial Regex NewLineRegex();

    [GeneratedRegex(@"\s*\bfull\s+stop\b\s*", RegexOptions.IgnoreCase)]
    private static partial Regex FullStopRegex();

    [GeneratedRegex(@"\s*\bquestion\s+mark\b\s*", RegexOptions.IgnoreCase)]
    private static partial Regex QuestionMarkRegex();

    [GeneratedRegex(@"\s*\bexclamation\s+point\b\s*", RegexOptions.IgnoreCase)]
    private static partial Regex ExclamationPointRegex();

    [GeneratedRegex(@"\s*\bexclamation\s+mark\b\s*", RegexOptions.IgnoreCase)]
    private static partial Regex ExclamationMarkRegex();

    // Single-word patterns
    [GeneratedRegex(@"\s*\bcomma\b\s*", RegexOptions.IgnoreCase)]
    private static partial Regex CommaRegex();

    [GeneratedRegex(@"\s*\bperiod\b\s*", RegexOptions.IgnoreCase)]
    private static partial Regex PeriodRegex();

    [GeneratedRegex(@"\s*\bcolon\b\s*", RegexOptions.IgnoreCase)]
    private static partial Regex ColonRegex();

    [GeneratedRegex(@"\s*\bsemicolon\b\s*", RegexOptions.IgnoreCase)]
    private static partial Regex SemicolonRegex();

    // Collapse spaces that ended up before punctuation (e.g., "hello , world" → "hello, world")
    [GeneratedRegex(@"\s+([,.\?!:;])")]
    private static partial Regex SpaceBeforeInsertedPunctRegex();

    // Capitalize after sentence-ending punctuation followed by space(s)
    [GeneratedRegex(@"([.!?]\s+)([a-z])")]
    private static partial Regex CapitalizeAfterSentenceEndRegex();

    // Capitalize after newlines
    [GeneratedRegex(@"(\n+)([a-z])")]
    private static partial Regex CapitalizeAfterNewlineRegex();

    // ── Basic Cleanup ──────────────────────────────────────────────────

    private string ApplyBasicCleanup(string text)
    {
        // Collapse horizontal whitespace (not newlines) to single space
        text = CollapseHorizontalSpaceRegex().Replace(text, " ");

        // Remove space before punctuation
        text = SpaceBeforePunctRegex().Replace(text, "$1");

        // Ensure space after punctuation when followed by a letter (but not after newlines or within \n sequences)
        text = SpaceAfterPunctRegex().Replace(text, "$1 $2");

        // Capitalize first character of each sentence (after . ? ! and after newlines)
        text = SentenceStartRegex().Replace(text, m =>
            m.Groups[1].Value + char.ToUpper(m.Groups[2].Value[0]));

        // Capitalize after newlines
        text = NewlineSentenceStartRegex().Replace(text, m =>
            m.Groups[1].Value + char.ToUpper(m.Groups[2].Value[0]));

        // Trim each line
        text = TrimLines(text);

        // Capitalize first character of overall string
        text = text.TrimStart();
        if (text.Length > 0 && char.IsLower(text[0]))
            text = char.ToUpper(text[0]) + text[1..];

        text = text.TrimEnd();

        return text;
    }

    private static string TrimLines(string text)
    {
        if (!text.Contains('\n')) return text.Trim();

        var lines = text.Split('\n');
        for (int i = 0; i < lines.Length; i++)
            lines[i] = lines[i].Trim();
        return string.Join('\n', lines);
    }

    // Collapse horizontal whitespace (spaces, tabs) but preserve newlines
    [GeneratedRegex(@"[^\S\n]+")]
    private static partial Regex CollapseHorizontalSpaceRegex();

    // Remove space before punctuation marks
    [GeneratedRegex(@"\s+([,.\?!:;])")]
    private static partial Regex SpaceBeforePunctRegex();

    // Ensure space after punctuation when directly followed by a letter
    [GeneratedRegex(@"([,.\?!:;])([A-Za-z])")]
    private static partial Regex SpaceAfterPunctRegex();

    // Capitalize after sentence-ending punctuation + space
    [GeneratedRegex(@"([.!?]\s+)([a-z])")]
    private static partial Regex SentenceStartRegex();

    // Capitalize after newlines
    [GeneratedRegex(@"(\n+\s*)([a-z])")]
    private static partial Regex NewlineSentenceStartRegex();
}
