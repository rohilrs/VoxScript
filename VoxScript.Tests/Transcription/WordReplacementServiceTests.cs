using FluentAssertions;
using NSubstitute;
using VoxScript.Core.Dictionary;
using VoxScript.Core.Persistence;
using VoxScript.Core.Transcription.Processing;
using Xunit;

namespace VoxScript.Tests.Transcription;

public class WordReplacementServiceTests
{
    private IWordReplacementRepository MakeRepo(params WordReplacementRecord[] records)
    {
        var repo = Substitute.For<IWordReplacementRepository>();
        repo.GetAllAsync(default).ReturnsForAnyArgs(Task.FromResult<IReadOnlyList<WordReplacementRecord>>(records));
        return repo;
    }

    [Fact]
    public async Task ApplyAsync_replaces_whole_word()
    {
        var svc = new WordReplacementService(MakeRepo(
            new WordReplacementRecord { Original = "colour", Replacement = "color" }));

        var result = await svc.ApplyAsync("The colour is red.", default);
        result.Should().Be("The color is red.");
    }

    [Fact]
    public async Task ApplyAsync_does_not_replace_partial_word()
    {
        var svc = new WordReplacementService(MakeRepo(
            new WordReplacementRecord { Original = "cat", Replacement = "dog" }));

        var result = await svc.ApplyAsync("The category is set.", default);
        result.Should().Be("The category is set.");
    }

    [Fact]
    public async Task ApplyAsync_case_insensitive_by_default()
    {
        var svc = new WordReplacementService(MakeRepo(
            new WordReplacementRecord { Original = "hello", Replacement = "hi", CaseSensitive = false }));

        var result = await svc.ApplyAsync("HELLO world", default);
        result.Should().Be("hi world");
    }
}
