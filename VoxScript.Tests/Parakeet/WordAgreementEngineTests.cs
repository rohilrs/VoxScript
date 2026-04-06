// VoxScript.Tests/Parakeet/WordAgreementEngineTests.cs
using FluentAssertions;
using VoxScript.Native.Parakeet;
using Xunit;

namespace VoxScript.Tests.Parakeet;

public class WordAgreementEngineTests
{
    [Fact]
    public void Feed_returns_empty_until_threshold_reached()
    {
        var engine = new WordAgreementEngine(stabilityThreshold: 3);

        engine.Feed(["hello", "world"]).Should().BeEmpty();
        engine.Feed(["hello", "world"]).Should().BeEmpty();
    }

    [Fact]
    public void Feed_returns_stable_words_after_threshold()
    {
        var engine = new WordAgreementEngine(stabilityThreshold: 3);

        engine.Feed(["hello", "world"]);
        engine.Feed(["hello", "world"]);
        var stable = engine.Feed(["hello", "world"]);

        stable.Should().BeEquivalentTo(["hello", "world"]);
    }

    [Fact]
    public void Feed_does_not_re_emit_already_emitted_words()
    {
        var engine = new WordAgreementEngine(stabilityThreshold: 3);

        engine.Feed(["hello", "world"]);
        engine.Feed(["hello", "world"]);
        engine.Feed(["hello", "world"]); // emits "hello", "world"

        // New window with additional word
        engine.Feed(["hello", "world", "foo"]);
        engine.Feed(["hello", "world", "foo"]);
        var more = engine.Feed(["hello", "world", "foo"]);

        more.Should().BeEquivalentTo(["foo"]); // already-emitted words not re-returned
    }

    [Fact]
    public void Feed_resets_state_correctly()
    {
        var engine = new WordAgreementEngine(stabilityThreshold: 3);
        engine.Feed(["hello"]); engine.Feed(["hello"]); engine.Feed(["hello"]);
        engine.Reset();

        engine.Feed(["world"]); engine.Feed(["world"]);
        engine.Feed(["world"]).Should().BeEquivalentTo(["world"]);
    }
}
