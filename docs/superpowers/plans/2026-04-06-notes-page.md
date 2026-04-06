# Notes Page Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Build a Notes feature with two surfaces — a list view in the main window's Notes tab, and a separate editor window with master-detail layout, rich text editing, and History star-to-note integration.

**Architecture:** Data layer lives in `VoxScript.Core/Notes/` (entity, interface, repository). A single `NotesViewModel` (CommunityToolkit.Mvvm) is shared between both UI surfaces. The main window `NotesPage` shows a full-width card list; clicking a card opens `NoteEditorWindow` (in `VoxScript/Shell/`) which has a sidebar list + `RichEditBox` editor. The editor window is a singleton — created once, shown/hidden on demand.

**Tech Stack:** EF Core + SQLite (schema via `EnsureCreated` + raw SQL fallback), CommunityToolkit.Mvvm, WinUI 3 `RichEditBox`, `DispatcherTimer` debounce, Win32 clipboard (existing pattern).

---

## File Structure

| File | Action | Purpose |
|------|--------|---------|
| `VoxScript.Core/Notes/NoteRecord.cs` | Create | EF entity for `Notes` table |
| `VoxScript.Core/Notes/INoteRepository.cs` | Create | Core repository interface |
| `VoxScript.Core/Notes/NoteRepository.cs` | Create | EF Core implementation |
| `VoxScript.Core/Persistence/AppDbContext.cs` | Modify | Add `DbSet<NoteRecord>` |
| `VoxScript/Infrastructure/AppBootstrapper.cs` | Modify | Register `INoteRepository` |
| `VoxScript/App.xaml.cs` | Modify | Add `CREATE TABLE IF NOT EXISTS Notes` raw SQL + editor window singleton |
| `VoxScript/ViewModels/NotesViewModel.cs` | Create | Shared ViewModel: list, search, sort, selected note, auto-save |
| `VoxScript/Views/NotesPage.xaml` | Modify | Full-width note list (replaces placeholder) |
| `VoxScript/Views/NotesPage.xaml.cs` | Modify | Card building, search, sort, open editor window |
| `VoxScript/Shell/NoteEditorWindow.xaml` | Create | Master-detail XAML layout |
| `VoxScript/Shell/NoteEditorWindow.xaml.cs` | Create | Toolbar wiring, RichEditBox, sidebar cards, auto-save |
| `VoxScript/Views/HistoryPage.xaml.cs` | Modify | Add star button to each history card |
| `VoxScript.Tests/Notes/NoteRepositoryTests.cs` | Create | xUnit tests for NoteRepository |

---

### Task 1: NoteRecord entity

**Files:**
- Create: `VoxScript.Core/Notes/NoteRecord.cs`

- [ ] **Step 1: Create the entity file**

```csharp
// VoxScript.Core/Notes/NoteRecord.cs
using System.ComponentModel.DataAnnotations;
using Microsoft.EntityFrameworkCore;

namespace VoxScript.Core.Notes;

[Index(nameof(ModifiedAt))]
public sealed class NoteRecord
{
    public int Id { get; set; }

    [Required, MaxLength(200)]
    public string Title { get; set; } = string.Empty;

    public string ContentRtf { get; set; } = string.Empty;

    public string ContentPlainText { get; set; } = string.Empty;

    public bool IsStarred { get; set; }

    public int? SourceTranscriptionId { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public DateTime ModifiedAt { get; set; } = DateTime.UtcNow;
}
```

- [ ] **Step 2: Commit**

```bash
git add VoxScript.Core/Notes/NoteRecord.cs
git commit -m "feat: add NoteRecord entity"
```

---

### Task 2: INoteRepository interface

**Files:**
- Create: `VoxScript.Core/Notes/INoteRepository.cs`

- [ ] **Step 1: Create the interface**

```csharp
// VoxScript.Core/Notes/INoteRepository.cs
namespace VoxScript.Core.Notes;

public interface INoteRepository
{
    Task<IReadOnlyList<NoteRecord>> GetAllAsync(CancellationToken ct);
    Task<NoteRecord?> GetByIdAsync(int id, CancellationToken ct);
    Task<IReadOnlyList<NoteRecord>> SearchAsync(string query, CancellationToken ct);
    Task<NoteRecord> CreateAsync(NoteRecord note, CancellationToken ct);
    Task UpdateAsync(NoteRecord note, CancellationToken ct);
    Task DeleteAsync(int id, CancellationToken ct);
}
```

- [ ] **Step 2: Commit**

```bash
git add VoxScript.Core/Notes/INoteRepository.cs
git commit -m "feat: add INoteRepository interface"
```

---

### Task 3: Add NoteRecord to AppDbContext

**Files:**
- Modify: `VoxScript.Core/Persistence/AppDbContext.cs`

- [ ] **Step 1: Add DbSet and using**

Add `using VoxScript.Core.Notes;` at the top. Add the property alongside the existing DbSets:

```csharp
public DbSet<NoteRecord> Notes => Set<NoteRecord>();
```

The full file should look like:

```csharp
using Microsoft.EntityFrameworkCore;
using VoxScript.Core.Notes;

namespace VoxScript.Core.Persistence;

public sealed class AppDbContext : DbContext
{
    public DbSet<TranscriptionRecord> Transcriptions => Set<TranscriptionRecord>();
    public DbSet<VocabularyWordRecord> VocabularyWords => Set<VocabularyWordRecord>();
    public DbSet<WordReplacementRecord> WordReplacements => Set<WordReplacementRecord>();
    public DbSet<CorrectionRecord> Corrections => Set<CorrectionRecord>();
    public DbSet<PowerModeConfigRecord> PowerModeConfigs => Set<PowerModeConfigRecord>();
    public DbSet<NoteRecord> Notes => Set<NoteRecord>();

    public AppDbContext(DbContextOptions<AppDbContext> options) : base(options) { }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<TranscriptionRecord>()
            .Property(e => e.CreatedAt)
            .HasConversion(
                v => v.ToUnixTimeMilliseconds(),
                v => DateTimeOffset.FromUnixTimeMilliseconds(v));
    }
}
```

- [ ] **Step 2: Add raw SQL fallback in App.xaml.cs**

In `App.xaml.cs` inside `OnLaunched`, after the existing `CREATE TABLE IF NOT EXISTS PowerModeConfigs` block, add:

```csharp
db.Database.ExecuteSqlRaw("""
    CREATE TABLE IF NOT EXISTS Notes (
        Id INTEGER PRIMARY KEY AUTOINCREMENT,
        Title TEXT NOT NULL DEFAULT '',
        ContentRtf TEXT NOT NULL DEFAULT '',
        ContentPlainText TEXT NOT NULL DEFAULT '',
        IsStarred INTEGER NOT NULL DEFAULT 0,
        SourceTranscriptionId INTEGER,
        CreatedAt TEXT NOT NULL DEFAULT '0001-01-01T00:00:00',
        ModifiedAt TEXT NOT NULL DEFAULT '0001-01-01T00:00:00'
    )
    """);
```

