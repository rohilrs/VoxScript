using FluentAssertions;
using VoxScript.Core.Transcription.Processing;

namespace VoxScript.Tests.Transcription;

public class SmartTextFormatterTests
{
    private readonly SmartTextFormatter _sut = new();

    // ── Null / empty input ─────────────────────────────────────────────

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Format_returns_empty_for_null_or_whitespace(string? input)
    {
        _sut.Format(input!, smartFormattingEnabled: true).Should().BeEmpty();
        _sut.Format(input!, smartFormattingEnabled: false).Should().BeEmpty();
    }

    // ── Spoken punctuation (smart formatting ON) ───────────────────────

    [Theory]
    [InlineData("hello comma how are you", "Hello, how are you")]
    [InlineData("stop period next sentence", "Stop. Next sentence")]
    [InlineData("is it done question mark", "Is it done?")]
    [InlineData("wow exclamation point that is great", "Wow! That is great")]
    [InlineData("items colon eggs and milk", "Items: eggs and milk")]
    [InlineData("first semicolon second", "First; second")]
    [InlineData("line one new line line two", "Line one\nLine two")]
    [InlineData("paragraph one new paragraph paragraph two", "Paragraph one\n\nParagraph two")]
    [InlineData("hello COMMA world", "Hello, world")]
    [InlineData("done full stop next", "Done. Next")]
    public void SpokenPunctuation_is_replaced(string input, string expected)
    {
        _sut.Format(input, smartFormattingEnabled: true).Should().Be(expected);
    }

    // ── Basic cleanup (smart formatting OFF to test isolation) ─────────

    [Theory]
    [InlineData("  hello   world  ", "Hello world")]
    [InlineData("hello. world", "Hello. World")]
    [InlineData("done! next", "Done! Next")]
    [InlineData("what? yes", "What? Yes")]
    [InlineData("line one\n\nline two", "Line one\n\nLine two")]
    [InlineData("hello ,world", "Hello, world")]
    public void BasicCleanup_formats_correctly(string input, string expected)
    {
        _sut.Format(input, smartFormattingEnabled: false).Should().Be(expected);
    }

    // ── Smart formatting OFF does not apply spoken punctuation ─────────

    [Fact]
    public void SmartFormatting_off_does_not_replace_spoken_punctuation()
    {
        _sut.Format("hello comma world", smartFormattingEnabled: false)
            .Should().Be("Hello comma world");
    }
}
