# Import/Export Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Export and import vocabulary, corrections, and expansions as a single JSON file from the Settings page.

**Architecture:** A `DataPortService` in VoxScript.Core handles serialization/deserialization and duplicate detection via the three existing repositories. The Settings page gets Export/Import buttons in the Extras card, with file picker logic in code-behind passing streams to the ViewModel.

**Tech Stack:** System.Text.Json, WinUI 3 FileSavePicker/FileOpenPicker, xUnit + FluentAssertions + NSubstitute

---

## File Structure

| Action | File | Responsibility |
|--------|------|----------------|
| Create | `VoxScript.Core/DataPort/IDataPortService.cs` | Interface: `ExportAsync`, `ImportAsync` |
| Create | `VoxScript.Core/DataPort/DataPortService.cs` | Implementation: JSON serialization, duplicate detection |
| Create | `VoxScript.Core/DataPort/DataPortModels.cs` | DTOs: `DataPortPayload`, `ImportResult`, `ExportResult` |
| Create | `VoxScript.Tests/DataPort/DataPortServiceTests.cs` | Unit tests for export/import logic |
| Modify | `VoxScript/ViewModels/SettingsViewModel.cs` | Add `ExportDataAsync`, `ImportDataAsync` methods |
| Modify | `VoxScript/Views/SettingsPage.xaml` | Add Export/Import row in Extras card |
| Modify | `VoxScript/Views/SettingsPage.xaml.cs` | File picker handlers, InfoBar display |
| Modify | `VoxScript/Infrastructure/AppBootstrapper.cs` | Register `IDataPortService` |

---

### Task 1: Data Port Models

**Files:**
- Create: `VoxScript.Core/DataPort/DataPortModels.cs`

- [ ] **Step 1: Create the DTO classes**

```csharp
using System.Text.Json.Serialization;

namespace VoxScript.Core.DataPort;

public sealed class DataPortPayload
{
    [JsonPropertyName("version")]
    public int Version { get; set; } = 1;

    [JsonPropertyName("exportedAt")]
    public DateTimeOffset ExportedAt { get; set; }

    [JsonPropertyName("vocabulary")]
    public List<string> Vocabulary { get; set; } = [];

    [JsonPropertyName("corrections")]
    public List<CorrectionDto> Corrections { get; set; } = [];

    [JsonPropertyName("expansions")]
    public List<ExpansionDto> Expansions { get; set; } = [];
}

public sealed class CorrectionDto
{
    [JsonPropertyName("wrong")]
    public string Wrong { get; set; } = string.Empty;

    [JsonPropertyName("correct")]
    public string Correct { get; set; } = string.Empty;
}

public sealed class ExpansionDto
{
    [JsonPropertyName("original")]
    public string Original { get; set; } = string.Empty;

    [JsonPropertyName("replacement")]
    public string Replacement { get; set; } = string.Empty;

    [JsonPropertyName("caseSensitive")]
    public bool CaseSensitive { get; set; }
}

public sealed class ExportResult
{
    public int VocabularyCount { get; init; }
    public int CorrectionsCount { get; init; }
    public int ExpansionsCount { get; init; }
}

public sealed class ImportResult
{
    public int VocabularyAdded { get; init; }
    public int CorrectionsAdded { get; init; }
    public int ExpansionsAdded { get; init; }
    public int Skipped { get; init; }
}
```

- [ ] **Step 2: Verify it compiles**

Run: `dotnet build VoxScript.Core`
Expected: Build succeeded

- [ ] **Step 3: Commit**

```bash
git add VoxScript.Core/DataPort/DataPortModels.cs
git commit -m "feat: add data port DTOs for import/export"
```

---

### Task 2: Data Port Service Interface and Implementation

**Files:**
- Create: `VoxScript.Core/DataPort/IDataPortService.cs`
- Create: `VoxScript.Core/DataPort/DataPortService.cs`

- [ ] **Step 1: Create the interface**

```csharp
namespace VoxScript.Core.DataPort;

public interface IDataPortService
{
    Task<ExportResult> ExportAsync(Stream output, CancellationToken ct);
    Task<ImportResult> ImportAsync(Stream input, CancellationToken ct);
}
```

- [ ] **Step 2: Create the implementation**

```csharp
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

        // Load existing data for duplicate detection
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
            if (string.IsNullOrWhiteSpace(c.Wrong) || !correctionSet.Add(c.Wrong))
            {
                skipped++;
                continue;
            }
            await _corrections.AddAsync(new CorrectionRecord { Wrong = c.Wrong, Correct = c.Correct }, ct);
            correctionsAdded++;
        }

        foreach (var e in payload.Expansions)
        {
            if (string.IsNullOrWhiteSpace(e.Original) || !expansionSet.Add(e.Original))
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
```

- [ ] **Step 3: Verify it compiles**