- [ ] **Step 3: Commit**

```bash
git add VoxScript.Core/Persistence/AppDbContext.cs VoxScript/App.xaml.cs
git commit -m "feat: add Notes DbSet to AppDbContext with raw SQL fallback"
```

---

### Task 4: NoteRepository implementation + tests

**Files:**
- Create: `VoxScript.Core/Notes/NoteRepository.cs`
- Create: `VoxScript.Tests/Notes/NoteRepositoryTests.cs`

- [ ] **Step 1: Write the failing tests**

```csharp
// VoxScript.Tests/Notes/NoteRepositoryTests.cs
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using VoxScript.Core.Notes;
using VoxScript.Core.Persistence;
using Xunit;

namespace VoxScript.Tests.Notes;

public sealed class NoteRepositoryTests : IDisposable
{
    private readonly AppDbContext _db;
    private readonly NoteRepository _repo;

    public NoteRepositoryTests()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlite("Data Source=:memory:")
            .Options;
        _db = new AppDbContext(options);
        _db.Database.OpenConnection();
        _db.Database.EnsureCreated();
        _repo = new NoteRepository(_db);
    }

    public void Dispose() => _db.Dispose();

    [Fact]
    public async Task CreateAsync_persists_and_returns_record_with_id()
    {
        var note = new NoteRecord { Title = "Test Note", ContentPlainText = "hello" };
        var saved = await _repo.CreateAsync(note, default);
        saved.Id.Should().BeGreaterThan(0);
        saved.Title.Should().Be("Test Note");
    }

    [Fact]
    public async Task GetAllAsync_returns_ordered_by_modified_descending()
    {
        var older = new NoteRecord { Title = "Old", ModifiedAt = DateTime.UtcNow.AddDays(-2) };
        var newer = new NoteRecord { Title = "New", ModifiedAt = DateTime.UtcNow };
        await _repo.CreateAsync(older, default);
        await _repo.CreateAsync(newer, default);

        var all = await _repo.GetAllAsync(default);
        all.Should().HaveCount(2);
        all[0].Title.Should().Be("New");
        all[1].Title.Should().Be("Old");
    }

    [Fact]
    public async Task SearchAsync_finds_by_title_and_content()
    {
        await _repo.CreateAsync(new NoteRecord { Title = "Meeting notes", ContentPlainText = "discuss Q2" }, default);
        await _repo.CreateAsync(new NoteRecord { Title = "Shopping", ContentPlainText = "buy groceries" }, default);

        var byTitle = await _repo.SearchAsync("Meeting", default);
        byTitle.Should().HaveCount(1);
        byTitle[0].Title.Should().Be("Meeting notes");

        var byContent = await _repo.SearchAsync("groceries", default);
        byContent.Should().HaveCount(1);
        byContent[0].Title.Should().Be("Shopping");
    }

    [Fact]
    public async Task UpdateAsync_modifies_fields()
    {
        var note = await _repo.CreateAsync(new NoteRecord { Title = "Draft" }, default);
        note.Title = "Final";
        note.ContentPlainText = "updated content";
        await _repo.UpdateAsync(note, default);

        var fetched = await _repo.GetByIdAsync(note.Id, default);
        fetched!.Title.Should().Be("Final");
        fetched.ContentPlainText.Should().Be("updated content");
    }

    [Fact]
    public async Task DeleteAsync_removes_record()
    {
        var note = await _repo.CreateAsync(new NoteRecord { Title = "Delete me" }, default);
        await _repo.DeleteAsync(note.Id, default);

        var fetched = await _repo.GetByIdAsync(note.Id, default);
        fetched.Should().BeNull();
    }
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test VoxScript.Tests --filter "FullyQualifiedName~NoteRepositoryTests" -v n`
Expected: Compilation error — `NoteRepository` does not exist yet.

- [ ] **Step 3: Write the repository implementation**

```csharp
// VoxScript.Core/Notes/NoteRepository.cs
using Microsoft.EntityFrameworkCore;
using VoxScript.Core.Persistence;

namespace VoxScript.Core.Notes;

public sealed class NoteRepository : INoteRepository
{
    private readonly AppDbContext _db;
    public NoteRepository(AppDbContext db) => _db = db;

    public async Task<IReadOnlyList<NoteRecord>> GetAllAsync(CancellationToken ct) =>
        await _db.Notes
            .OrderByDescending(n => n.ModifiedAt)
            .ToListAsync(ct);

    public Task<NoteRecord?> GetByIdAsync(int id, CancellationToken ct) =>
        _db.Notes.FirstOrDefaultAsync(n => n.Id == id, ct);

    public async Task<IReadOnlyList<NoteRecord>> SearchAsync(string query, CancellationToken ct) =>
        await _db.Notes
            .Where(n => EF.Functions.Like(n.Title, $"%{query}%")
                     || EF.Functions.Like(n.ContentPlainText, $"%{query}%"))
            .OrderByDescending(n => n.ModifiedAt)
            .ToListAsync(ct);

    public async Task<NoteRecord> CreateAsync(NoteRecord note, CancellationToken ct)
    {
        _db.Notes.Add(note);
        await _db.SaveChangesAsync(ct);
        return note;
    }

    public async Task UpdateAsync(NoteRecord note, CancellationToken ct)
    {
        _db.Notes.Update(note);
        await _db.SaveChangesAsync(ct);
    }

    public async Task DeleteAsync(int id, CancellationToken ct)
    {
        var note = await _db.Notes.FindAsync(new object[] { id }, ct);
        if (note is not null)
        {
            _db.Notes.Remove(note);
            await _db.SaveChangesAsync(ct);
        }
    }
}
```

- [ ] **Step 4: Run tests to verify they pass**

Run: `dotnet test VoxScript.Tests --filter "FullyQualifiedName~NoteRepositoryTests" -v n`
Expected: All 5 tests PASS.

- [ ] **Step 5: Commit**

```bash
git add VoxScript.Core/Notes/NoteRepository.cs VoxScript.Tests/Notes/NoteRepositoryTests.cs
git commit -m "feat: add NoteRepository with tests"
```

---

### Task 5: DI registration

**Files:**
- Modify: `VoxScript/Infrastructure/AppBootstrapper.cs`

- [ ] **Step 1: Register INoteRepository**

Add `using VoxScript.Core.Notes;` to the top of `AppBootstrapper.cs`.

Add the following line in the `Build()` method, after the `ITranscriptionRepository` registration (around line 74):

```csharp
services.AddSingleton<INoteRepository, NoteRepository>();
```

- [ ] **Step 2: Verify build compiles**

Run: `dotnet build VoxScript.slnx`
Expected: Build succeeded.

- [ ] **Step 3: Commit**

```bash
git add VoxScript/Infrastructure/AppBootstrapper.cs
git commit -m "feat: register INoteRepository in DI container"
```

