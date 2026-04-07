using FluentAssertions;
using VoxScript.Core.Dictionary;
using Xunit;

namespace VoxScript.Tests.Dictionary;

public sealed class CommonWordListTests : IDisposable
{
    private readonly string _tempFile;
    private readonly CommonWordList _list;

    public CommonWordListTests()
    {
        _tempFile = Path.GetTempFileName();
        File.WriteAllLines(_tempFile, ["the", "and", "hello", "world", "computer"]);
        _list = new CommonWordList(_tempFile);
    }

    public void Dispose() => File.Delete(_tempFile);

    [Fact]
    public void Contains_returns_true_for_listed_word()
    {
        _list.Contains("hello").Should().BeTrue();
    }

    [Fact]
    public void Contains_is_case_insensitive()
    {
        _list.Contains("HELLO").Should().BeTrue();
        _list.Contains("Hello").Should().BeTrue();
    }

    [Fact]
    public void Contains_returns_false_for_unlisted_word()
    {
        _list.Contains("kubernetes").Should().BeFalse();
    }

    [Fact]
    public void Contains_handles_empty_and_null()
    {
        _list.Contains("").Should().BeFalse();
        _list.Contains(null!).Should().BeFalse();
    }
}
