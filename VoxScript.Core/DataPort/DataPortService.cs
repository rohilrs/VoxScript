using System.Text.Json;
using VoxScript.Core.Dictionary;
using VoxScript.Core.Persistence;

namespace VoxScript.Core.DataPort;

public sealed class DataPortService : IDataPortService
{
    private readonly IVocabularyRepository _vocabulary;
    private readonly ICorrectionRepository _corrections;
    private readonly IWordReplacementRepository _expansions;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
    };

    public DataPortService(
        IVocabularyRepository vocabulary,
        ICorrectionRepository corrections,
        IWordReplacementRepository expansions)
    {
        _vocabulary = vocabulary;
        _corrections = corrections;
        _expansions = expansions;
    }

    public async Task<ExportResult> ExportAsync(Stream output, CancellationToken ct)
    {
        var words = await _vocabulary.GetWordsAsync(ct);
        var corrections = await _corrections.GetAllAsync(ct);
        var expansions = await _expansions.GetAllAsync(ct);

        var payload = new DataPortPayload
        {
            Version = 1,
            ExportedAt = DateTimeOffset.UtcNow,
            Vocabulary = words.ToList(),
            Corrections = corrections.Select(c => new CorrectionDto
            {
                Wrong = c.Wrong,
                Correct = c.Correct,
            }).ToList(),
            Expansions = expansions.Select(e => new ExpansionDto
            {
                Original = e.Original,
                Replacement = e.Replacement,
                CaseSensitive = e.CaseSensitive,
            }).ToList(),
        };

        await JsonSerializer.SerializeAsync(output, payload, JsonOptions, ct);

        return new ExportResult
        {
            VocabularyCount = words.Count,
            CorrectionsCount = corrections.Count,
            ExpansionsCount = expansions.Count,
        };
    }

    public async Task<ImportResult> ImportAsync(Stream input, CancellationToken ct)
    {
        if (input.CanSeek && input.Length > 50 * 1024 * 1024)
            throw new InvalidOperationException("File is too large to import.");

        DataPortPayload? payload;
        try
        {
            payload = await JsonSerializer.DeserializeAsync<DataPortPayload>(input, cancellationToken: ct);
        }
        catch (JsonException)
        {
            throw new InvalidOperationException("Invalid file format.");
        }

        if (payload is null || payload.Version != 1)
            throw new InvalidOperationException("Invalid file format.");

        var existingWords = await _vocabulary.GetWordsAsync(ct);
        var existingCorrections = await _corrections.GetAllAsync(ct);
        var existingExpansions = await _expansions.GetAllAsync(ct);

        var wordSet = new HashSet<string>(existingWords, StringComparer.OrdinalIgnoreCase);
        var correctionSet = new HashSet<string>(
            existingCorrections.Select(c => c.Wrong), StringComparer.OrdinalIgnoreCase);
        var expansionSet = new HashSet<string>(
            existingExpansions.Select(e => e.Original), StringComparer.OrdinalIgnoreCase);

        int vocabAdded = 0, correctionsAdded = 0, expansionsAdded = 0, skipped = 0;

        foreach (var word in payload.Vocabulary)
        {
            if (string.IsNullOrWhiteSpace(word) || !wordSet.Add(word))
            {
                skipped++;
                continue;
            }
            await _vocabulary.AddWordAsync(word, ct);
            vocabAdded++;
        }

        foreach (var c in payload.Corrections)
        {
            if (string.IsNullOrWhiteSpace(c.Wrong) || string.IsNullOrWhiteSpace(c.Correct) || !correctionSet.Add(c.Wrong))
            {
                skipped++;
                continue;
            }
            await _corrections.AddAsync(new CorrectionRecord { Wrong = c.Wrong, Correct = c.Correct }, ct);
            correctionsAdded++;
        }

        foreach (var e in payload.Expansions)
        {
            if (string.IsNullOrWhiteSpace(e.Original) || string.IsNullOrWhiteSpace(e.Replacement) || !expansionSet.Add(e.Original))
            {
                skipped++;
                continue;
            }
            await _expansions.AddAsync(new WordReplacementRecord
            {
                Original = e.Original,
                Replacement = e.Replacement,
                CaseSensitive = e.CaseSensitive,
            }, ct);
            expansionsAdded++;
        }

        return new ImportResult
        {
            VocabularyAdded = vocabAdded,
            CorrectionsAdded = correctionsAdded,
            ExpansionsAdded = expansionsAdded,
            Skipped = skipped,
        };
    }
}