---

### Task 6: NotesViewModel

**Files:**
- Create: `VoxScript/ViewModels/NotesViewModel.cs`

- [ ] **Step 1: Create the ViewModel**

This ViewModel is shared between the main window NotesPage and the editor window. It manages the note list, search, sort, selected note, and auto-save.

```csharp
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

    // ── Load ────────────────────────────────────────────────────

    public async Task LoadAsync()
    {
        var records = await _repo.GetAllAsync(CancellationToken.None);
        _allItems = records.Select(r => new NoteItem(r)).ToList();
        ApplySort();
        UpdateEmptyState();
    }

    // ── Search ──────────────────────────────────────────────────

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

    // ── Sort ────────────────────────────────────────────────────

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

    // ── Create ──────────────────────────────────────────────────

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

    /// <summary>
    /// Create a note from a starred transcription (called from History page).
    /// </summary>
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

    // ── Save ────────────────────────────────────────────────────

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

        // Update the in-memory item
        item.Title = record.Title;
        item.ContentRtf = record.ContentRtf;
        item.ContentPlainText = record.ContentPlainText;
        item.ModifiedAt = record.ModifiedAt;
        SaveStatus = "\u2713 Saved";
    }

    // ── Delete ──────────────────────────────────────────────────

    public async Task DeleteAsync(NoteItem item)
    {
        await _repo.DeleteAsync(item.Id, CancellationToken.None);
        _allItems.Remove(item);
        Notes.Remove(item);

        if (SelectedNote == item)
            SelectedNote = Notes.FirstOrDefault();

        UpdateEmptyState();
    }

    // ── Helpers ─────────────────────────────────────────────────

    private void UpdateEmptyState() => IsEmpty = Notes.Count == 0;

    /// <summary>Select a note by ID (used when opening editor from main window).</summary>
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
```

Note: `SortMode` enum already exists in `ExpansionsViewModel.cs`. Since it's reused, it stays there.

- [ ] **Step 2: Verify build compiles**

Run: `dotnet build VoxScript.slnx`
Expected: Build succeeded.

- [ ] **Step 3: Commit**

```bash
git add VoxScript/ViewModels/NotesViewModel.cs
git commit -m "feat: add NotesViewModel with search, sort, CRUD, auto-save"
```

---

### Task 7: NotesPage — main window list view

**Files:**
- Modify: `VoxScript/Views/NotesPage.xaml` (replace placeholder)
- Modify: `VoxScript/Views/NotesPage.xaml.cs` (replace placeholder)

- [ ] **Step 1: Replace NotesPage.xaml**

```xml
<?xml version="1.0" encoding="utf-8"?>
<Page
    x:Class="VoxScript.Views.NotesPage"
    xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
    xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
    Background="{StaticResource BrandBackgroundBrush}"
    Loaded="Page_Loaded">

    <ScrollViewer VerticalScrollBarVisibility="Auto" HorizontalScrollBarVisibility="Disabled">
        <StackPanel HorizontalAlignment="Stretch"
                    Padding="60,40" Spacing="24">

            <!-- Page Title + New button -->
            <Grid>
                <StackPanel Spacing="4">
                    <TextBlock Text="Notes"
                               FontFamily="Georgia" FontSize="40"
                               Foreground="{StaticResource BrandForegroundBrush}" />
                    <TextBlock Text="Your notes and saved transcriptions"
                               FontSize="14"
                               Foreground="{StaticResource BrandMutedBrush}" />
                </StackPanel>
                <Button Content="＋ New Note"
                        HorizontalAlignment="Right" VerticalAlignment="Top"
                        Click="NewButton_Click"
                        Background="{StaticResource BrandPrimaryBrush}"
                        Foreground="White"
                        FontSize="14" FontWeight="Medium"
                        CornerRadius="8" Padding="20,8" />
            </Grid>

            <!-- Search bar -->
            <Border Background="{StaticResource BrandCardBrush}"
                    CornerRadius="12" Padding="4"
                    BorderBrush="{StaticResource BrandPrimaryLightBrush}"
                    BorderThickness="1">
                <TextBox x:Name="SearchBox"
                         PlaceholderText="Search notes..."
                         FontSize="14"
                         BorderThickness="0" Background="Transparent"
                         TextChanged="SearchBox_TextChanged" />
            </Border>

            <!-- Sort pills -->
            <StackPanel x:Name="SortPanel" Orientation="Horizontal" Spacing="4" />

            <!-- Note list -->
            <StackPanel x:Name="NotesList" Spacing="10" />

            <!-- Empty state -->
            <StackPanel x:Name="EmptyState" HorizontalAlignment="Center" Spacing="6"
                        Padding="0,48" Visibility="Collapsed">
                <FontIcon Glyph="&#xE70B;" FontSize="42"
                          Foreground="{StaticResource BrandMutedBrush}"
                          HorizontalAlignment="Center" Opacity="0.25" />
                <TextBlock Text="No notes yet"
                           FontSize="16"
                           Foreground="{StaticResource BrandMutedBrush}"
                           HorizontalAlignment="Center" />
                <TextBlock Text="Create a note or star a transcription from History"
                           FontSize="13"
                           Foreground="{StaticResource BrandMutedBrush}"
                           HorizontalAlignment="Center" Opacity="0.6" />
            </StackPanel>

            <Border Height="20" />
        </StackPanel>
    </ScrollViewer>
</Page>
```

- [ ] **Step 2: Replace NotesPage.xaml.cs**

