using FluentAssertions;
using NSubstitute;
using VoxScript.Core.Dictionary;
using VoxScript.Core.Persistence;
using VoxScript.Core.Transcription.Processing;
using Xunit;

namespace VoxScript.Tests.Transcription;

public class WordReplacementServiceTests
{
    private readonly IVocabularyRepository _vocab = Substitute.For<IVocabularyRepository>();

    public WordReplacementServiceTests()
    {
        _vocab.GetWordsAsync(Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<IReadOnlyList<string>>(new List<string>()));
    }

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
            new WordReplacementRecord { Original = "colour", Replacement = "color" }), _vocab);

        var result = await svc.ApplyAsync("The colour is red.", default);
        result.Should().Be("The color is red.");
    }

    [Fact]
    public async Task ApplyAsync_does_not_replace_partial_word()
    {
        var svc = new WordReplacementService(MakeRepo(
            new WordReplacementRecord { Original = "cat", Replacement = "dog" }), _vocab);

        var result = await svc.ApplyAsync("The category is set.", default);
        result.Should().Be("The category is set.");
    }

    [Fact]
    public async Task ApplyAsync_case_insensitive_by_default()
    {
        var svc = new WordReplacementService(MakeRepo(
            new WordReplacementRecord { Original = "hello", Replacement = "hi", CaseSensitive = false }), _vocab);

        var result = await svc.ApplyAsync("HELLO world", default);
        result.Should().Be("hi world");
    }

    // ── Vocabulary fuzzy correction tests ────────────────────────

    [Fact]
    public async Task ApplyAsync_corrects_misspelling_to_vocabulary_word()
    {
        _vocab.GetWordsAsync(Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<IReadOnlyList<string>>(new List<string> { "Rohil" }));

        var svc = new WordReplacementService(MakeRepo(), _vocab);
        var result = await svc.ApplyAsync("Hello Rohill how are you", default);
        result.Should().Be("Hello Rohil how are you");
    }

    [Fact]
    public async Task ApplyAsync_corrects_serilog_misspelling()
    {
        _vocab.GetWordsAsync(Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<IReadOnlyList<string>>(new List<string> { "Serilog" }));

        var svc = new WordReplacementService(MakeRepo(), _vocab);
        var result = await svc.ApplyAsync("We use Seralog for logging", default);
        result.Should().Be("We use Serilog for logging");
    }

    [Fact]
    public async Task ApplyAsync_does_not_correct_exact_vocabulary_match()
    {
        _vocab.GetWordsAsync(Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<IReadOnlyList<string>>(new List<string> { "Rohil" }));

        var svc = new WordReplacementService(MakeRepo(), _vocab);
        var result = await svc.ApplyAsync("Hello Rohil", default);
        result.Should().Be("Hello Rohil");
    }

    [Fact]
    public async Task ApplyAsync_skips_fuzzy_match_with_different_first_letter()
    {
        _vocab.GetWordsAsync(Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<IReadOnlyList<string>>(new List<string> { "Kubernetes" }));

        var svc = new WordReplacementService(MakeRepo(), _vocab);
        // "Tubernetes" starts with T, not K — should not match
        var result = await svc.ApplyAsync("Using Tubernetes for orchestration", default);
        result.Should().Be("Using Tubernetes for orchestration");
    }

    [Fact]
    public async Task ApplyAsync_skips_fuzzy_match_for_short_words()
    {
        _vocab.GetWordsAsync(Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<IReadOnlyList<string>>(new List<string> { "cat" }));

        var svc = new WordReplacementService(MakeRepo(), _vocab);
        // "car" is edit distance 1 from "cat" but both are < 4 chars
        var result = await svc.ApplyAsync("The car is red", default);
        result.Should().Be("The car is red");
    }

    [Fact]
    public async Task ApplyAsync_skips_fuzzy_match_beyond_edit_distance_2()
    {
        _vocab.GetWordsAsync(Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<IReadOnlyList<string>>(new List<string> { "Kubernetes" }));

        var svc = new WordReplacementService(MakeRepo(), _vocab);
        // "Kubanatees" has too many edits from "Kubernetes"
        var result = await svc.ApplyAsync("Deploy to Kubanatees", default);
        result.Should().Be("Deploy to Kubanatees");
    }

    // ── Levenshtein distance unit tests ─────────────────────────

    [Theory]
    [InlineData("rohil", "rohill", 1)]
    [InlineData("serilog", "seralog", 1)]
    [InlineData("kubernetes", "kubernets", 1)]
    [InlineData("hello", "hello", 0)]
    [InlineData("", "abc", 3)]
    [InlineData("abc", "", 3)]
    public void LevenshteinDistance_computes_correctly(string a, string b, int expected)
    {
        WordReplacementService.LevenshteinDistance(a, b).Should().Be(expected);
    }
}
