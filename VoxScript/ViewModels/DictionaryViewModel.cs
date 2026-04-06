using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using VoxScript.Core.Dictionary;
using VoxScript.Core.Persistence;
using VoxScript.Infrastructure;

namespace VoxScript.ViewModels;

public sealed partial class DictionaryViewModel : ObservableObject
{
    private readonly IVocabularyRepository _vocabRepo;
    private readonly ICorrectionRepository _corrRepo;
    private List<string> _allWords = new();
    private List<CorrectionItem> _allCorrections = new();

    public DictionaryViewModel()
    {
        _vocabRepo = ServiceLocator.Get<IVocabularyRepository>();
        _corrRepo = ServiceLocator.Get<ICorrectionRepository>();
    }

    // ── Words ───────────────────────────────────────────────────

    public ObservableCollection<string> Words { get; } = new();

    [ObservableProperty]
    public partial SortMode WordSort { get; set; } = SortMode.Newest;

    partial void OnWordSortChanged(SortMode value) => ApplyWordSort();

    public int WordCount => Words.Count;
    public bool HasWords => Words.Count > 0;

    public async Task LoadAsync()
    {
        var words = await _vocabRepo.GetWordsAsync(CancellationToken.None);
        _allWords = words.ToList();
        ApplyWordSort();

        var corrections = await _corrRepo.GetAllAsync(CancellationToken.None);
        _allCorrections = corrections.Select(r => new CorrectionItem(r)).ToList();
        ApplyCorrectionSort();
    }

    private void ApplyWordSort()
    {
        var sorted = WordSort switch
        {
            SortMode.Alphabetical => _allWords.OrderBy(w => w, StringComparer.OrdinalIgnoreCase),
            SortMode.Newest => _allWords.AsEnumerable().Reverse(), // Last added = newest (by list position)
            SortMode.Oldest => _allWords.AsEnumerable(),
            _ => _allWords.AsEnumerable(),
        };

        Words.Clear();
        foreach (var w in sorted)
            Words.Add(w);
        OnPropertyChanged(nameof(WordCount));
        OnPropertyChanged(nameof(HasWords));
    }

    public async Task AddWordAsync(string word)
    {
        word = word.Trim();
        if (string.IsNullOrEmpty(word)) return;
        if (_allWords.Contains(word, StringComparer.OrdinalIgnoreCase)) return;

        await _vocabRepo.AddWordAsync(word, CancellationToken.None);
        _allWords.Add(word);
        ApplyWordSort();
    }

    public async Task EditWordAsync(string oldWord, string newWord)
    {
        newWord = newWord.Trim();
        if (string.IsNullOrEmpty(newWord) || oldWord == newWord) return;

        await _vocabRepo.DeleteWordAsync(oldWord, CancellationToken.None);
        await _vocabRepo.AddWordAsync(newWord, CancellationToken.None);

        var idx = _allWords.IndexOf(oldWord);
        if (idx >= 0) _allWords[idx] = newWord;
        ApplyWordSort();
    }

    public async Task DeleteWordAsync(string word)
    {
        await _vocabRepo.DeleteWordAsync(word, CancellationToken.None);
        _allWords.Remove(word);
        Words.Remove(word);
        OnPropertyChanged(nameof(WordCount));
        OnPropertyChanged(nameof(HasWords));
    }

    // ── Corrections ─────────────────────────────────────────────

    public ObservableCollection<CorrectionItem> Corrections { get; } = new();

    [ObservableProperty]
    public partial SortMode CorrectionSort { get; set; } = SortMode.Newest;

    partial void OnCorrectionSortChanged(SortMode value) => ApplyCorrectionSort();

    public int CorrectionCount => Corrections.Count;
    public bool HasCorrections => Corrections.Count > 0;

    private void ApplyCorrectionSort()
    {
        var sorted = CorrectionSort switch
        {
            SortMode.Newest => _allCorrections.OrderByDescending(x => x.Id),
            SortMode.Oldest => _allCorrections.OrderBy(x => x.Id),
            SortMode.Alphabetical => _allCorrections.OrderBy(x => x.Wrong, StringComparer.OrdinalIgnoreCase),
            _ => _allCorrections.AsEnumerable(),
        };

        Corrections.Clear();
        foreach (var c in sorted)
            Corrections.Add(c);
        OnPropertyChanged(nameof(CorrectionCount));
        OnPropertyChanged(nameof(HasCorrections));
    }

    public async Task AddCorrectionAsync(string wrong, string correct)
    {
        wrong = wrong.Trim();
        correct = correct.Trim();
        if (string.IsNullOrEmpty(wrong) || string.IsNullOrEmpty(correct)) return;

        var record = new CorrectionRecord { Wrong = wrong, Correct = correct };
        await _corrRepo.AddAsync(record, CancellationToken.None);

        var item = new CorrectionItem(record);
        _allCorrections.Add(item);
        ApplyCorrectionSort();
    }

    public async Task EditCorrectionAsync(CorrectionItem item, string wrong, string correct)
    {
        wrong = wrong.Trim();
        correct = correct.Trim();
        if (string.IsNullOrEmpty(wrong) || string.IsNullOrEmpty(correct)) return;

        var record = new CorrectionRecord { Id = item.Id, Wrong = wrong, Correct = correct };
        await _corrRepo.UpdateAsync(record, CancellationToken.None);

        item.Wrong = wrong;
        item.Correct = correct;
        ApplyCorrectionSort();
    }

    public async Task DeleteCorrectionAsync(CorrectionItem item)
    {
        await _corrRepo.DeleteAsync(item.Id, CancellationToken.None);
        _allCorrections.Remove(item);
        Corrections.Remove(item);
        OnPropertyChanged(nameof(CorrectionCount));
        OnPropertyChanged(nameof(HasCorrections));
    }
}

public sealed partial class CorrectionItem : ObservableObject
{
    public int Id { get; }

    [ObservableProperty]
    public partial string Wrong { get; set; }

    [ObservableProperty]
    public partial string Correct { get; set; }

    public CorrectionItem(CorrectionRecord record)
    {
        Id = record.Id;
        Wrong = record.Wrong;
        Correct = record.Correct;
    }
}