```csharp
// VoxScript/Views/NotesPage.xaml.cs
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Windows.ApplicationModel.DataTransfer;
using VoxScript.ViewModels;

namespace VoxScript.Views;

public sealed partial class NotesPage : Page
{
    public static NotesViewModel SharedViewModel { get; } = new();

    private DispatcherTimer? _searchDebounce;

    public NotesPage()
    {
        this.InitializeComponent();
        SharedViewModel.Notes.CollectionChanged += (_, _) => RebuildList();
        BuildSortPills();
    }

    private async void Page_Loaded(object sender, RoutedEventArgs e)
    {
        await SharedViewModel.LoadAsync();
        RebuildList();
        UpdateVisibility();
    }

    // ── New Note ────────────────────────────────────────────────

    private async void NewButton_Click(object sender, RoutedEventArgs e)
    {
        var item = await SharedViewModel.CreateAsync();
        NoteEditorManager.OpenEditor(item.Id);
    }

    // ── Search ──────────────────────────────────────────────────

    private void SearchBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        SharedViewModel.SearchQuery = SearchBox.Text;
        _searchDebounce?.Stop();
        _searchDebounce = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(300) };
        _searchDebounce.Tick += async (_, _) =>
        {
            _searchDebounce?.Stop();
            await SharedViewModel.SearchAsync();
            RebuildList();
            UpdateVisibility();
        };
        _searchDebounce.Start();
    }

    // ── Sort Pills ──────────────────────────────────────────────

    private void BuildSortPills()
    {
        var sorts = new[] { ("Newest", SortMode.Newest), ("Oldest", SortMode.Oldest), ("A\u2013Z", SortMode.Alphabetical) };
        foreach (var (label, mode) in sorts)
        {
            var pill = new Button
            {
                Content = label,
                FontSize = 12,
                Padding = new Thickness(12, 4, 12, 4),
                CornerRadius = new CornerRadius(12),
                Background = mode == SharedViewModel.CurrentSort
                    ? (SolidColorBrush)Application.Current.Resources["BrandPrimaryLightBrush"]
                    : new SolidColorBrush(Microsoft.UI.Colors.Transparent),
                Foreground = mode == SharedViewModel.CurrentSort
                    ? (SolidColorBrush)Application.Current.Resources["BrandPrimaryBrush"]
                    : (SolidColorBrush)Application.Current.Resources["BrandMutedBrush"],
                BorderThickness = new Thickness(0),
            };
            pill.Click += (_, _) =>
            {
                SharedViewModel.CurrentSort = mode;
                RebuildSortPills();
                RebuildList();
            };
            SortPanel.Children.Add(pill);
        }
    }

    private void RebuildSortPills()
    {
        SortPanel.Children.Clear();
        BuildSortPills();
    }

    // ── Card List ───────────────────────────────────────────────

    private void UpdateVisibility()
    {
        EmptyState.Visibility = SharedViewModel.IsEmpty ? Visibility.Visible : Visibility.Collapsed;
    }

    private void RebuildList()
    {
        NotesList.Children.Clear();
        foreach (var item in SharedViewModel.Notes)
            NotesList.Children.Add(BuildCard(item));
        UpdateVisibility();
    }

    private Border BuildCard(NoteItem item)
    {
        var card = new Border
        {
            Background = (SolidColorBrush)Application.Current.Resources["BrandCardBrush"],
            CornerRadius = new CornerRadius(12),
            Padding = new Thickness(20, 16, 20, 16),
            BorderBrush = (SolidColorBrush)Application.Current.Resources["BrandPrimaryLightBrush"],
            BorderThickness = new Thickness(1),
        };

        var outer = new StackPanel { Spacing = 6 };

        // Header: title + copy button
        var header = new Grid();
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var title = new TextBlock
        {
            Text = item.Title,
            FontSize = 15,
            FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
            Foreground = (SolidColorBrush)Application.Current.Resources["BrandForegroundBrush"],
            TextTrimming = TextTrimming.CharacterEllipsis,
            MaxLines = 1,
        };
        Grid.SetColumn(title, 0);
        header.Children.Add(title);

        var copyIcon = new FontIcon { Glyph = "\uE8C8", FontSize = 14, Foreground = (SolidColorBrush)Application.Current.Resources["BrandMutedBrush"] };
        var copyBtn = new Button
        {
            Background = new SolidColorBrush(Microsoft.UI.Colors.Transparent),
            Padding = new Thickness(8, 4, 8, 4),
            CornerRadius = new CornerRadius(6),
            Content = copyIcon,
            BorderThickness = new Thickness(0),
        };
        ToolTipService.SetToolTip(copyBtn, "Copy to clipboard");
        copyBtn.Click += async (_, _) =>
        {
            var dp = new DataPackage();
            dp.SetText(item.ContentPlainText);
            Clipboard.SetContent(dp);
            copyIcon.Glyph = "\uE73E";
            copyIcon.Foreground = (SolidColorBrush)Application.Current.Resources["BrandSuccessBrush"];
            await Task.Delay(1500);
            copyIcon.Glyph = "\uE8C8";
            copyIcon.Foreground = (SolidColorBrush)Application.Current.Resources["BrandMutedBrush"];
        };
        Grid.SetColumn(copyBtn, 1);
        header.Children.Add(copyBtn);

        outer.Children.Add(header);

        // Preview text
        if (!string.IsNullOrWhiteSpace(item.Preview))
        {
            outer.Children.Add(new TextBlock
            {
                Text = item.Preview,
                FontSize = 13,
                LineHeight = 20,
                MaxLines = 2,
                TextTrimming = TextTrimming.CharacterEllipsis,
                Foreground = (SolidColorBrush)Application.Current.Resources["BrandMutedBrush"],
            });
        }

        // Footer: timestamp + badge
        var footer = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        footer.Children.Add(new TextBlock
        {
            Text = item.TimeDisplay,
            FontSize = 11,
            Foreground = (SolidColorBrush)Application.Current.Resources["BrandMutedBrush"],
            Opacity = 0.7,
        });

        if (item.IsStarred)
        {
            footer.Children.Add(new TextBlock
            {
                Text = "\u2605 Saved",
                FontSize = 10,
                Foreground = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 212, 168, 83)),
            });
        }
        else
        {
            footer.Children.Add(new Border
            {
                Background = (SolidColorBrush)Application.Current.Resources["BrandPrimaryLightBrush"],
                CornerRadius = new CornerRadius(4),
                Padding = new Thickness(6, 1, 6, 1),
                Child = new TextBlock
                {
                    Text = "Note",
                    FontSize = 10,
                    Foreground = (SolidColorBrush)Application.Current.Resources["BrandMutedBrush"],
                },
            });
        }
        outer.Children.Add(footer);

        card.Child = outer;

        // Click card → open editor
        card.PointerPressed += (_, _) => NoteEditorManager.OpenEditor(item.Id);

        return card;
    }
}

/// <summary>
/// Manages the singleton NoteEditorWindow. Call OpenEditor to show/focus it.
/// </summary>
public static class NoteEditorManager
{
    private static Shell.NoteEditorWindow? _window;

    public static void OpenEditor(int? selectNoteId = null)
    {
        if (_window is null)
        {
            _window = new Shell.NoteEditorWindow();
            _window.Closed += (_, _) => _window = null;
        }

        _window.Activate();

        if (selectNoteId.HasValue)
            NotesPage.SharedViewModel.SelectById(selectNoteId.Value);
    }
}
```

- [ ] **Step 3: Verify build compiles**

Run: `dotnet build VoxScript.slnx`
Expected: May fail because `NoteEditorWindow` doesn't exist yet. That's OK — proceed to Task 8.

- [ ] **Step 4: Commit**

```bash
git add VoxScript/Views/NotesPage.xaml VoxScript/Views/NotesPage.xaml.cs
git commit -m "feat: implement NotesPage list view with search, sort, card building"
```

---

### Task 8: NoteEditorWindow — master-detail editor

**Files:**
- Create: `VoxScript/Shell/NoteEditorWindow.xaml`
- Create: `VoxScript/Shell/NoteEditorWindow.xaml.cs`
- Modify: `VoxScript/VoxScript.csproj` (may need to add new XAML file reference — WinUI auto-discovers, but verify)

- [ ] **Step 1: Create NoteEditorWindow.xaml**

