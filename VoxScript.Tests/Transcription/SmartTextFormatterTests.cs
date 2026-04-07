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
    [InlineData("hello COMMA world", "Hello, world")]
    [InlineData("done full stop next", "Done. Next")]
    public void SpokenPunctuation_is_replaced(string input, string expected)
    {
        _sut.Format(input, smartFormattingEnabled: true).Should().Be(expected);
    }

    // Spoken punctuation tests that also involve number conversion side effects
    [Theory]
    [InlineData("first semicolon second", "First; 2nd")]
    [InlineData("line one new line line two", "Line one\nLine 2")]
    [InlineData("paragraph one new paragraph paragraph two", "Paragraph one\n\nParagraph 2")]
    public void SpokenPunctuation_with_number_interaction(string input, string expected)
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

    // ── Number conversion — Cardinals ──────────────────────────────────

    [Theory]
    [InlineData("I have zero apples", "I have 0 apples")]
    [InlineData("twenty three people", "23 people")]
    [InlineData("one hundred and fifty", "150")]
    [InlineData("two thousand twenty six", "2026")]
    [InlineData("a hundred dollars", "100 dollars")]
    [InlineData("three million", "3000000")]
    [InlineData("five hundred thousand", "500000")]
    public void Cardinals_are_converted(string input, string expected)
    {
        _sut.Format(input, smartFormattingEnabled: true).Should().Be(expected);
    }

    // ── Number conversion — Ordinals ──────────────────────────────────

    [Theory]
    [InlineData("the first time", "The 1st time")]
    [InlineData("second place", "2nd place")]
    [InlineData("third row", "3rd row")]
    [InlineData("twenty first birthday", "21st birthday")]
    public void Ordinals_are_converted(string input, string expected)
    {
        _sut.Format(input, smartFormattingEnabled: true).Should().Be(expected);
    }

    // ── Number conversion — No false positives ────────────────────────

    [Theory]
    [InlineData("anyone can do it", "Anyone can do it")]
    [InlineData("one of the best", "One of the best")]
    [InlineData("the one thing", "The one thing")]
    [InlineData("no one knows", "No one knows")]
    public void Number_exclusions_are_preserved(string input, string expected)
    {
        _sut.Format(input, smartFormattingEnabled: true).Should().Be(expected);
    }

    // ── List Detection ────────────────────────────────────────────────

    [Fact]
    public void NumberedList_three_items_formatted()
    {
        _sut.Format("1 eggs 2 milk 3 oranges", true)
            .Should().Be("1. Eggs\n2. Milk\n3. Oranges");
    }

    [Fact]
    public void NumberedList_four_items_with_surrounding_text()
    {
        _sut.Format("my list 1 eggs 2 milk 3 oranges 4 bread and done", true)
            .Should().Be("My list\n1. Eggs\n2. Milk\n3. Oranges\n4. Bread and done");
    }

    [Fact]
    public void Two_items_not_treated_as_list()
    {
        // Only 2 items — not enough to trigger list detection
        _sut.Format("I have 1 dog and 2 cats", true)
            .Should().Be("I have 1 dog and 2 cats");
    }

    [Fact]
    public void NonSequential_numbers_not_treated_as_list()
    {
        _sut.Format("I need 3 eggs and 5 oranges and 7 apples", true)
            .Should().Be("I need 3 eggs and 5 oranges and 7 apples");
    }
}
