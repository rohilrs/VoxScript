using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using VoxScript.Core.History;
using VoxScript.Core.Persistence;
using VoxScript.Infrastructure;

namespace VoxScript.ViewModels;

public sealed partial class HistoryViewModel : ObservableObject
{
    private readonly ITranscriptionRepository _repo;
    private const int ChunkDays = 30;

    /// <summary>The oldest date we've loaded so far (exclusive lower bound).</summary>
    private DateTimeOffset _loadedUntil;

    public HistoryViewModel()
    {
        _repo = ServiceLocator.Get<ITranscriptionRepository>();
    }

    public ObservableCollection<HistoryDateGroup> Groups { get; } = new();

    [ObservableProperty]
    public partial bool HasMore { get; set; }

    [ObservableProperty]
    public partial bool IsEmpty { get; set; }

    [ObservableProperty]
    public partial string SearchQuery { get; set; } = "";

    [ObservableProperty]
    public partial bool IsSearching { get; set; }

    // ── Initial load ────────────────────────────────────────────

    public async Task LoadAsync()
    {
        Groups.Clear();
        _loadedUntil = DateTimeOffset.UtcNow;

        await LoadChunkAsync();
        UpdateEmptyState();
    }

    // ── Load more (next 30-day chunk) ───────────────────────────

    public async Task LoadMoreAsync()
    {
        await LoadChunkAsync();
        UpdateEmptyState();
    }

    private async Task LoadChunkAsync()
    {
        var to = _loadedUntil;
        var from = to.AddDays(-ChunkDays);

        var records = await _repo.GetRangeAsync(from, to, CancellationToken.None);
        _loadedUntil = from;

        AddRecordsToGroups(records);

        // Check if there's anything older
        var older = await _repo.GetRangeAsync(DateTimeOffset.MinValue, from, CancellationToken.None);
        HasMore = older.Count > 0;
    }

    // ── Search ──────────────────────────────────────────────────

    public async Task SearchAsync()
    {
        var query = SearchQuery.Trim();
        if (string.IsNullOrEmpty(query))
        {
            IsSearching = false;
            await LoadAsync();
            return;
        }

        IsSearching = true;
        Groups.Clear();
        HasMore = false;

        var results = await _repo.SearchAsync(query, 200, CancellationToken.None);
        AddRecordsToGroups(results);
        UpdateEmptyState();
    }

    // ── Delete ──────────────────────────────────────────────────

    public async Task DeleteAsync(HistoryItem item)
    {
        await _repo.DeleteAsync(item.Id, CancellationToken.None);

        // Remove from the group
        foreach (var group in Groups)
        {
            if (group.Items.Remove(item))
            {
                if (group.Items.Count == 0)
                    Groups.Remove(group);
                break;
            }
        }
        UpdateEmptyState();
    }

    // ── Helpers ─────────────────────────────────────────────────

    private void AddRecordsToGroups(IReadOnlyList<TranscriptionRecord> records)
    {
        var now = DateTimeOffset.Now;
        var today = now.Date;
        var yesterday = today.AddDays(-1);

        foreach (var record in records)
        {
            var localDate = record.CreatedAt.LocalDateTime.Date;
            string label;
            if (localDate == today)
                label = $"Today — {localDate:MMMM d, yyyy}";
            else if (localDate == yesterday)
                label = $"Yesterday — {localDate:MMMM d, yyyy}";
            else
                label = localDate.ToString("MMMM d, yyyy");

            var group = Groups.FirstOrDefault(g => g.DateLabel == label);
            if (group is null)
            {
                group = new HistoryDateGroup(label, localDate);
                // Insert in sorted order (newest first)
                int idx = 0;
                while (idx < Groups.Count && Groups[idx].SortDate > localDate)
                    idx++;
                Groups.Insert(idx, group);
            }

            var historyItem = new HistoryItem(record);
            // Insert within group sorted by time descending
            int itemIdx = 0;
            while (itemIdx < group.Items.Count && group.Items[itemIdx].CreatedAt > record.CreatedAt)
                itemIdx++;
            group.Items.Insert(itemIdx, historyItem);
        }
    }

    private void UpdateEmptyState()
    {
        IsEmpty = Groups.Count == 0;
    }
}

public sealed class HistoryDateGroup
{
    public string DateLabel { get; }
    public DateTime SortDate { get; }
    public ObservableCollection<HistoryItem> Items { get; } = new();

    public HistoryDateGroup(string dateLabel, DateTime sortDate)
    {
        DateLabel = dateLabel;
        SortDate = sortDate;
    }
}

public sealed class HistoryItem
{
    public int Id { get; }
    public string Text { get; }
    public string? EnhancedText { get; }
    public string TimeDisplay { get; }
    public string? ModelName { get; }
    public bool WasAiEnhanced { get; }
    public DateTimeOffset CreatedAt { get; }

    /// <summary>Show enhanced text if available, otherwise raw text.</summary>
    public string DisplayText => EnhancedText ?? Text;

    public HistoryItem(TranscriptionRecord record)
    {
        Id = record.Id;
        Text = record.Text;
        EnhancedText = record.EnhancedText;
        TimeDisplay = record.CreatedAt.LocalDateTime.ToString("h:mm tt");
        ModelName = record.ModelName;
        WasAiEnhanced = record.WasAiEnhanced;
        CreatedAt = record.CreatedAt;
    }
}