```xml
<?xml version="1.0" encoding="utf-8"?>
<Window
    x:Class="VoxScript.Shell.NoteEditorWindow"
    xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
    xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
    Title="Notes — VoxScript">

    <Grid Background="{StaticResource BrandBackgroundBrush}">
        <Grid.ColumnDefinitions>
            <ColumnDefinition Width="300" />
            <ColumnDefinition Width="1" />
            <ColumnDefinition Width="*" />
        </Grid.ColumnDefinitions>

        <!-- Left Panel: Note List Sidebar -->
        <Grid Grid.Column="0">
            <Grid.RowDefinitions>
                <RowDefinition Height="Auto" />
                <RowDefinition Height="Auto" />
                <RowDefinition Height="Auto" />
                <RowDefinition Height="*" />
            </Grid.RowDefinitions>

            <!-- Header + New -->
            <Grid Grid.Row="0" Padding="16,16,16,8">
                <TextBlock Text="Notes"
                           FontFamily="Georgia" FontSize="18"
                           Foreground="{StaticResource BrandForegroundBrush}"
                           VerticalAlignment="Center" />
                <Button Content="＋ New"
                        HorizontalAlignment="Right"
                        Click="SidebarNewButton_Click"
                        Background="{StaticResource BrandPrimaryBrush}"
                        Foreground="White"
                        FontSize="11" FontWeight="Medium"
                        CornerRadius="6" Padding="12,4" />
            </Grid>

            <!-- Search -->
            <Border Grid.Row="1" Margin="16,0,16,8"
                    Background="{StaticResource BrandCardBrush}"
                    CornerRadius="8" Padding="2"
                    BorderBrush="{StaticResource BrandPrimaryLightBrush}"
                    BorderThickness="1">
                <TextBox x:Name="SidebarSearchBox"
                         PlaceholderText="Search..."
                         FontSize="12"
                         BorderThickness="0" Background="Transparent"
                         TextChanged="SidebarSearchBox_TextChanged" />
            </Border>

            <!-- Sort pills -->
            <StackPanel x:Name="SidebarSortPanel" Grid.Row="2"
                        Orientation="Horizontal" Spacing="4"
                        Padding="16,0,16,8" />

            <!-- Note card list -->
            <ScrollViewer Grid.Row="3" VerticalScrollBarVisibility="Auto">
                <StackPanel x:Name="SidebarNotesList" Spacing="4"
                            Padding="12,0,12,12" />
            </ScrollViewer>
        </Grid>

        <!-- Divider -->
        <Border Grid.Column="1" Background="{StaticResource BrandPrimaryLightBrush}" />

        <!-- Right Panel: Editor -->
        <Grid Grid.Column="2" x:Name="EditorPanel">
            <Grid.RowDefinitions>
                <RowDefinition Height="Auto" />
                <RowDefinition Height="Auto" />
                <RowDefinition Height="Auto" />
                <RowDefinition Height="*" />
                <RowDefinition Height="Auto" />
            </Grid.RowDefinitions>

            <!-- Title bar -->
            <Grid Grid.Row="0" Padding="20,16,20,4">
                <TextBox x:Name="TitleBox"
                         FontFamily="Georgia" FontSize="20"
                         Foreground="{StaticResource BrandForegroundBrush}"
                         Background="Transparent"
                         BorderThickness="0"
                         PlaceholderText="Note title..."
                         TextChanged="TitleBox_TextChanged" />
                <Button x:Name="DeleteButton"
                        HorizontalAlignment="Right"
                        Click="DeleteButton_Click"
                        Background="Transparent"
                        BorderThickness="0"
                        Padding="8,4"
                        CornerRadius="6"
                        ToolTipService.ToolTip="Delete note">
                    <FontIcon Glyph="&#xE74D;" FontSize="14"
                              Foreground="#CC4444" />
                </Button>
            </Grid>

            <!-- Metadata -->
            <TextBlock x:Name="MetadataText" Grid.Row="1"
                       Padding="20,0,20,8"
                       FontSize="11"
                       Foreground="{StaticResource BrandMutedBrush}"
                       Opacity="0.7" />

            <!-- Formatting toolbar -->
            <Border Grid.Row="2"
                    BorderBrush="{StaticResource BrandPrimaryLightBrush}"
                    BorderThickness="0,1,0,1"
                    Padding="20,4">
                <StackPanel x:Name="ToolbarPanel" Orientation="Horizontal" Spacing="2" />
            </Border>

            <!-- RichEditBox -->
            <RichEditBox x:Name="Editor" Grid.Row="3"
                         Padding="20,12"
                         Background="Transparent"
                         BorderThickness="0"
                         TextChanged="Editor_TextChanged"
                         SelectionChanged="Editor_SelectionChanged" />

            <!-- Empty state (shown when no note selected) -->
            <StackPanel x:Name="EditorEmptyState" Grid.Row="0" Grid.RowSpan="5"
                        HorizontalAlignment="Center" VerticalAlignment="Center"
                        Visibility="Visible">
                <TextBlock Text="Select a note or create a new one"
                           FontSize="15"
                           Foreground="{StaticResource BrandMutedBrush}" />
            </StackPanel>

            <!-- Save status -->
            <TextBlock x:Name="SaveStatusText" Grid.Row="4"
                       Padding="20,6"
                       FontSize="10"
                       Foreground="{StaticResource BrandMutedBrush}"
                       HorizontalAlignment="Right"
                       Opacity="0.7" />
        </Grid>
    </Grid>
</Window>
```

- [ ] **Step 2: Create NoteEditorWindow.xaml.cs**

