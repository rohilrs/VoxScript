// VoxScript/ViewModels/NotesViewModel.cs
using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using VoxScript.Core.Notes;
using VoxScript.Infrastructure;

namespace VoxScript.ViewModels;

public sealed partial class NotesViewModel : ObservableObject
{
    private readonly INoteRepository _repo;
    private List<NoteItem> _allItems = new();

    public NotesViewModel()
    {
        _repo = ServiceLocator.Get<INoteRepository>();
    }

    public ObservableCollection<NoteItem> Notes { get; } = new();

    [ObservableProperty]
    public partial NoteItem? SelectedNote { get; set; }

    [ObservableProperty]
    public partial SortMode CurrentSort { get; set; } = SortMode.Newest;

    [ObservableProperty]
    public partial string SearchQuery { get; set; } = "";

    [ObservableProperty]
    public partial bool IsEmpty { get; set; }

    [ObservableProperty]
    public partial string SaveStatus { get; set; } = "";

    partial void OnCurrentSortChanged(SortMode value) => ApplySort();

    public async Task LoadAsync()
    {
        var records = await _repo.GetAllAsync(CancellationToken.None);
        _allItems = records.Select(r => new NoteItem(r)).ToList();
        ApplySort();
        UpdateEmptyState();
    }

    public async Task SearchAsync()
    {
        var query = SearchQuery.Trim();
        if (string.IsNullOrEmpty(query))
        {
            await LoadAsync();
            return;
        }

        var results = await _repo.SearchAsync(query, CancellationToken.None);
        _allItems = results.Select(r => new NoteItem(r)).ToList();
        ApplySort();
        UpdateEmptyState();
    }

    private void ApplySort()
    {
        var sorted = CurrentSort switch
        {
            SortMode.Newest => _allItems.OrderByDescending(x => x.ModifiedAt),
            SortMode.Oldest => _allItems.OrderBy(x => x.ModifiedAt),
            SortMode.Alphabetical => _allItems.OrderBy(x => x.Title, StringComparer.OrdinalIgnoreCase),
            _ => _allItems.AsEnumerable(),
        };

        Notes.Clear();
        foreach (var item in sorted)
            Notes.Add(item);
    }

    public async Task<NoteItem> CreateAsync()
    {
        var record = new NoteRecord
        {
            Title = "Untitled",
            CreatedAt = DateTime.UtcNow,
            ModifiedAt = DateTime.UtcNow,
        };
        await _repo.CreateAsync(record, CancellationToken.None);

        var item = new NoteItem(record);
        _allItems.Add(item);
        ApplySort();
        UpdateEmptyState();
        SelectedNote = item;
        return item;
    }

    public async Task<NoteItem> CreateFromTranscriptionAsync(int transcriptionId, string text)
    {
        var title = text.Length > 50 ? text[..50].TrimEnd() + "..." : text;
        if (string.IsNullOrWhiteSpace(title)) title = "Saved transcription";

        var record = new NoteRecord
        {
            Title = title,
            ContentPlainText = text,
            IsStarred = true,
            SourceTranscriptionId = transcriptionId,
            CreatedAt = DateTime.UtcNow,
            ModifiedAt = DateTime.UtcNow,
        };
        await _repo.CreateAsync(record, CancellationToken.None);

        var item = new NoteItem(record);
        _allItems.Add(item);
        ApplySort();
        UpdateEmptyState();
        return item;
    }

    public async Task SaveNoteAsync(NoteItem item, string title, string contentRtf, string contentPlainText)
    {
        var record = new NoteRecord
        {
            Id = item.Id,
            Title = title.Trim(),
            ContentRtf = contentRtf,
            ContentPlainText = contentPlainText,
            IsStarred = item.IsStarred,
            SourceTranscriptionId = item.SourceTranscriptionId,
            CreatedAt = item.CreatedAt,
            ModifiedAt = DateTime.UtcNow,
        };
        await _repo.UpdateAsync(record, CancellationToken.None);

        item.Title = record.Title;
        item.ContentRtf = record.ContentRtf;
        item.ContentPlainText = record.ContentPlainText;
        item.ModifiedAt = record.ModifiedAt;
        SaveStatus = "\u2713 Saved";
    }

    public async Task DeleteAsync(NoteItem item)
    {
        await _repo.DeleteAsync(item.Id, CancellationToken.None);
        _allItems.Remove(item);
        Notes.Remove(item);

        if (SelectedNote == item)
            SelectedNote = Notes.FirstOrDefault();

        UpdateEmptyState();
    }

    private void UpdateEmptyState() => IsEmpty = Notes.Count == 0;

    public void SelectById(int noteId)
    {
        SelectedNote = Notes.FirstOrDefault(n => n.Id == noteId);
    }
}

public sealed partial class NoteItem : ObservableObject
{
    public int Id { get; }
    public bool IsStarred { get; }
    public int? SourceTranscriptionId { get; }
    public DateTime CreatedAt { get; }

    [ObservableProperty]
    public partial string Title { get; set; }

    [ObservableProperty]
    public partial string ContentRtf { get; set; }

    [ObservableProperty]
    public partial string ContentPlainText { get; set; }

    [ObservableProperty]
    public partial DateTime ModifiedAt { get; set; }

    public string Preview
    {
        get
        {
            if (string.IsNullOrWhiteSpace(ContentPlainText)) return "";
            return ContentPlainText.Length > 100 ? ContentPlainText[..100] : ContentPlainText;
        }
    }

    public string TimeDisplay
    {
        get
        {
            var local = ModifiedAt.ToLocalTime();
            var today = DateTime.Now.Date;
            if (local.Date == today)
                return $"Today, {local:h:mm tt}";
            if (local.Date == today.AddDays(-1))
                return $"Yesterday, {local:h:mm tt}";
            return local.ToString("MMM d, h:mm tt");
        }
    }

    public NoteItem(NoteRecord record)
    {
        Id = record.Id;
        Title = record.Title;
        ContentRtf = record.ContentRtf;
        ContentPlainText = record.ContentPlainText;
        IsStarred = record.IsStarred;
        SourceTranscriptionId = record.SourceTranscriptionId;
        CreatedAt = record.CreatedAt;
        ModifiedAt = record.ModifiedAt;
    }
}