Run: `dotnet build VoxScript.Core`
Expected: Build succeeded

- [ ] **Step 4: Commit**

```bash
git add VoxScript.Core/DataPort/
git commit -m "feat: add DataPortService for JSON import/export"
```

---

### Task 3: Unit Tests for DataPortService

**Files:**
- Create: `VoxScript.Tests/DataPort/DataPortServiceTests.cs`

- [ ] **Step 1: Write the test class**

```csharp
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
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test VoxScript.Tests --filter "FullyQualifiedName~DataPortServiceTests" -v n`
Expected: All 8 tests FAIL (DataPortService not yet wired, but since we created it in Task 2, they should pass if Tasks 1-2 are done)

- [ ] **Step 3: Run tests to verify they pass**

Run: `dotnet test VoxScript.Tests --filter "FullyQualifiedName~DataPortServiceTests" -v n`
Expected: 8 passed

- [ ] **Step 4: Commit**

```bash
git add VoxScript.Tests/DataPort/DataPortServiceTests.cs
git commit -m "test: add unit tests for DataPortService import/export"
```

---

### Task 4: Register DataPortService in DI

**Files:**
- Modify: `VoxScript/Infrastructure/AppBootstrapper.cs:71-74`

- [ ] **Step 1: Add the DI registration**

Add after the `WordReplacementService` registration (around line 74 in `AppBootstrapper.cs`):

```csharp
services.AddSingleton<IDataPortService, DataPortService>();
```

Add the using at the top of the file:

```csharp
using VoxScript.Core.DataPort;
```

- [ ] **Step 2: Verify it compiles**

Run: `dotnet build VoxScript.slnx`
Expected: Build succeeded

- [ ] **Step 3: Commit**

```bash
git add VoxScript/Infrastructure/AppBootstrapper.cs
git commit -m "feat: register DataPortService in DI container"
```

---

### Task 5: SettingsViewModel Export/Import Methods

**Files:**
- Modify: `VoxScript/ViewModels/SettingsViewModel.cs`

- [ ] **Step 1: Add import/export methods and result properties**

Add at the top of `SettingsViewModel.cs`, in the using block:

```csharp
using VoxScript.Core.DataPort;
```

Add after the `AutoAddToDictionary` property block (around line 216), before the Keybinds section:

```csharp
// ── Import / Export ────────────────────────────────────────

[ObservableProperty]
public partial string DataPortStatusMessage { get; set; } = "";

[ObservableProperty]
public partial bool DataPortIsError { get; set; }

public async Task<ExportResult> ExportDataAsync(Stream output, CancellationToken ct)
{
    var service = ServiceLocator.Get<IDataPortService>();
    var result = await service.ExportAsync(output, ct);
    DataPortIsError = false;
    DataPortStatusMessage = $"Exported {result.VocabularyCount} vocabulary words, {result.CorrectionsCount} corrections, {result.ExpansionsCount} expansions.";
    return result;
}

public async Task ImportDataAsync(Stream input, CancellationToken ct)
{
    try
    {
        var service = ServiceLocator.Get<IDataPortService>();
        var result = await service.ImportAsync(input, ct);
        DataPortIsError = false;
        DataPortStatusMessage = $"Imported {result.VocabularyAdded} vocabulary words, {result.CorrectionsAdded} corrections, {result.ExpansionsAdded} expansions ({result.Skipped} skipped).";
    }
    catch (InvalidOperationException ex)
    {
        DataPortIsError = true;
        DataPortStatusMessage = ex.Message;
    }
}
```

- [ ] **Step 2: Verify it compiles**

Run: `dotnet build VoxScript.slnx`
Expected: Build succeeded

- [ ] **Step 3: Commit**

```bash
git add VoxScript/ViewModels/SettingsViewModel.cs
git commit -m "feat: add export/import methods to SettingsViewModel"
```

---

### Task 6: Settings Page UI — Export/Import Row + InfoBar

**Files:**
- Modify: `VoxScript/Views/SettingsPage.xaml`
- Modify: `VoxScript/Views/SettingsPage.xaml.cs`

- [ ] **Step 1: Add the Export/Import row to the Extras card in XAML**

In `SettingsPage.xaml`, inside the Extras card `<StackPanel Spacing="18">`, add a new row after the "Smart formatting" row (before the closing `</StackPanel>` of the Extras card):

