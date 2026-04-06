using FluentAssertions;
using VoxScript.Core.AI;
using Xunit;

namespace VoxScript.Tests.AI;

public class AIEnhancementOutputFilterTests
{
    private readonly AIEnhancementOutputFilter _filter = new();

    [Fact]
    public void Filter_passes_similar_length_response()
    {
        var original = "hello world this is a test";
        var enhanced = "Hello world, this is a test.";
        _filter.Filter(enhanced, original).Should().Be(enhanced);
    }

    [Fact]
    public void Filter_rejects_response_three_times_longer()
    {
        var original = "hi";
        var enhanced = string.Join(" ", Enumerable.Repeat("word", 20));
        _filter.Filter(enhanced, original).Should().BeNull();
    }

    [Fact]
    public void Filter_rejects_response_too_short()
    {
        var original = string.Join(" ", Enumerable.Repeat("word", 20));
        _filter.Filter("ok", original).Should().BeNull();
    }

    [Fact]
    public void Filter_returns_null_for_empty_response()
    {
        _filter.Filter("", "some text").Should().BeNull();
    }
}