```csharp
// VoxScript/Shell/NoteEditorWindow.xaml.cs
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Text;
using Windows.ApplicationModel.DataTransfer;
using VoxScript.ViewModels;
using VoxScript.Helpers;
using WinUIEx;

namespace VoxScript.Shell;

public sealed partial class NoteEditorWindow : Window
{
    private NotesViewModel ViewModel => Views.NotesPage.SharedViewModel;
    private DispatcherTimer? _searchDebounce;
    private DispatcherTimer? _autoSaveTimer;
    private bool _isLoadingNote;

    public NoteEditorWindow()
    {
        InitializeComponent();

        // Window setup
        this.SetWindowSize(1000, 650);
        MinWidth = 800;
        MinHeight = 500;
        SystemBackdrop = new Microsoft.UI.Xaml.Media.MicaBackdrop();
        ExtendsContentIntoTitleBar = true;

        ViewModel.Notes.CollectionChanged += (_, _) =>
            DispatcherQueue.TryEnqueue(RebuildSidebar);
        ViewModel.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(ViewModel.SelectedNote))
                DispatcherQueue.TryEnqueue(LoadSelectedNote);
        };

        BuildToolbar();
        BuildSortPills();

        // Initial load
        DispatcherQueue.TryEnqueue(async () =>
        {
            await ViewModel.LoadAsync();
            RebuildSidebar();
            LoadSelectedNote();
        });
    }

    // ── Sidebar ─────────────────────────────────────────────────

    private async void SidebarNewButton_Click(object sender, RoutedEventArgs e)
    {
        var item = await ViewModel.CreateAsync();
        RebuildSidebar();
        LoadSelectedNote();
        TitleBox.Focus(FocusState.Programmatic);
        TitleBox.SelectAll();
    }

    private void SidebarSearchBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        ViewModel.SearchQuery = SidebarSearchBox.Text;
        _searchDebounce?.Stop();
        _searchDebounce = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(300) };
        _searchDebounce.Tick += async (_, _) =>
        {
            _searchDebounce?.Stop();
            await ViewModel.SearchAsync();
            RebuildSidebar();
        };
        _searchDebounce.Start();
    }

    private void BuildSortPills()
    {
        SidebarSortPanel.Children.Clear();
        var sorts = new[] { ("Newest", SortMode.Newest), ("Oldest", SortMode.Oldest), ("A\u2013Z", SortMode.Alphabetical) };
        foreach (var (label, mode) in sorts)
        {
            var pill = new Button
            {
                Content = label,
                FontSize = 10,
                Padding = new Thickness(8, 2, 8, 2),
                CornerRadius = new CornerRadius(10),
                Background = mode == ViewModel.CurrentSort
                    ? (SolidColorBrush)Application.Current.Resources["BrandPrimaryLightBrush"]
                    : new SolidColorBrush(Microsoft.UI.Colors.Transparent),
                Foreground = mode == ViewModel.CurrentSort
                    ? (SolidColorBrush)Application.Current.Resources["BrandPrimaryBrush"]
                    : (SolidColorBrush)Application.Current.Resources["BrandMutedBrush"],
                BorderThickness = new Thickness(0),
            };
            pill.Click += (_, _) =>
            {
                ViewModel.CurrentSort = mode;
                BuildSortPills();
                RebuildSidebar();
            };
            SidebarSortPanel.Children.Add(pill);
        }
    }

    private void RebuildSidebar()
    {
        SidebarNotesList.Children.Clear();
        foreach (var item in ViewModel.Notes)
            SidebarNotesList.Children.Add(BuildSidebarCard(item));
    }

    private Border BuildSidebarCard(NoteItem item)
    {
        bool isSelected = ViewModel.SelectedNote?.Id == item.Id;

        var card = new Border
        {
            Background = isSelected
                ? (SolidColorBrush)Application.Current.Resources["BrandPrimaryLightBrush"]
                : (SolidColorBrush)Application.Current.Resources["BrandCardBrush"],
            CornerRadius = new CornerRadius(8),
            Padding = new Thickness(10, 8, 10, 8),
            BorderBrush = isSelected
                ? (SolidColorBrush)Application.Current.Resources["BrandPrimaryBrush"]
                : new SolidColorBrush(Microsoft.UI.Colors.Transparent),
            BorderThickness = new Thickness(isSelected ? 0 : 0, 0, 0, 0),
        };

        // Add left accent for selected
        if (isSelected)
        {
            card.BorderThickness = new Thickness(3, 0, 0, 0);
            card.BorderBrush = (SolidColorBrush)Application.Current.Resources["BrandPrimaryBrush"];
        }

        var outer = new StackPanel { Spacing = 3 };

        // Top row: title + copy button
        var topRow = new Grid();
        topRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        topRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var titleText = new TextBlock
        {
            Text = item.Title,
            FontSize = 11,
            FontWeight = FontWeights.SemiBold,
            Foreground = (SolidColorBrush)Application.Current.Resources["BrandForegroundBrush"],
            TextTrimming = TextTrimming.CharacterEllipsis,
            MaxLines = 1,
        };
        Grid.SetColumn(titleText, 0);
        topRow.Children.Add(titleText);

        var copyIcon = new FontIcon { Glyph = "\uE8C8", FontSize = 11, Foreground = (SolidColorBrush)Application.Current.Resources["BrandMutedBrush"] };
        var copyBtn = new Button
        {
            Background = new SolidColorBrush(Microsoft.UI.Colors.Transparent),
            Padding = new Thickness(4, 2, 4, 2),
            CornerRadius = new CornerRadius(4),
            Content = copyIcon,
            BorderThickness = new Thickness(0),
            MinWidth = 0,
            MinHeight = 0,
        };
        ToolTipService.SetToolTip(copyBtn, "Copy to clipboard");
        copyBtn.Click += async (_, _) =>
        {
            var dp = new DataPackage();
            dp.SetText(item.ContentPlainText);
            Clipboard.SetContent(dp);
            copyIcon.Glyph = "\uE73E";
            copyIcon.Foreground = (SolidColorBrush)Application.Current.Resources["BrandSuccessBrush"];
            await Task.Delay(1500);
            copyIcon.Glyph = "\uE8C8";
            copyIcon.Foreground = (SolidColorBrush)Application.Current.Resources["BrandMutedBrush"];
        };
        Grid.SetColumn(copyBtn, 1);
        topRow.Children.Add(copyBtn);

        outer.Children.Add(topRow);

        // Preview
        if (!string.IsNullOrWhiteSpace(item.Preview))
        {
            outer.Children.Add(new TextBlock
            {
                Text = item.Preview,
                FontSize = 9,
                MaxLines = 2,
                TextTrimming = TextTrimming.CharacterEllipsis,
                Foreground = (SolidColorBrush)Application.Current.Resources["BrandMutedBrush"],
                Opacity = 0.8,
            });
        }

        // Footer: time + badge
        var footer = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6 };
        footer.Children.Add(new TextBlock
        {
            Text = item.TimeDisplay,
            FontSize = 9,
            Foreground = (SolidColorBrush)Application.Current.Resources["BrandMutedBrush"],
            Opacity = 0.6,
        });

        if (item.IsStarred)
        {
            footer.Children.Add(new TextBlock
            {
                Text = "\u2605",
                FontSize = 9,
                Foreground = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 212, 168, 83)),
            });
        }
        outer.Children.Add(footer);

        card.Child = outer;

        // Click to select
        card.PointerPressed += (_, _) =>
        {
            ViewModel.SelectedNote = item;
            RebuildSidebar();
            LoadSelectedNote();
        };

        return card;
    }

    // ── Editor ──────────────────────────────────────────────────

    private void LoadSelectedNote()
    {
        var note = ViewModel.SelectedNote;
        bool hasNote = note is not null;

        EditorEmptyState.Visibility = hasNote ? Visibility.Collapsed : Visibility.Visible;
        TitleBox.Visibility = hasNote ? Visibility.Visible : Visibility.Collapsed;
        DeleteButton.Visibility = hasNote ? Visibility.Visible : Visibility.Collapsed;
        MetadataText.Visibility = hasNote ? Visibility.Visible : Visibility.Collapsed;
        Editor.Visibility = hasNote ? Visibility.Visible : Visibility.Collapsed;
        SaveStatusText.Visibility = hasNote ? Visibility.Visible : Visibility.Collapsed;

        if (note is null) return;

        _isLoadingNote = true;
        TitleBox.Text = note.Title;

        if (!string.IsNullOrEmpty(note.ContentRtf))
        {
            Editor.Document.SetText(TextSetOptions.FormatRtf, note.ContentRtf);
        }
        else if (!string.IsNullOrEmpty(note.ContentPlainText))
        {
            // Starred transcriptions start with plain text only
            Editor.Document.SetText(TextSetOptions.None, note.ContentPlainText);
        }
        else
        {
            Editor.Document.SetText(TextSetOptions.None, "");
        }

        var created = note.CreatedAt.ToLocalTime();
        var modified = note.ModifiedAt.ToLocalTime();
        MetadataText.Text = $"Created: {FormatDate(created)}  \u00b7  Modified: {FormatDate(modified)}";

        SaveStatusText.Text = "";
        _isLoadingNote = false;
    }

    private static string FormatDate(DateTime dt)
    {
        var today = DateTime.Now.Date;
        if (dt.Date == today) return $"Today, {dt:h:mm tt}";
        if (dt.Date == today.AddDays(-1)) return $"Yesterday, {dt:h:mm tt}";
        return dt.ToString("MMM d, h:mm tt");
    }

    // ── Auto-save ───────────────────────────────────────────────

    private void TitleBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (!_isLoadingNote) ScheduleAutoSave();
    }

    private void Editor_TextChanged(object sender, RoutedEventArgs e)
    {
        if (!_isLoadingNote) ScheduleAutoSave();
    }

    private void ScheduleAutoSave()
    {
        SaveStatusText.Text = "";
        _autoSaveTimer?.Stop();
        _autoSaveTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        _autoSaveTimer.Tick += async (_, _) =>
        {
            _autoSaveTimer?.Stop();
            await SaveCurrentNote();
        };
        _autoSaveTimer.Start();
    }

    private async Task SaveCurrentNote()
    {
        var note = ViewModel.SelectedNote;
        if (note is null) return;

        Editor.Document.GetText(TextGetOptions.FormatRtf, out var rtf);
        Editor.Document.GetText(TextGetOptions.None, out var plain);

        await ViewModel.SaveNoteAsync(note, TitleBox.Text, rtf, plain.TrimEnd('\r', '\n'));
        SaveStatusText.Text = ViewModel.SaveStatus;

        // Update metadata display
        var modified = note.ModifiedAt.ToLocalTime();
        var created = note.CreatedAt.ToLocalTime();
        MetadataText.Text = $"Created: {FormatDate(created)}  \u00b7  Modified: {FormatDate(modified)}";
    }

    // ── Delete ──────────────────────────────────────────────────

    private async void DeleteButton_Click(object sender, RoutedEventArgs e)
    {
        var note = ViewModel.SelectedNote;
        if (note is null) return;

        if (await DialogHelper.ConfirmDeleteAsync(Content.XamlRoot, "this note"))
        {
            await ViewModel.DeleteAsync(note);
            RebuildSidebar();
            LoadSelectedNote();
        }
    }

    // ── Formatting Toolbar ──────────────────────────────────────

    private Button? _boldBtn, _italicBtn, _underlineBtn, _bulletBtn, _numberBtn, _checkBtn;

    private void BuildToolbar()
    {
        _boldBtn = MakeToolbarButton("\uE8DD", "Bold");
        _boldBtn.Click += (_, _) => ToggleCharFormat(f => f.Bold = f.Bold == FormatEffect.On ? FormatEffect.Off : FormatEffect.On);
        ToolbarPanel.Children.Add(_boldBtn);

        _italicBtn = MakeToolbarButton("\uE8DB", "Italic");
        _italicBtn.Click += (_, _) => ToggleCharFormat(f => f.Italic = f.Italic == FormatEffect.On ? FormatEffect.Off : FormatEffect.On);
        ToolbarPanel.Children.Add(_italicBtn);

        _underlineBtn = MakeToolbarButton("\uE8DC", "Underline");
        _underlineBtn.Click += (_, _) => ToggleCharFormat(f => f.Underline = f.Underline == UnderlineType.Single ? UnderlineType.None : UnderlineType.Single);
        ToolbarPanel.Children.Add(_underlineBtn);

        // Separator
        ToolbarPanel.Children.Add(new Border
        {
            Width = 1,
            Height = 20,
            Background = (SolidColorBrush)Application.Current.Resources["BrandPrimaryLightBrush"],
            Margin = new Thickness(4, 0, 4, 0),
        });

        _bulletBtn = MakeToolbarButton("\uE8FD", "Bullet list");
        _bulletBtn.Click += (_, _) => ToggleListFormat(MarkerType.Bullet);
        ToolbarPanel.Children.Add(_bulletBtn);

        _numberBtn = MakeToolbarButton("\uE9D5", "Numbered list");
        _numberBtn.Click += (_, _) => ToggleListFormat(MarkerType.Arabic);
        ToolbarPanel.Children.Add(_numberBtn);

        _checkBtn = MakeToolbarButton("\uE73A", "Checklist");
        _checkBtn.Click += (_, _) => ToggleChecklist();
        ToolbarPanel.Children.Add(_checkBtn);
    }

    private static Button MakeToolbarButton(string glyph, string tooltip)
    {
        var btn = new Button
        {
            Content = new FontIcon { Glyph = glyph, FontSize = 14 },
            Width = 32,
            Height = 32,
            Padding = new Thickness(0),
            Background = new SolidColorBrush(Microsoft.UI.Colors.Transparent),
            BorderThickness = new Thickness(0),
            CornerRadius = new CornerRadius(5),
        };
        ToolTipService.SetToolTip(btn, tooltip);
        return btn;
    }

    private void ToggleCharFormat(Action<ITextCharacterFormat> toggle)
    {
        var sel = Editor.Document.Selection;
        if (sel is null) return;
        var format = sel.CharacterFormat;
        toggle(format);
        sel.CharacterFormat = format;
        UpdateToolbarState();
    }

    private void ToggleListFormat(MarkerType marker)
    {
        var sel = Editor.Document.Selection;
        if (sel is null) return;
        var format = sel.ParagraphFormat;
        format.ListType = format.ListType == marker ? MarkerType.None : marker;
        sel.ParagraphFormat = format;
        UpdateToolbarState();
    }

    private void ToggleChecklist()
    {
        var sel = Editor.Document.Selection;
        if (sel is null) return;

        // Get the current line text
        sel.GetText(TextGetOptions.None, out var text);

        if (text.StartsWith("\u2611"))
        {
            // Checked → unchecked
            sel.SetText(TextSetOptions.None, "\u2610" + text[1..]);
        }
        else if (text.StartsWith("\u2610"))
        {
            // Unchecked → remove checkbox
            sel.SetText(TextSetOptions.None, text[1..].TrimStart());
        }
        else
        {
            // No checkbox → add unchecked
            sel.SetText(TextSetOptions.None, "\u2610 " + text);
        }
    }

    private void Editor_SelectionChanged(object sender, RoutedEventArgs e)
    {
        UpdateToolbarState();
    }

    private void UpdateToolbarState()
    {
        var sel = Editor.Document.Selection;
        if (sel is null) return;

        var cf = sel.CharacterFormat;
        var pf = sel.ParagraphFormat;

        SetToolbarActive(_boldBtn, cf.Bold == FormatEffect.On);
        SetToolbarActive(_italicBtn, cf.Italic == FormatEffect.On);
        SetToolbarActive(_underlineBtn, cf.Underline == UnderlineType.Single);
        SetToolbarActive(_bulletBtn, pf.ListType == MarkerType.Bullet);
        SetToolbarActive(_numberBtn, pf.ListType == MarkerType.Arabic);
    }

    private void SetToolbarActive(Button? btn, bool active)
    {
        if (btn is null) return;
        btn.Background = active
            ? (SolidColorBrush)Application.Current.Resources["BrandPrimaryLightBrush"]
            : new SolidColorBrush(Microsoft.UI.Colors.Transparent);
        if (btn.Content is FontIcon icon)
        {
            icon.Foreground = active
                ? (SolidColorBrush)Application.Current.Resources["BrandForegroundBrush"]
                : (SolidColorBrush)Application.Current.Resources["BrandMutedBrush"];
        }
    }
}
```

