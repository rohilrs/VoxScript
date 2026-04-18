using FluentAssertions;
using VoxScript.Core.AI;

namespace VoxScript.Tests.AI;

public class StructuralFormattingPromptTests
{
    // ── Null / empty / whitespace ──────────────────────────────────────────

    [Fact]
    public void ValidateOutput_returns_null_for_null_result()
    {
        StructuralFormattingPrompt.ValidateOutput(null, "some original text").Should().BeNull();
    }

    [Fact]
    public void ValidateOutput_returns_null_for_empty_result()
    {
        StructuralFormattingPrompt.ValidateOutput("", "some original text").Should().BeNull();
    }

    [Fact]
    public void ValidateOutput_returns_null_for_whitespace_result()
    {
        StructuralFormattingPrompt.ValidateOutput("   ", "some original text").Should().BeNull();
    }

    [Fact]
    public void ValidateOutput_returns_null_when_original_has_no_content_words()
    {
        // original is pure punctuation/numbers → origCount == 0 → return null
        StructuralFormattingPrompt.ValidateOutput("hello world", "123 456 !!! ---").Should().BeNull();
    }

    // ── Ratio boundary: lower bound ────────────────────────────────────────

    [Fact]
    public void ValidateOutput_returns_null_when_ratio_is_below_0_85()
    {
        // original: 20 words. result: 16 words → ratio = 0.80 → reject
        string original = string.Join(" ", Enumerable.Repeat("word", 20));
        string result   = string.Join(" ", Enumerable.Repeat("word", 16));
        StructuralFormattingPrompt.ValidateOutput(result, original).Should().BeNull();
    }

    [Fact]
    public void ValidateOutput_accepts_ratio_at_exactly_0_85()
    {
        // original: 20 words. result: 17 words → ratio = 0.85 → accept
        string original = string.Join(" ", Enumerable.Repeat("word", 20));
        string result   = string.Join(" ", Enumerable.Repeat("word", 17));
        StructuralFormattingPrompt.ValidateOutput(result, original).Should().NotBeNull();
    }

    // ── Ratio boundary: upper bound ────────────────────────────────────────

    [Fact]
    public void ValidateOutput_accepts_ratio_at_exactly_1_15()
    {
        // original: 20 words. result: 23 words → ratio = 1.15 → accept
        string original = string.Join(" ", Enumerable.Repeat("word", 20));
        string result   = string.Join(" ", Enumerable.Repeat("word", 23));
        StructuralFormattingPrompt.ValidateOutput(result, original).Should().NotBeNull();
    }

    [Fact]
    public void ValidateOutput_returns_null_when_ratio_exceeds_1_15()
    {
        // original: 20 words. result: 24 words → ratio = 1.20 → reject
        string original = string.Join(" ", Enumerable.Repeat("word", 20));
        string result   = string.Join(" ", Enumerable.Repeat("word", 24));
        StructuralFormattingPrompt.ValidateOutput(result, original).Should().BeNull();
    }

    // ── List markers are not counted as content words ──────────────────────

    [Fact]
    public void ValidateOutput_excludes_pure_numeric_list_markers_from_count()
    {
        // original: 3 content words. result: same 3 words + "1." "2." "3." markers
        // Without exclusion, result would be 6 "words" → ratio 2.0 → reject
        // With exclusion, result is 3 content words → ratio 1.0 → accept
        const string original = "fix auth logging deployment";
        const string result = "1. fix\n2. auth logging\n3. deployment";
        StructuralFormattingPrompt.ValidateOutput(result, original).Should().NotBeNull();
    }

    [Fact]
    public void ValidateOutput_excludes_dash_tokens_from_count()
    {
        const string original = "alpha beta gamma";
        const string result = "- alpha\n- beta\n- gamma";
        StructuralFormattingPrompt.ValidateOutput(result, original).Should().NotBeNull();
    }

    // ── Result is trimmed ──────────────────────────────────────────────────

    [Fact]
    public void ValidateOutput_trims_result()
    {
        const string original = "hello world";
        const string result   = "  hello world  ";
        StructuralFormattingPrompt.ValidateOutput(result, original).Should().Be("hello world");
    }

    // ── System prompt is non-empty ─────────────────────────────────────────

    [Fact]
    public void System_prompt_is_not_null_or_whitespace()
    {
        StructuralFormattingPrompt.System.Should().NotBeNullOrWhiteSpace();
    }
}
