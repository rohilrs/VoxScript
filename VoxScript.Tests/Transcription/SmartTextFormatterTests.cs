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
    [InlineData("first semicolon second", "1st; 2nd")]
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
    [InlineData("a hundred dollars", "$100")]
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

    // ── Number conversion — With trailing punctuation ────────────────

    [Theory]
    [InlineData("one. Problem scoping", "1. Problem scoping")]
    [InlineData("Two, the rest", "2, the rest")]
    [InlineData("Three, evaluation", "3, evaluation")]
    [InlineData("alright one. next two. done", "Alright 1. Next 2. Done")]
    public void Numbers_with_trailing_punctuation_are_converted(string input, string expected)
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
    public void NumberedList_after_colon_is_formatted()
    {
        _sut.Format("my list: 1 eggs 2 milk 3 oranges 4 bread and done", true)
            .Should().Be("My list:\n1. Eggs\n2. Milk\n3. Oranges\n4. Bread and done");
    }

    [Fact]
    public void NumberedList_after_newline_is_formatted()
    {
        _sut.Format("here are my items\n1 eggs 2 milk 3 oranges", true)
            .Should().Be("Here are my items\n1. Eggs\n2. Milk\n3. Oranges");
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

    [Fact]
    public void MidSentence_numbers_are_not_fragmented_into_list()
    {
        // Regression: narrative prose with sequential numbers used to be reformatted
        // as a list. The starting "1" is mid-sentence, so it must NOT anchor a list.
        _sut.Format("I had 1 coffee, met 2 friends, walked 3 miles", true)
            .Should().Be("I had 1 coffee, met 2 friends, walked 3 miles");
    }

    [Fact]
    public void Numbers_in_middle_of_prose_without_cue_stay_inline()
    {
        // No colon, no newline, no start-of-string before the "1" — stays as prose.
        _sut.Format("my list 1 eggs 2 milk 3 oranges", true)
            .Should().Be("My list 1 eggs 2 milk 3 oranges");
    }

    // ── Currency ──────────────────────────────────────────────────────

    [Theory]
    [InlineData("that costs 23 dollars", "That costs $23")]
    [InlineData("just 50 cents", "Just $0.50")]
    [InlineData("10 dollars and 50 cents", "$10.50")]
    [InlineData("about 23 bucks", "About $23")]
    [InlineData("5 dollars and 5 cents", "$5.05")]
    public void Currency_is_formatted(string input, string expected)
    {
        _sut.Format(input, smartFormattingEnabled: true).Should().Be(expected);
    }

    // ── Percentages ───────────────────────────────────────────────────

    [Theory]
    [InlineData("50 percent chance", "50% chance")]
    [InlineData("100 percent done", "100% done")]
    public void Percentages_are_formatted(string input, string expected)
    {
        _sut.Format(input, smartFormattingEnabled: true).Should().Be(expected);
    }

    // ── Dates ─────────────────────────────────────────────────────────

    [Theory]
    [InlineData("on March 5", "On March 5th")]
    [InlineData("March 5 2026", "March 5th, 2026")]
    [InlineData("January 1 2000", "January 1st, 2000")]
    [InlineData("December 22", "December 22nd")]
    [InlineData("April 13", "April 13th")]
    [InlineData("February 21", "February 21st")]
    public void Dates_are_formatted(string input, string expected)
    {
        _sut.Format(input, smartFormattingEnabled: true).Should().Be(expected);
    }

    // ── Times ─────────────────────────────────────────────────────────

    [Theory]
    [InlineData("at 3 30 PM", "At 3:30 PM")]
    [InlineData("at 3 PM", "At 3:00 PM")]
    [InlineData("at 3 o'clock", "At 3:00")]
    [InlineData("at noon today", "At 12:00 PM today")]
    [InlineData("at midnight", "At 12:00 AM")]
    public void Times_are_formatted(string input, string expected)
    {
        _sut.Format(input, smartFormattingEnabled: true).Should().Be(expected);
    }

    // ── Email Assembly ────────────────────────────────────────────────

    [Theory]
    [InlineData("my email is rohils74 at gmail dot com", "My email is rohils74@gmail.com")]
    [InlineData("send to user at company dot co dot uk", "Send to user@company.co.uk")]
    public void Emails_are_assembled(string input, string expected)
    {
        _sut.Format(input, smartFormattingEnabled: true).Should().Be(expected);
    }

    // ── URL Assembly ──────────────────────────────────────────────────

    [Theory]
    [InlineData("go to w w w dot example dot com", "Go to www.example.com")]
    [InlineData("visit https colon slash slash example dot com", "Visit https://example.com")]
    [InlineData("check example dot com slash about", "Check example.com/about")]
    public void Urls_are_assembled(string input, string expected)
    {
        _sut.Format(input, smartFormattingEnabled: true).Should().Be(expected);
    }

    // ── Phone Number Formatting ───────────────────────────────────────

    [Theory]
    [InlineData("call 5 5 5 1 2 3 4 5 6 7", "Call (555) 123-4567")]
    [InlineData("dial 5 5 5 1 2 3 4", "Dial 555-1234")]
    public void PhoneNumbers_are_formatted(string input, string expected)
    {
        _sut.Format(input, smartFormattingEnabled: true).Should().Be(expected);
    }
}
