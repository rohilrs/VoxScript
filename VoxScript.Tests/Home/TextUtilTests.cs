using FluentAssertions;
using VoxScript.Core.Home;

namespace VoxScript.Tests.Home;

public class TextUtilTests
{
    [Fact] public void CountWords_null_returns_zero() =>
        TextUtil.CountWords(null).Should().Be(0);

    [Fact] public void CountWords_empty_returns_zero() =>
        TextUtil.CountWords("").Should().Be(0);

    [Fact] public void CountWords_whitespace_only_returns_zero() =>
        TextUtil.CountWords("   ").Should().Be(0);

    [Fact] public void CountWords_single_word() =>
        TextUtil.CountWords("hello").Should().Be(1);

    [Fact] public void CountWords_multiple_spaces_between_words() =>
        TextUtil.CountWords("hello   world").Should().Be(2);

    [Fact] public void CountWords_leading_trailing_whitespace() =>
        TextUtil.CountWords("  hello world  ").Should().Be(2);

    [Fact] public void CountWords_punctuation_does_not_split() =>
        TextUtil.CountWords("hello, world").Should().Be(2);

    [Fact] public void CountWords_newlines_count_as_whitespace() =>
        TextUtil.CountWords("hello\nworld\nfoo").Should().Be(3);

    [Fact] public void CountWords_tabs_count_as_whitespace() =>
        TextUtil.CountWords("hello\tworld").Should().Be(2);
}
