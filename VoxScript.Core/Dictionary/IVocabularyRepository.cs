namespace VoxScript.Core.Dictionary;

public interface IVocabularyRepository
{
    Task<IReadOnlyList<string>> GetWordsAsync(CancellationToken ct);
    Task AddWordAsync(string word, CancellationToken ct);
    Task DeleteWordAsync(string word, CancellationToken ct);
}