- [ ] **Step 3: Verify build compiles**

Run: `dotnet build VoxScript.slnx`
Expected: Build succeeded. If `WinUIEx` `SetWindowSize` is not available, replace with `AppWindow.Resize(new Windows.Graphics.SizeInt32(1000, 650));`.

- [ ] **Step 4: Commit**

```bash
git add VoxScript/Shell/NoteEditorWindow.xaml VoxScript/Shell/NoteEditorWindow.xaml.cs
git commit -m "feat: implement NoteEditorWindow with master-detail layout and rich text editor"
```

---

### Task 9: History page star button

**Files:**
- Modify: `VoxScript/Views/HistoryPage.xaml.cs`

- [ ] **Step 1: Add star button to BuildCard method**

In `HistoryPage.xaml.cs`, inside the `BuildCard` method, in the `actions` StackPanel (where `copyBtn` and `deleteBtn` are added), insert a star button **before** the copy button:

```csharp
// Star button — save to Notes
var starIcon = new FontIcon { Glyph = "\uE734", FontSize = 14, Foreground = (SolidColorBrush)Application.Current.Resources["BrandMutedBrush"] };
var starBtn = new Button
{
    Background = new SolidColorBrush(Microsoft.UI.Colors.Transparent),
    Padding = new Thickness(8, 4, 8, 4),
    CornerRadius = new CornerRadius(6),
    Content = starIcon,
};
ToolTipService.SetToolTip(starBtn, "Save to Notes");
starBtn.Click += async (_, _) =>
{
    await NotesPage.SharedViewModel.CreateFromTranscriptionAsync(item.Id, item.DisplayText);
    starIcon.Glyph = "\uE735"; // Filled star
    starIcon.Foreground = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 212, 168, 83));
    ToolTipService.SetToolTip(starBtn, "Saved to Notes!");
};
actions.Children.Add(starBtn);
```

