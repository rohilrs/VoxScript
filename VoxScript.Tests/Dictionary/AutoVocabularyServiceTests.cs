using FluentAssertions;
using NSubstitute;
using VoxScript.Core.Dictionary;
using Xunit;

namespace VoxScript.Tests.Dictionary;

public sealed class AutoVocabularyServiceTests
{
    private readonly IVocabularyRepository _repo = Substitute.For<IVocabularyRepository>();
    private readonly ICommonWordList _commonWords = Substitute.For<ICommonWordList>();
    private readonly AutoVocabularyService _service;

    public AutoVocabularyServiceTests()
    {
        _repo.GetWordsAsync(Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<IReadOnlyList<string>>(new List<string>()));
        _service = new AutoVocabularyService(_repo, _commonWords);
    }

    [Fact]
    public async Task Adds_uncommon_words_to_vocabulary()
    {
        _commonWords.Contains("hello").Returns(true);
        _commonWords.Contains("kubernetes").Returns(false);

        await _service.ProcessTranscriptionAsync("hello Kubernetes", default);

        await _repo.Received(1).AddWordAsync("Kubernetes", Arg.Any<CancellationToken>());
        await _repo.DidNotReceive().AddWordAsync("hello", Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Skips_single_character_words()
    {
        _commonWords.Contains("a").Returns(false);
        _commonWords.Contains("i").Returns(false);

        await _service.ProcessTranscriptionAsync("a I test", default);

        await _repo.DidNotReceive().AddWordAsync("a", Arg.Any<CancellationToken>());
        await _repo.DidNotReceive().AddWordAsync("I", Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Skips_pure_numbers()
    {
        _commonWords.Contains("123").Returns(false);
        _commonWords.Contains("45").Returns(false);

        await _service.ProcessTranscriptionAsync("123 45 test", default);

        await _repo.DidNotReceive().AddWordAsync("123", Arg.Any<CancellationToken>());
        await _repo.DidNotReceive().AddWordAsync("45", Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Skips_words_already_in_vocabulary()
    {
        _commonWords.Contains("fastapi").Returns(false);
        _repo.GetWordsAsync(Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<IReadOnlyList<string>>(new List<string> { "FastAPI" }));

        await _service.ProcessTranscriptionAsync("FastAPI is great", default);

        await _repo.DidNotReceive().AddWordAsync("FastAPI", Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Deduplicates_within_same_transcription()
    {
        _commonWords.Contains("kubernetes").Returns(false);

        await _service.ProcessTranscriptionAsync("Kubernetes and Kubernetes again", default);

        await _repo.Received(1).AddWordAsync("Kubernetes", Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handles_empty_and_null_text()
    {
        await _service.ProcessTranscriptionAsync("", default);
        await _service.ProcessTranscriptionAsync(null!, default);

        await _repo.DidNotReceive().AddWordAsync(Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Strips_punctuation_from_words()
    {
        _commonWords.Contains("kubernetes").Returns(false);
        _commonWords.Contains("great").Returns(true);

        await _service.ProcessTranscriptionAsync("Kubernetes, great!", default);

        await _repo.Received(1).AddWordAsync("Kubernetes", Arg.Any<CancellationToken>());
    }
}
