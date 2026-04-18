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
    public void ValidateOutput_returns_null_when_ratio_is_below_0_75()
    {
        // original: 20 words. result: 14 words → ratio = 0.70 → reject
        string original = string.Join(" ", Enumerable.Repeat("word", 20));
        string result   = string.Join(" ", Enumerable.Repeat("word", 14));
        StructuralFormattingPrompt.ValidateOutput(result, original).Should().BeNull();
    }

    [Fact]
    public void ValidateOutput_accepts_ratio_at_exactly_0_75()
    {
        // original: 20 words. result: 15 words → ratio = 0.75 → accept
        string original = string.Join(" ", Enumerable.Repeat("word", 20));
        string result   = string.Join(" ", Enumerable.Repeat("word", 15));
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
    public void ValidateOutput_excludes_dash_tokens_from_count()
    {
        // Dash markers aren't treated as list markers by the safety net
        // (only "^N. " counts), so this only exercises content-word counting.
        const string original = "alpha beta gamma";
        const string result = "- alpha\n- beta\n- gamma";
        StructuralFormattingPrompt.ValidateOutput(result, original).Should().NotBeNull();
    }

    // ── Numbered-marker safety net ─────────────────────────────────────────

    [Fact]
    public void ValidateOutput_rejects_added_numbered_markers_without_signal()
    {
        // The LLM cannot invent numbered list markers without an enumeration
        // signal (ordinal word, cue phrase, or existing markers) in the input.
        // Regression: "I think one thing I want to make sure..." was being
        // rewritten as "1. I want to make sure..." — word-count ratio alone
        // didn't catch it because the ratio was plausible.
        const string original = "I think one thing I want to make sure is that the harness branch is rebased on main";
        const string result   = "I think one thing I want to make sure is that\n\n1. The harness branch is rebased on main";
        StructuralFormattingPrompt.ValidateOutput(result, original).Should().BeNull();
    }

    [Fact]
    public void ValidateOutput_allows_numbered_markers_when_input_has_ordinal_word()
    {
        // "first" is a recognized enumeration signal.
        const string original = "first alpha beta gamma delta epsilon zeta eta theta iota";
        const string result   = "1. alpha beta gamma\n2. delta epsilon zeta\n3. eta theta iota";
        // ratio = 9/10 = 0.9, safety net passes (signal present)
        StructuralFormattingPrompt.ValidateOutput(result, original).Should().NotBeNull();
    }

    [Fact]
    public void ValidateOutput_allows_numbered_markers_when_input_has_cue_phrase()
    {
        const string original = "the following alpha beta gamma delta epsilon zeta eta theta iota";
        const string result   = "1. alpha beta gamma\n2. delta epsilon zeta\n3. eta theta iota";
        StructuralFormattingPrompt.ValidateOutput(result, original).Should().NotBeNull();
    }

    [Fact]
    public void ValidateOutput_allows_preserving_existing_numbered_markers()
    {
        // Input already has numbered markers → output preserving them is fine.
        const string original = "1. alpha beta\n2. gamma delta\n3. epsilon zeta";
        const string result   = "1. alpha beta\n2. gamma delta\n3. epsilon zeta";
        StructuralFormattingPrompt.ValidateOutput(result, original).Should().NotBeNull();
    }

    [Fact]
    public void ValidateOutput_word_one_alone_is_not_an_enumeration_signal()
    {
        // "one" is too common in prose ("one thing", "one of") to count as a
        // list signal. Without any ordinal or cue, markers in the output must
        // be rejected even if "one" appears in the input.
        const string original = "I think one thing we should fix the auth bug and the logging issue";
        const string result   = "I think one thing we should fix\n\n1. The auth bug\n2. The logging issue";
        StructuralFormattingPrompt.ValidateOutput(result, original).Should().BeNull();
    }

    // ── Newline-preservation safety net ────────────────────────────────────

    [Fact]
    public void ValidateOutput_rejects_output_that_drops_existing_newlines()
    {
        // Regression: smart formatter inserts "\n" for spoken "new line" / "new paragraph",
        // the LLM was silently merging the lines back together, word count stayed intact
        // so the ratio check didn't catch it.
        const string original = "I finished the first task.\nNow I'm starting the second one immediately";
        const string result   = "I finished the first task. Now I'm starting the second one immediately";
        StructuralFormattingPrompt.ValidateOutput(result, original).Should().BeNull();
    }

    [Fact]
    public void ValidateOutput_allows_output_that_preserves_newlines()
    {
        const string original = "First point here.\nSecond point there with more content to meet ratio";
        const string result   = "First point here.\nSecond point there with more content to meet ratio";
        StructuralFormattingPrompt.ValidateOutput(result, original).Should().NotBeNull();
    }

    [Fact]
    public void ValidateOutput_allows_output_that_adds_newlines()
    {
        // LLM adding a blank line between topics is fine — only REMOVING is banned.
        const string original = "Topic A with enough words here. Topic B with enough words too";
        const string result   = "Topic A with enough words here.\n\nTopic B with enough words too";
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