```xml
                    <!-- Import / Export -->
                    <Grid ColumnSpacing="32">
                        <Grid.ColumnDefinitions>
                            <ColumnDefinition Width="*" />
                            <ColumnDefinition Width="Auto" />
                        </Grid.ColumnDefinitions>
                        <StackPanel VerticalAlignment="Center">
                            <TextBlock Text="Import / Export"
                                       FontSize="15" Foreground="{StaticResource BrandForegroundBrush}" />
                            <TextBlock Text="Vocabulary, corrections, and expansions"
                                       FontSize="13" Foreground="{StaticResource BrandMutedBrush}" />
                        </StackPanel>
                        <StackPanel Grid.Column="1" Orientation="Horizontal" Spacing="8">
                            <Button Content="Export"
                                    Click="ExportButton_Click"
                                    Background="{StaticResource BrandBackgroundBrush}"
                                    Foreground="{StaticResource BrandForegroundBrush}"
                                    FontSize="13" CornerRadius="8" Padding="12,8"
                                    BorderBrush="{StaticResource BrandPrimaryLightBrush}"
                                    BorderThickness="1" />
                            <Button Content="Import"
                                    Click="ImportButton_Click"
                                    Background="{StaticResource BrandBackgroundBrush}"
                                    Foreground="{StaticResource BrandForegroundBrush}"
                                    FontSize="13" CornerRadius="8" Padding="12,8"
                                    BorderBrush="{StaticResource BrandPrimaryLightBrush}"
                                    BorderThickness="1" />
                        </StackPanel>
                    </Grid>
```

Also add an InfoBar above the Extras card (just before `<!-- Card 5: Extras -->`):

```xml
            <!-- Data port status -->
            <InfoBar x:Name="DataPortInfoBar"
                     IsOpen="False"
                     Severity="Success"
                     IsClosable="True" />
```

- [ ] **Step 2: Add the click handlers in code-behind**

Add these usings at the top of `SettingsPage.xaml.cs`:

```csharp
using Windows.Storage.Pickers;
using WinRT.Interop;
```

Add these methods to `SettingsPage.xaml.cs`:

```csharp
private async void ExportButton_Click(object sender, RoutedEventArgs e)
{
    var picker = new FileSavePicker();
    picker.SuggestedStartLocation = PickerLocationId.DocumentsLibrary;
    picker.FileTypeChoices.Add("JSON", [".json"]);
    picker.SuggestedFileName = "voxscript-data";

    var hwnd = WindowNative.GetWindowHandle(App.MainWindow);
    InitializeWithWindow.Initialize(picker, hwnd);

    var file = await picker.PickSaveFileAsync();
    if (file is null) return;

    using var stream = await file.OpenStreamForWriteAsync();
    stream.SetLength(0);
    await ViewModel.ExportDataAsync(stream, default);

    DataPortInfoBar.Message = ViewModel.DataPortStatusMessage;
    DataPortInfoBar.Severity = InfoBarSeverity.Success;
    DataPortInfoBar.IsOpen = true;
}

private async void ImportButton_Click(object sender, RoutedEventArgs e)
{
    var picker = new FileOpenPicker();
    picker.FileTypeFilter.Add(".json");

    var hwnd = WindowNative.GetWindowHandle(App.MainWindow);
    InitializeWithWindow.Initialize(picker, hwnd);

    var file = await picker.PickSingleFileAsync();
    if (file is null) return;

    using var stream = await file.OpenStreamForReadAsync();
    await ViewModel.ImportDataAsync(stream, default);

    DataPortInfoBar.Message = ViewModel.DataPortStatusMessage;
    DataPortInfoBar.Severity = ViewModel.DataPortIsError
        ? InfoBarSeverity.Error
        : InfoBarSeverity.Success;
    DataPortInfoBar.IsOpen = true;
}
```

- [ ] **Step 3: Verify it compiles**

Run: `dotnet build VoxScript.slnx`
Expected: Build succeeded

- [ ] **Step 4: Run all tests to confirm nothing broke**

Run: `dotnet test VoxScript.Tests -v n`
Expected: All tests pass (86+ existing + 8 new)

- [ ] **Step 5: Commit**

```bash
git add VoxScript/Views/SettingsPage.xaml VoxScript/Views/SettingsPage.xaml.cs
git commit -m "feat: add export/import buttons to Settings page"
```

---

### Task 7: Update STATUS.md

**Files:**
- Modify: `STATUS.md`

- [ ] **Step 1: Mark import/export as done in STATUS.md**

In the "Low Priority — Polish" section, update item 22 from:

```
22. **Import/export** — dictionary, expansions, corrections, and history
```

to:

```
22. **Import/export** — DONE
    - Single JSON file with vocabulary, corrections, and expansions
    - Export/Import buttons in Settings > Extras card
    - Import skips duplicates (case-insensitive matching), adds new items only
    - File format versioned (`version: 1`) for future compatibility
    - Success/error InfoBar with item counts
    - Files: `IDataPortService.cs`, `DataPortService.cs`, `DataPortModels.cs`, `SettingsViewModel.cs`, `SettingsPage.xaml/.cs`
```

- [ ] **Step 2: Commit**

```bash
git add STATUS.md
git commit -m "docs: mark import/export as done in STATUS.md"
```
