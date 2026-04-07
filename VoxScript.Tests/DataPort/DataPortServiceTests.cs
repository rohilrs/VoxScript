using System.Text;
using System.Text.Json;
using FluentAssertions;
using NSubstitute;
using VoxScript.Core.DataPort;
using VoxScript.Core.Dictionary;
using VoxScript.Core.Persistence;
using Xunit;

namespace VoxScript.Tests.DataPort;

public sealed class DataPortServiceTests
{
    private readonly IVocabularyRepository _vocab = Substitute.For<IVocabularyRepository>();
    private readonly ICorrectionRepository _corrections = Substitute.For<ICorrectionRepository>();
    private readonly IWordReplacementRepository _expansions = Substitute.For<IWordReplacementRepository>();
    private readonly DataPortService _service;

    public DataPortServiceTests()
    {
        _vocab.GetWordsAsync(Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<IReadOnlyList<string>>(new List<string>()));
        _corrections.GetAllAsync(Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<IReadOnlyList<CorrectionRecord>>(new List<CorrectionRecord>()));
        _expansions.GetAllAsync(Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<IReadOnlyList<WordReplacementRecord>>(new List<WordReplacementRecord>()));

        _service = new DataPortService(_vocab, _corrections, _expansions);
    }

    [Fact]
    public async Task Export_writes_valid_json_with_all_sections()
    {
        _vocab.GetWordsAsync(Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<IReadOnlyList<string>>(new List<string> { "VoxScript", "Kubernetes" }));
        _corrections.GetAllAsync(Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<IReadOnlyList<CorrectionRecord>>(new List<CorrectionRecord>
            {
                new() { Wrong = "teh", Correct = "the" },
            }));
        _expansions.GetAllAsync(Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<IReadOnlyList<WordReplacementRecord>>(new List<WordReplacementRecord>
            {
                new() { Original = "brb", Replacement = "be right back", CaseSensitive = false },
            }));

        using var stream = new MemoryStream();
        var result = await _service.ExportAsync(stream, default);

        result.VocabularyCount.Should().Be(2);
        result.CorrectionsCount.Should().Be(1);
        result.ExpansionsCount.Should().Be(1);

        stream.Position = 0;
        var payload = await JsonSerializer.DeserializeAsync<DataPortPayload>(stream);
        payload!.Version.Should().Be(1);
        payload.Vocabulary.Should().BeEquivalentTo(["VoxScript", "Kubernetes"]);
        payload.Corrections.Should().HaveCount(1);
        payload.Corrections[0].Wrong.Should().Be("teh");
        payload.Expansions.Should().HaveCount(1);
        payload.Expansions[0].Original.Should().Be("brb");
    }

    [Fact]
    public async Task Import_adds_new_items_and_returns_counts()
    {
        var json = """
        {
            "version": 1,
            "exportedAt": "2026-04-06T00:00:00Z",
            "vocabulary": ["Rohil"],
            "corrections": [{ "wrong": "teh", "correct": "the" }],
            "expansions": [{ "original": "brb", "replacement": "be right back", "caseSensitive": false }]
        }
        """;
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes(json));

        var result = await _service.ImportAsync(stream, default);

        result.VocabularyAdded.Should().Be(1);
        result.CorrectionsAdded.Should().Be(1);
        result.ExpansionsAdded.Should().Be(1);
        result.Skipped.Should().Be(0);

        await _vocab.Received(1).AddWordAsync("Rohil", Arg.Any<CancellationToken>());
        await _corrections.Received(1).AddAsync(
            Arg.Is<CorrectionRecord>(c => c.Wrong == "teh" && c.Correct == "the"),
            Arg.Any<CancellationToken>());
        await _expansions.Received(1).AddAsync(
            Arg.Is<WordReplacementRecord>(e => e.Original == "brb"),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Import_skips_duplicates()
    {
        _vocab.GetWordsAsync(Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<IReadOnlyList<string>>(new List<string> { "Rohil" }));
        _corrections.GetAllAsync(Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<IReadOnlyList<CorrectionRecord>>(new List<CorrectionRecord>
            {
                new() { Wrong = "teh", Correct = "the" },
            }));

        var json = """
        {
            "version": 1,
            "exportedAt": "2026-04-06T00:00:00Z",
            "vocabulary": ["Rohil", "NewWord"],
            "corrections": [{ "wrong": "teh", "correct": "the" }, { "wrong": "hte", "correct": "the" }],
            "expansions": []
        }
        """;
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes(json));

        var result = await _service.ImportAsync(stream, default);

        result.VocabularyAdded.Should().Be(1);
        result.CorrectionsAdded.Should().Be(1);
        result.Skipped.Should().Be(2);

        await _vocab.Received(1).AddWordAsync("NewWord", Arg.Any<CancellationToken>());
        await _vocab.DidNotReceive().AddWordAsync("Rohil", Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Import_duplicate_detection_is_case_insensitive()
    {
        _vocab.GetWordsAsync(Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<IReadOnlyList<string>>(new List<string> { "Rohil" }));

        var json = """
        {
            "version": 1,
            "exportedAt": "2026-04-06T00:00:00Z",
            "vocabulary": ["rohil"],
            "corrections": [],
            "expansions": []
        }
        """;
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes(json));

        var result = await _service.ImportAsync(stream, default);

        result.VocabularyAdded.Should().Be(0);
        result.Skipped.Should().Be(1);
        await _vocab.DidNotReceive().AddWordAsync(Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Import_rejects_invalid_json()
    {
        var stream = new MemoryStream(Encoding.UTF8.GetBytes("not json"));

        var act = () => _service.ImportAsync(stream, default);

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("Invalid file format.");
    }

    [Fact]
    public async Task Import_rejects_wrong_version()
    {
        var json = """{ "version": 99, "vocabulary": [] }""";
        var stream = new MemoryStream(Encoding.UTF8.GetBytes(json));

        var act = () => _service.ImportAsync(stream, default);

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("Invalid file format.");
    }

    [Fact]
    public async Task Import_handles_missing_sections_gracefully()
    {
        var json = """
        {
            "version": 1,
            "exportedAt": "2026-04-06T00:00:00Z",
            "vocabulary": ["OnlyVocab"]
        }
        """;
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes(json));

        var result = await _service.ImportAsync(stream, default);

        result.VocabularyAdded.Should().Be(1);
        result.CorrectionsAdded.Should().Be(0);
        result.ExpansionsAdded.Should().Be(0);
    }

    [Fact]
    public async Task Import_skips_blank_entries()
    {
        var json = """
        {
            "version": 1,
            "exportedAt": "2026-04-06T00:00:00Z",
            "vocabulary": ["", "  ", "Valid"],
            "corrections": [{ "wrong": "", "correct": "x" }],
            "expansions": [{ "original": "  ", "replacement": "y", "caseSensitive": false }]
        }
        """;
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes(json));

        var result = await _service.ImportAsync(stream, default);

        result.VocabularyAdded.Should().Be(1);
        result.CorrectionsAdded.Should().Be(0);
        result.ExpansionsAdded.Should().Be(0);
        result.Skipped.Should().Be(4);
    }
}
