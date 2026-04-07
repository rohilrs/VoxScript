namespace VoxScript.Core.Dictionary;

public interface IAutoVocabularyService
{
    Task ProcessTranscriptionAsync(string text, CancellationToken ct);
}