This goes before the existing `actions.Children.Add(copyBtn);` line. The full `actions` StackPanel block becomes:

```csharp
var actions = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 4 };

// Star button
var starIcon = new FontIcon { Glyph = "\uE734", FontSize = 14, Foreground = (SolidColorBrush)Application.Current.Resources["BrandMutedBrush"] };
var starBtn = new Button
{
    Background = new SolidColorBrush(Microsoft.UI.Colors.Transparent),
    Padding = new Thickness(8, 4, 8, 4),
    CornerRadius = new CornerRadius(6),
    Content = starIcon,
};
ToolTipService.SetToolTip(starBtn, "Save to Notes");
starBtn.Click += async (_, _) =>
{
    await NotesPage.SharedViewModel.CreateFromTranscriptionAsync(item.Id, item.DisplayText);
    starIcon.Glyph = "\uE735"; // Filled star
    starIcon.Foreground = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 212, 168, 83));
    ToolTipService.SetToolTip(starBtn, "Saved to Notes!");
};
actions.Children.Add(starBtn);

// Copy button (existing code stays the same)
var copyIcon = new FontIcon { ... };
// ... rest of existing code
```

Also add this using at the top if not present:

```csharp
using VoxScript.ViewModels;
```

Note: `NotesPage.SharedViewModel` is accessible because `SharedViewModel` is a `public static` property on `NotesPage`. No need to resolve from DI.

- [ ] **Step 2: Verify build compiles**

Run: `dotnet build VoxScript.slnx`
Expected: Build succeeded.

- [ ] **Step 3: Commit**

```bash
git add VoxScript/Views/HistoryPage.xaml.cs
git commit -m "feat: add star button to History cards for saving to Notes"
```

---

### Task 10: Smoke test and schema verification

- [ ] **Step 1: Run all tests**

Run: `dotnet test VoxScript.Tests -v n`
Expected: All tests pass (existing 57+ plus 5 new NoteRepository tests).

- [ ] **Step 2: Verify build**

Run: `dotnet build VoxScript.slnx`
Expected: Build succeeded with no errors.

- [ ] **Step 3: Delete local database to force schema recreation**

The app uses `EnsureCreated` + raw SQL fallbacks, but the safest approach for development is to delete the existing DB:

```powershell
Remove-Item "$env:LOCALAPPDATA\VoxScript\voxscript.db" -ErrorAction SilentlyContinue
```

On next app launch, the database will be recreated with the `Notes` table.

- [ ] **Step 4: Commit any final fixes**

If any compilation or test issues were found and fixed, commit them:

```bash
git add -A
git commit -m "fix: resolve Notes page compilation and test issues"
```

---

## Summary

| Task | Files | What |
|------|-------|------|
| 1 | `VoxScript.Core/Notes/NoteRecord.cs` | EF entity |
| 2 | `VoxScript.Core/Notes/INoteRepository.cs` | Repository interface |
| 3 | `AppDbContext.cs`, `App.xaml.cs` | DbSet + raw SQL fallback |
| 4 | `NoteRepository.cs`, `NoteRepositoryTests.cs` | Implementation + 5 tests |
| 5 | `AppBootstrapper.cs` | DI registration |
| 6 | `NotesViewModel.cs` | Shared ViewModel (list, search, sort, CRUD, auto-save) |
| 7 | `NotesPage.xaml`, `NotesPage.xaml.cs` | Main window list + NoteEditorManager |
| 8 | `NoteEditorWindow.xaml`, `NoteEditorWindow.xaml.cs` | Master-detail editor window |
| 9 | `HistoryPage.xaml.cs` | Star button on History cards |
| 10 | — | Smoke test + schema rebuild |
