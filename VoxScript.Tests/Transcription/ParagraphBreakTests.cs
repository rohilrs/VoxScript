using FluentAssertions;
using VoxScript.Core.Transcription.Batch;
using VoxScript.Core.Transcription.Core;
using Xunit;

namespace VoxScript.Tests.Transcription;

public class ParagraphBreakTests
{
    [Fact]
    public void EmptyArray_returns_empty_string()
    {
        LocalTranscriptionService.JoinWithParagraphBreaks([])
            .Should().BeEmpty();
    }

    [Fact]
    public void SingleSegment_returns_trimmed_text()
    {
        var segments = new[] { new TranscriptionSegment(" Hello world ", 0, 1000) };
        LocalTranscriptionService.JoinWithParagraphBreaks(segments)
            .Should().Be("Hello world");
    }

    [Fact]
    public void SmallGap_joins_with_space()
    {
        var segments = new[]
        {
            new TranscriptionSegment("First segment", 0, 1000),
            new TranscriptionSegment("second segment", 1200, 2000),
        };
        LocalTranscriptionService.JoinWithParagraphBreaks(segments)
            .Should().Be("First segment second segment");
    }

    [Fact]
    public void LargeGap_inserts_paragraph_break()
    {
        var segments = new[]
        {
            new TranscriptionSegment("First paragraph", 0, 1000),
            new TranscriptionSegment("second paragraph", 3500, 5000),
        };
        LocalTranscriptionService.JoinWithParagraphBreaks(segments)
            .Should().Be("First paragraph\n\nsecond paragraph");
    }

    [Fact]
    public void ExactThreshold_inserts_paragraph_break()
    {
        var segments = new[]
        {
            new TranscriptionSegment("Before", 0, 1000),
            new TranscriptionSegment("after", 3500, 5000),
        };
        long gap = 3500 - 1000; // 2500ms exactly
        gap.Should().Be(LocalTranscriptionService.ParagraphGapMs);

        LocalTranscriptionService.JoinWithParagraphBreaks(segments)
            .Should().Be("Before\n\nafter");
    }

    [Fact]
    public void JustBelowThreshold_joins_with_space()
    {
        var segments = new[]
        {
            new TranscriptionSegment("Before", 0, 1000),
            new TranscriptionSegment("after", 3499, 5000),
        };
        long gap = 3499 - 1000; // 2499ms, just below threshold
        gap.Should().BeLessThan(LocalTranscriptionService.ParagraphGapMs);

        LocalTranscriptionService.JoinWithParagraphBreaks(segments)
            .Should().Be("Before after");
    }

    [Fact]
    public void MixedGaps_produces_correct_breaks()
    {
        var segments = new[]
        {
            new TranscriptionSegment("Sentence one", 0, 2000),
            new TranscriptionSegment("sentence two", 2200, 4000),       // 200ms gap -> space
            new TranscriptionSegment("new paragraph", 7000, 9000),      // 3000ms gap -> break
            new TranscriptionSegment("still same para", 9100, 10000),   // 100ms gap -> space
        };
        LocalTranscriptionService.JoinWithParagraphBreaks(segments)
            .Should().Be("Sentence one sentence two\n\nnew paragraph still same para");
    }
}
