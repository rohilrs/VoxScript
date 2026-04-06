using FluentAssertions;
using VoxScript.Core.Transcription.Processing;
using Xunit;

namespace VoxScript.Tests.Transcription;

public class TranscriptionOutputFilterTests
{
    private readonly TranscriptionOutputFilter _filter = new();

    [Fact]
    public void Filter_returns_empty_for_hallucination_phrase()
    {
        _filter.Filter("Thank you for watching").Should().BeEmpty();
    }

    [Fact]
    public void Filter_returns_empty_for_blank_audio_marker()
    {
        _filter.Filter("[BLANK_AUDIO]").Should().BeEmpty();
    }

    [Fact]
    public void Filter_passes_through_valid_text()
    {
        var result = _filter.Filter("Hello, this is a test.");
        result.Should().Be("Hello, this is a test.");
    }

    [Fact]
    public void Filter_strips_repetition_loops()
    {
        var repeated = string.Concat(Enumerable.Repeat("the quick brown fox ", 4));
        var result = _filter.Filter(repeated);
        result.Length.Should().BeLessThan(repeated.Length);
    }
}
