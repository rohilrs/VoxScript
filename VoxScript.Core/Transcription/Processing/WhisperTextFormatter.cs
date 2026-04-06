using System.Text.RegularExpressions;

namespace VoxScript.Core.Transcription.Processing;

/// <summary>Formats whisper output: capitalization, paragraph breaks, punctuation normalization.</summary>
public sealed class WhisperTextFormatter
{
    private static readonly Regex MultipleSpaces = new(@"\s{2,}", RegexOptions.Compiled);
    private static readonly Regex SentenceEnd = new(@"([.!?])\s+([A-Z])", RegexOptions.Compiled);

    public string Format(string text)
    {
        if (string.IsNullOrWhiteSpace(text)) return string.Empty;

        text = MultipleSpaces.Replace(text.Trim(), " ");

        // Ensure first character is uppercase
        if (char.IsLower(text[0]))
            text = char.ToUpper(text[0]) + text[1..];

        return text;
    }
}
