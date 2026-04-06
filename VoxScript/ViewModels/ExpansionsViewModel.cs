using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using VoxScript.Core.Dictionary;
using VoxScript.Core.Persistence;
using VoxScript.Infrastructure;

namespace VoxScript.ViewModels;

public enum SortMode { Newest, Oldest, Alphabetical }

public sealed partial class ExpansionsViewModel : ObservableObject
{
    private readonly IWordReplacementRepository _repo;
    private List<ExpansionItem> _allItems = new();

    public ExpansionsViewModel()
    {
        _repo = ServiceLocator.Get<IWordReplacementRepository>();
    }

    public ObservableCollection<ExpansionItem> Expansions { get; } = new();

    [ObservableProperty]
    public partial SortMode CurrentSort { get; set; } = SortMode.Newest;

    partial void OnCurrentSortChanged(SortMode value) => ApplySort();

    public int Count => Expansions.Count;
    public bool HasExpansions => Expansions.Count > 0;

    // ── Load ────────────────────────────────────────────────────

    public async Task LoadAsync()
    {
        var records = await _repo.GetAllAsync(CancellationToken.None);
        _allItems = records.Select(r => new ExpansionItem(r)).ToList();
        ApplySort();
    }

    private void ApplySort()
    {
        var sorted = CurrentSort switch
        {
            SortMode.Newest => _allItems.OrderByDescending(x => x.Id),
            SortMode.Oldest => _allItems.OrderBy(x => x.Id),
            SortMode.Alphabetical => _allItems.OrderBy(x => x.Original, StringComparer.OrdinalIgnoreCase),
            _ => _allItems.AsEnumerable(),
        };

        Expansions.Clear();
        foreach (var item in sorted)
            Expansions.Add(item);
        OnPropertyChanged(nameof(Count));
        OnPropertyChanged(nameof(HasExpansions));
    }

    // ── Add ─────────────────────────────────────────────────────

    public async Task AddAsync(string original, string replacement, bool caseSensitive)
    {
        original = original.Trim();
        replacement = replacement.Trim();
        if (string.IsNullOrEmpty(original) || string.IsNullOrEmpty(replacement)) return;

        var record = new WordReplacementRecord
        {
            Original = original,
            Replacement = replacement,
            CaseSensitive = caseSensitive,
        };
        await _repo.AddAsync(record, CancellationToken.None);

        var item = new ExpansionItem(record);
        _allItems.Add(item);
        ApplySort();
    }

    // ── Edit ────────────────────────────────────────────────────

    public async Task SaveEditAsync(ExpansionItem item, string original, string replacement, bool caseSensitive)
    {
        original = original.Trim();
        replacement = replacement.Trim();
        if (string.IsNullOrEmpty(original) || string.IsNullOrEmpty(replacement)) return;

        var record = new WordReplacementRecord
        {
            Id = item.Id,
            Original = original,
            Replacement = replacement,
            CaseSensitive = caseSensitive,
        };
        await _repo.UpdateAsync(record, CancellationToken.None);

        item.Original = original;
        item.Replacement = replacement;
        item.CaseSensitive = caseSensitive;
        ApplySort();
    }

    // ── Delete ──────────────────────────────────────────────────

    public async Task DeleteAsync(ExpansionItem item)
    {
        await _repo.DeleteAsync(item.Id, CancellationToken.None);
        _allItems.Remove(item);
        Expansions.Remove(item);
        OnPropertyChanged(nameof(Count));
        OnPropertyChanged(nameof(HasExpansions));
    }
}

public sealed partial class ExpansionItem : ObservableObject
{
    public int Id { get; }

    [ObservableProperty]
    public partial string Original { get; set; }

    [ObservableProperty]
    public partial string Replacement { get; set; }

    [ObservableProperty]
    public partial bool CaseSensitive { get; set; }

    public ExpansionItem(WordReplacementRecord record)
    {
        Id = record.Id;
        Original = record.Original;
        Replacement = record.Replacement;
        CaseSensitive = record.CaseSensitive;
    }
}
