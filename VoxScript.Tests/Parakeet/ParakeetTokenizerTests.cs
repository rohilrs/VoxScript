using FluentAssertions;
using VoxScript.Native.Parakeet;
using Xunit;

namespace VoxScript.Tests.Parakeet;

public class ParakeetTokenizerTests
{
    [Fact]
    public void Constructor_throws_if_model_file_missing()
    {
        var act = () => new ParakeetTokenizer("/nonexistent/path.model");
        act.Should().Throw<FileNotFoundException>();
    }
}
