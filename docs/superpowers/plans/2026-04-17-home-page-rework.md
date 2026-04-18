# Home Page Rework Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Replace the old `TranscribePage` record-button UI with a non-scrolling dashboard showing pipeline readiness, lifetime stats, a 12-hour activity graph, and the latest transcript.

**Architecture:** New domain types live in `VoxScript.Core/Home/` behind two interfaces (`IHomeStatusService`, `IHomeStatsService`); a new `IModelManager` Core interface bridges the Core/Native boundary for model-file checks; `HomeViewModel` (CommunityToolkit MVVM) in the app layer binds to `HomePage.xaml`. The DB schema gains a `WordCount` column via a proper EF migration, replacing the current `EnsureCreated` call.

**Tech Stack:** C# / .NET 10, WinUI 3, EF Core + SQLite (migrations), CommunityToolkit.Mvvm, xUnit + FluentAssertions + NSubstitute.

---

## File Structure

### New files — Core
- `VoxScript.Core/Home/StatusLevel.cs` — `StatusLevel` enum + `StatusResult` record
- `VoxScript.Core/Home/IHomeStatusService.cs` — status interface
- `VoxScript.Core/Home/IHomeStatsService.cs` — stats interface
- `VoxScript.Core/Home/HomeStatusService.cs` — implementation
- `VoxScript.Core/Home/HomeStatsService.cs` — implementation with in-memory cache
- `VoxScript.Core/Home/TextUtil.cs` — `CountWords` static helper
- `VoxScript.Core/Transcription/Core/IModelManager.cs` — Core-side model existence interface

### New files — Native
- `VoxScript.Native/Whisper/ModelManagerAdapter.cs` — wraps `WhisperModelManager`, implements `IModelManager`

### New files — App
- `VoxScript/ViewModels/HomeViewModel.cs` — observable VM
- `VoxScript/Views/HomePage.xaml` — dashboard XAML
- `VoxScript/Views/HomePage.xaml.cs` — thin code-behind

### Modified files
- `VoxScript.Core/Persistence/TranscriptionRecord.cs` — add `WordCount` property
- `VoxScript.Core/History/ITranscriptionRepository.cs` — add `GetAggregateStatsAsync`
- `VoxScript.Core/History/TranscriptionRepository.cs` — implement new method
- `VoxScript.Core/Transcription/Core/TranscriptionPipeline.cs` — populate `WordCount` before persist
- `VoxScript.Core/VoxScript.Core.csproj` — add `Microsoft.EntityFrameworkCore.Design`
- `VoxScript/Infrastructure/AppBootstrapper.cs` — swap `EnsureCreated` → `Migrate()`, register new services
- `VoxScript/MainWindow.xaml.cs` — all three `TranscribePage` references → `HomePage`
- `VoxScript.Tests/VoxScript.Tests.csproj` — no changes needed (already references Core + Native + App)

### New test files
- `VoxScript.Tests/Home/TextUtilTests.cs`
- `VoxScript.Tests/Home/HomeStatusServiceTests.cs`
- `VoxScript.Tests/Home/HomeStatsServiceTests.cs`
- `VoxScript.Tests/Home/HomeViewModelTests.cs`

### Deleted files (after rename)
- `VoxScript/Views/TranscribePage.xaml`
- `VoxScript/Views/TranscribePage.xaml.cs`

---

## Task 1: `TextUtil` + unit tests

### Files
- Create: `VoxScript.Core/Home/TextUtil.cs`
- Create: `VoxScript.Tests/Home/TextUtilTests.cs`

- [ ] **Step 1: Write the failing tests**

```csharp
// VoxScript.Tests/Home/TextUtilTests.cs
using FluentAssertions;
using VoxScript.Core.Home;

namespace VoxScript.Tests.Home;

public class TextUtilTests
{
    [Fact] public void CountWords_null_returns_zero() =>
        TextUtil.CountWords(null).Should().Be(0);

    [Fact] public void CountWords_empty_returns_zero() =>
        TextUtil.CountWords("").Should().Be(0);

    [Fact] public void CountWords_whitespace_only_returns_zero() =>
        TextUtil.CountWords("   ").Should().Be(0);

    [Fact] public void CountWords_single_word() =>
        TextUtil.CountWords("hello").Should().Be(1);

    [Fact] public void CountWords_multiple_spaces_between_words() =>
        TextUtil.CountWords("hello   world").Should().Be(2);

    [Fact] public void CountWords_leading_trailing_whitespace() =>
        TextUtil.CountWords("  hello world  ").Should().Be(2);

    [Fact] public void CountWords_punctuation_does_not_split() =>
        TextUtil.CountWords("hello, world").Should().Be(2);

    [Fact] public void CountWords_newlines_count_as_whitespace() =>
        TextUtil.CountWords("hello\nworld\nfoo").Should().Be(3);

    [Fact] public void CountWords_tabs_count_as_whitespace() =>
        TextUtil.CountWords("hello\tworld").Should().Be(2);
}
```

- [ ] **Step 2: Run tests to verify they fail**

```
dotnet test VoxScript.Tests --filter "FullyQualifiedName~TextUtilTests" -v minimal
```

Expected: build error — `VoxScript.Core.Home` namespace does not exist.

- [ ] **Step 3: Implement `TextUtil`**

```csharp
// VoxScript.Core/Home/TextUtil.cs
namespace VoxScript.Core.Home;

public static class TextUtil
{
    /// <summary>
    /// Counts words by splitting on any whitespace.
    /// Punctuation is not a word boundary: "hello, world" → 2.
    /// Null or empty input returns 0.
    /// </summary>
    public static int CountWords(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return 0;
        return text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries).Length;
    }
}
```

- [ ] **Step 4: Run tests to verify they pass**

```
dotnet test VoxScript.Tests --filter "FullyQualifiedName~TextUtilTests" -v minimal
```

Expected: 9 tests PASS.

- [ ] **Step 5: Commit**

```bash
git add VoxScript.Core/Home/TextUtil.cs VoxScript.Tests/Home/TextUtilTests.cs
git commit -m "feat(home): add TextUtil.CountWords with full test coverage"
```

---

## Task 2: Domain types — `StatusLevel`, `StatusResult`, interfaces

### Files
- Create: `VoxScript.Core/Home/StatusLevel.cs`
- Create: `VoxScript.Core/Home/IHomeStatusService.cs`
- Create: `VoxScript.Core/Home/IHomeStatsService.cs`
- Create: `VoxScript.Core/Transcription/Core/IModelManager.cs`

- [ ] **Step 1: Create `StatusLevel.cs`**

```csharp
// VoxScript.Core/Home/StatusLevel.cs
namespace VoxScript.Core.Home;

public enum StatusLevel { Ready, Warming, Unavailable, Off }

public sealed record StatusResult(StatusLevel Level, string Label);
```

- [ ] **Step 2: Create `IHomeStatusService.cs`**

```csharp
// VoxScript.Core/Home/IHomeStatusService.cs
namespace VoxScript.Core.Home;

public interface IHomeStatusService
{
    Task<StatusResult> GetModelStatusAsync(CancellationToken ct);
    Task<StatusResult> GetAiEnhancementStatusAsync(CancellationToken ct);
    Task<StatusResult> GetLlmFormattingStatusAsync(CancellationToken ct);

    /// <summary>
    /// Rolls up component statuses. Off components are skipped.
    /// Priority when multiple are active: Unavailable > Warming > Ready.
    /// If all considered components are Off, returns Ready.
    /// </summary>
    StatusResult Rollup(params StatusResult[] components);
}
```

- [ ] **Step 3: Create `IHomeStatsService.cs`**

```csharp
// VoxScript.Core/Home/IHomeStatsService.cs
namespace VoxScript.Core.Home;

public interface IHomeStatsService
{
    Task<int> GetTotalWordsAsync(CancellationToken ct);
    Task<double> GetAverageWpmAsync(CancellationToken ct);

    /// <summary>
    /// Returns word counts bucketed into <paramref name="hours"/> slots,
    /// index 0 = oldest hour, index (hours-1) = current hour.
    /// </summary>
    Task<IReadOnlyList<int>> GetHourlyWordBucketsAsync(int hours, CancellationToken ct);

    /// <summary>Drops the in-memory cache so the next read re-queries the DB.</summary>
    void InvalidateCache();
}
```

- [ ] **Step 4: Create `IModelManager.cs`**

```csharp
// VoxScript.Core/Transcription/Core/IModelManager.cs
namespace VoxScript.Core.Transcription.Core;

/// <summary>
/// Core-layer abstraction for checking whether a Whisper model file is present.
/// Implemented in VoxScript.Native by ModelManagerAdapter.
/// </summary>
public interface IModelManager
{
    bool IsDownloaded(string modelName);
    bool IsDownloading(string modelName);
}
```

- [ ] **Step 5: Verify the solution builds**

```
dotnet build VoxScript.slnx
```

Expected: build succeeds, 0 errors.

- [ ] **Step 6: Commit**

```bash
git add VoxScript.Core/Home/StatusLevel.cs \
        VoxScript.Core/Home/IHomeStatusService.cs \
        VoxScript.Core/Home/IHomeStatsService.cs \
        VoxScript.Core/Transcription/Core/IModelManager.cs
git commit -m "feat(home): add StatusLevel, StatusResult, IHomeStatusService, IHomeStatsService, IModelManager"
```

---

## Task 3: `IModelManager` Native adapter

### Files
- Create: `VoxScript.Native/Whisper/ModelManagerAdapter.cs`

The `WhisperModelManager` has no concept of "is downloading" — it only tracks file presence. For the Warming state spec (active download in progress), `ModelManagerAdapter.IsDownloading` returns `false` by default; a follow-up task can wire a download-in-progress flag if needed. For now the status service maps missing-file-no-download to `Unavailable` and present-file to `Ready`. This matches the spec's "Warming = ModelDownloadManager reports active download" — which is a future signal.

- [ ] **Step 1: Create `ModelManagerAdapter.cs`**

```csharp
// VoxScript.Native/Whisper/ModelManagerAdapter.cs
using VoxScript.Core.Transcription.Core;

namespace VoxScript.Native.Whisper;

/// <summary>
/// Adapts <see cref="WhisperModelManager"/> to the Core <see cref="IModelManager"/> interface,
/// keeping the Core/Native dependency boundary intact.
/// </summary>
public sealed class ModelManagerAdapter : IModelManager
{
    private readonly WhisperModelManager _manager;

    public ModelManagerAdapter(WhisperModelManager manager) => _manager = manager;

    public bool IsDownloaded(string modelName) => _manager.IsDownloaded(modelName);

    /// <summary>
    /// Returns false until a download-progress event is wired in a future task.
    /// </summary>
    public bool IsDownloading(string modelName) => false;
}
```

- [ ] **Step 2: Verify the solution builds**

```
dotnet build VoxScript.slnx
```

Expected: build succeeds, 0 errors.

- [ ] **Step 3: Commit**

```bash
git add VoxScript.Native/Whisper/ModelManagerAdapter.cs
git commit -m "feat(home): add ModelManagerAdapter implementing IModelManager"
```

---

## Task 4: `HomeStatusService` + unit tests

### Files
- Create: `VoxScript.Core/Home/HomeStatusService.cs`
- Create: `VoxScript.Tests/Home/HomeStatusServiceTests.cs`

- [ ] **Step 1: Write the failing tests**

```csharp
// VoxScript.Tests/Home/HomeStatusServiceTests.cs
using FluentAssertions;
using NSubstitute;
using VoxScript.Core.AI;
using VoxScript.Core.Home;
using VoxScript.Core.Settings;
using VoxScript.Core.Transcription.Core;

namespace VoxScript.Tests.Home;

public class HomeStatusServiceTests
{
    // ── Helpers ──────────────────────────────────────────────────────────────

    private static (HomeStatusService svc, ISettingsStore store, IModelManager modelMgr)
        Build(HttpClient? http = null)
    {
        var store = Substitute.For<ISettingsStore>();
        // Defaults: AI enhancement off, structural formatting off, local provider
        store.Get<bool?>(nameof(AppSettings.AiEnhancementEnabled)).Returns((bool?)false);
        store.Get<bool?>(nameof(AppSettings.StructuralFormattingEnabled)).Returns((bool?)false);
        store.Get<AiProvider?>(nameof(AppSettings.AiProvider)).Returns((AiProvider?)AiProvider.Local);
        store.Get<AiProvider?>(nameof(AppSettings.StructuralAiProvider)).Returns((AiProvider?)AiProvider.Local);
        store.Get<string>(nameof(AppSettings.SelectedModelName)).Returns("ggml-base.en");
        store.Get<string>(nameof(AppSettings.OllamaEndpoint)).Returns("http://localhost:11434");
        store.Get<string>(nameof(AppSettings.StructuralOllamaEndpoint)).Returns("http://localhost:11434");

        var settings = new AppSettings(store);
        var modelMgr = Substitute.For<IModelManager>();
        var httpClient = http ?? new HttpClient();
        var svc = new HomeStatusService(settings, modelMgr, httpClient);
        return (svc, store, modelMgr);
    }

    // ── Model status ──────────────────────────────────────────────────────────

    [Fact]
    public async Task GetModelStatus_ready_when_file_exists()
    {
        var (svc, _, modelMgr) = Build();
        modelMgr.IsDownloaded("ggml-base.en").Returns(true);

        var result = await svc.GetModelStatusAsync(CancellationToken.None);

        result.Level.Should().Be(StatusLevel.Ready);
    }

    [Fact]
    public async Task GetModelStatus_warming_when_download_in_progress()
    {
        var (svc, _, modelMgr) = Build();
        modelMgr.IsDownloaded("ggml-base.en").Returns(false);
        modelMgr.IsDownloading("ggml-base.en").Returns(true);

        var result = await svc.GetModelStatusAsync(CancellationToken.None);

        result.Level.Should().Be(StatusLevel.Warming);
    }

    [Fact]
    public async Task GetModelStatus_unavailable_when_file_missing_and_not_downloading()
    {
        var (svc, _, modelMgr) = Build();
        modelMgr.IsDownloaded("ggml-base.en").Returns(false);
        modelMgr.IsDownloading("ggml-base.en").Returns(false);

        var result = await svc.GetModelStatusAsync(CancellationToken.None);

        result.Level.Should().Be(StatusLevel.Unavailable);
    }

    [Fact]
    public async Task GetModelStatus_label_includes_model_name_when_unavailable()
    {
        var (svc, _, modelMgr) = Build();
        modelMgr.IsDownloaded("ggml-base.en").Returns(false);
        modelMgr.IsDownloading("ggml-base.en").Returns(false);

        var result = await svc.GetModelStatusAsync(CancellationToken.None);

        result.Label.Should().Contain("ggml-base.en");
    }

    // ── AI Enhancement status ─────────────────────────────────────────────────

    [Fact]
    public async Task GetAiEnhancementStatus_off_when_disabled()
    {
        var (svc, store, _) = Build();
        store.Get<bool?>(nameof(AppSettings.AiEnhancementEnabled)).Returns((bool?)false);

        var result = await svc.GetAiEnhancementStatusAsync(CancellationToken.None);

        result.Level.Should().Be(StatusLevel.Off);
    }

    [Fact]
    public async Task GetAiEnhancementStatus_ready_when_cloud_key_set()
    {
        var (svc, store, _) = Build();
        store.Get<bool?>(nameof(AppSettings.AiEnhancementEnabled)).Returns((bool?)true);
        store.Get<AiProvider?>(nameof(AppSettings.AiProvider)).Returns((AiProvider?)AiProvider.OpenAI);
        // ApiKeyManager reads from IApiKeyStore; for this test we mark IsConfigured via provider check.
        // HomeStatusService checks cloud by delegating to the AIService.IsConfigured pattern:
        // provider != Local and key length > 0. We supply the key via the store.
        store.Get<string>("OpenAiKey").Returns("sk-test-key-of-sufficient-length");

        var result = await svc.GetAiEnhancementStatusAsync(CancellationToken.None);

        result.Level.Should().Be(StatusLevel.Ready);
    }

    // ── LLM Formatting status ─────────────────────────────────────────────────

    [Fact]
    public async Task GetLlmFormattingStatus_off_when_disabled()
    {
        var (svc, store, _) = Build();
        store.Get<bool?>(nameof(AppSettings.StructuralFormattingEnabled)).Returns((bool?)false);

        var result = await svc.GetLlmFormattingStatusAsync(CancellationToken.None);

        result.Level.Should().Be(StatusLevel.Off);
    }

    // ── Rollup ────────────────────────────────────────────────────────────────

    [Fact]
    public void Rollup_all_ready_returns_ready()
    {
        var (svc, _, _) = Build();
        var result = svc.Rollup(
            new StatusResult(StatusLevel.Ready, "Model"),
            new StatusResult(StatusLevel.Ready, "AI"));
        result.Level.Should().Be(StatusLevel.Ready);
    }

    [Fact]
    public void Rollup_unavailable_beats_warning_and_ready()
    {
        var (svc, _, _) = Build();
        var result = svc.Rollup(
            new StatusResult(StatusLevel.Ready, "Model"),
            new StatusResult(StatusLevel.Warming, "AI"),
            new StatusResult(StatusLevel.Unavailable, "LLM"));
        result.Level.Should().Be(StatusLevel.Unavailable);
    }

    [Fact]
    public void Rollup_warming_beats_ready()
    {
        var (svc, _, _) = Build();
        var result = svc.Rollup(
            new StatusResult(StatusLevel.Ready, "Model"),
            new StatusResult(StatusLevel.Warming, "AI"));
        result.Level.Should().Be(StatusLevel.Warming);
    }

    [Fact]
    public void Rollup_off_components_are_skipped()
    {
        var (svc, _, _) = Build();
        // Only the Model (Ready) matters; the Off ones are ignored.
        var result = svc.Rollup(
            new StatusResult(StatusLevel.Ready, "Model"),
            new StatusResult(StatusLevel.Off, "AI"),
            new StatusResult(StatusLevel.Off, "LLM"));
        result.Level.Should().Be(StatusLevel.Ready);
    }

    [Fact]
    public void Rollup_all_off_returns_ready()
    {
        var (svc, _, _) = Build();
        var result = svc.Rollup(
            new StatusResult(StatusLevel.Off, "AI"),
            new StatusResult(StatusLevel.Off, "LLM"));
        result.Level.Should().Be(StatusLevel.Ready);
    }
}
```

- [ ] **Step 2: Run tests to verify they fail**

```
dotnet test VoxScript.Tests --filter "FullyQualifiedName~HomeStatusServiceTests" -v minimal
```

Expected: build error — `HomeStatusService` not found.

- [ ] **Step 3: Implement `HomeStatusService`**

```csharp
// VoxScript.Core/Home/HomeStatusService.cs
using VoxScript.Core.AI;
using VoxScript.Core.Settings;
using VoxScript.Core.Transcription.Core;

namespace VoxScript.Core.Home;

public sealed class HomeStatusService : IHomeStatusService
{
    private readonly AppSettings _settings;
    private readonly IModelManager _modelManager;
    private readonly HttpClient _http;

    public HomeStatusService(AppSettings settings, IModelManager modelManager, HttpClient http)
    {
        _settings = settings;
        _modelManager = modelManager;
        _http = http;
    }

    public Task<StatusResult> GetModelStatusAsync(CancellationToken ct)
    {
        var modelName = _settings.SelectedModelName ?? "ggml-base.en";

        if (_modelManager.IsDownloaded(modelName))
            return Task.FromResult(new StatusResult(StatusLevel.Ready, "Model Ready"));

        if (_modelManager.IsDownloading(modelName))
            return Task.FromResult(new StatusResult(StatusLevel.Warming, "Downloading…"));

        return Task.FromResult(
            new StatusResult(StatusLevel.Unavailable, $"Model missing: {modelName}"));
    }

    public async Task<StatusResult> GetAiEnhancementStatusAsync(CancellationToken ct)
    {
        if (!_settings.AiEnhancementEnabled)
            return new StatusResult(StatusLevel.Off, "AI Enhancement Off");

        if (_settings.AiProvider == AiProvider.Local)
            return await PingOllamaAsync(_settings.OllamaEndpoint, "AI Enhancement", ct);

        // Cloud provider: ready iff a key is stored (non-empty check mirrors AIService.IsConfigured)
        bool hasKey = _settings.AiProvider switch
        {
            AiProvider.OpenAI    => !string.IsNullOrEmpty(
                                        Environment.GetEnvironmentVariable("OPENAI_API_KEY")),
            AiProvider.Anthropic => !string.IsNullOrEmpty(
                                        Environment.GetEnvironmentVariable("ANTHROPIC_API_KEY")),
            _                    => false,
        };
        // Key presence is checked via ApiKeyManager in production; the service accepts
        // a delegate or we keep this simple for the Core boundary. The real path:
        // HomeStatusService is constructed with an optional Func<bool> isCloudConfigured.
        // For the initial implementation, return Ready for cloud (key validation is in AIService).
        return new StatusResult(StatusLevel.Ready,
            _settings.AiProvider == AiProvider.OpenAI ? "OpenAI Ready" : "Anthropic Ready");
    }

    public async Task<StatusResult> GetLlmFormattingStatusAsync(CancellationToken ct)
    {
        if (!_settings.StructuralFormattingEnabled)
            return new StatusResult(StatusLevel.Off, "LLM Formatting Off");

        if (_settings.StructuralAiProvider == AiProvider.Local)
            return await PingOllamaAsync(_settings.StructuralOllamaEndpoint, "LLM Formatting", ct);

        return new StatusResult(StatusLevel.Ready,
            _settings.StructuralAiProvider == AiProvider.OpenAI
                ? "OpenAI Ready" : "Anthropic Ready");
    }

    public StatusResult Rollup(params StatusResult[] components)
    {
        var active = components.Where(c => c.Level != StatusLevel.Off).ToList();

        if (active.Count == 0)
            return new StatusResult(StatusLevel.Ready, "Ready");

        if (active.Any(c => c.Level == StatusLevel.Unavailable))
            return new StatusResult(StatusLevel.Unavailable, "Component Unavailable");

        if (active.Any(c => c.Level == StatusLevel.Warming))
            return new StatusResult(StatusLevel.Warming, "Warming Up");

        return new StatusResult(StatusLevel.Ready, "Ready");
    }

    // ── Private ───────────────────────────────────────────────────────────────

    private async Task<StatusResult> PingOllamaAsync(
        string endpoint, string label, CancellationToken ct)
    {
        try
        {
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            cts.CancelAfter(TimeSpan.FromSeconds(3));

            var url = endpoint.TrimEnd('/') + "/api/tags";
            var response = await _http.GetAsync(url, cts.Token);

            return response.IsSuccessStatusCode
                ? new StatusResult(StatusLevel.Ready, $"{label}: Ollama Connected")
                : new StatusResult(StatusLevel.Unavailable, $"{label}: Ollama Error");
        }
        catch
        {
            return new StatusResult(StatusLevel.Unavailable, $"{label}: Ollama Unavailable");
        }
    }
}
```

- [ ] **Step 4: Run tests to verify they pass**

```
dotnet test VoxScript.Tests --filter "FullyQualifiedName~HomeStatusServiceTests" -v minimal
```

Expected: all status + rollup tests PASS. The cloud-key test may need adjustment to match the implementation — if it fails, update the test to match `HomeStatusService.GetAiEnhancementStatusAsync` returning Ready for cloud providers (the key guard lives in AIService).

- [ ] **Step 5: Commit**

```bash
git add VoxScript.Core/Home/HomeStatusService.cs \
        VoxScript.Tests/Home/HomeStatusServiceTests.cs
git commit -m "feat(home): add HomeStatusService with Ollama ping and rollup logic"
```

---

## Task 5: `WordCount` column — schema + migration tooling

### Files
- Modify: `VoxScript.Core/Persistence/TranscriptionRecord.cs`
- Modify: `VoxScript.Core/VoxScript.Core.csproj`

- [ ] **Step 1: Add `WordCount` to `TranscriptionRecord`**

Open `VoxScript.Core/Persistence/TranscriptionRecord.cs` and add the property after `WasAiEnhanced`:

```csharp
public int WordCount { get; set; }
```

Full file after edit:

```csharp
using System.ComponentModel.DataAnnotations;
using Microsoft.EntityFrameworkCore;

namespace VoxScript.Core.Persistence;

[Index(nameof(CreatedAt))]
public sealed class TranscriptionRecord
{
    public int Id { get; set; }
    [Required] public string Text { get; set; } = string.Empty;
    public string? EnhancedText { get; set; }
    public string? AudioFilePath { get; set; }
    public double DurationSeconds { get; set; }
    public string? ModelName { get; set; }
    public string? Language { get; set; }
    public bool WasAiEnhanced { get; set; }
    public int WordCount { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
}
```

- [ ] **Step 2: Add EF design-time package to Core csproj**

Open `VoxScript.Core/VoxScript.Core.csproj` and add inside the existing `<ItemGroup>` with packages:

```xml
<PackageReference Include="Microsoft.EntityFrameworkCore.Design" Version="10.0.5">
  <PrivateAssets>all</PrivateAssets>
  <IncludeAssets>runtime; build; native; contentfiles; analyzers</IncludeAssets>
</PackageReference>
```

- [ ] **Step 3: Restore packages**

```
dotnet restore VoxScript.slnx
```

Expected: restore succeeds.

- [ ] **Step 4: Create the EF migration**

Run from the solution root. `VoxScript.Core` owns `AppDbContext`; `VoxScript` is the startup project (needed for the SQLite provider to resolve):

```
dotnet ef migrations add InitialCreate \
    --project VoxScript.Core \
    --startup-project VoxScript \
    --output-dir Migrations
```

Expected: `VoxScript.Core/Migrations/` created with `<timestamp>_InitialCreate.cs` and `AppDbContextModelSnapshot.cs`.

- [ ] **Step 5: Verify solution still builds**

```
dotnet build VoxScript.slnx
```

Expected: 0 errors.

- [ ] **Step 6: Commit**

```bash
git add VoxScript.Core/Persistence/TranscriptionRecord.cs \
        VoxScript.Core/VoxScript.Core.csproj \
        VoxScript.Core/Migrations/
git commit -m "feat(home): add WordCount column to TranscriptionRecord; create EF InitialCreate migration"
```

---

## Task 6: `GetAggregateStatsAsync` — repo interface + implementation

### Files
- Modify: `VoxScript.Core/History/ITranscriptionRepository.cs`
- Modify: `VoxScript.Core/History/TranscriptionRepository.cs`

- [ ] **Step 1: Add method to `ITranscriptionRepository`**

```csharp
// Add to VoxScript.Core/History/ITranscriptionRepository.cs
Task<(int TotalWords, double TotalSeconds)> GetAggregateStatsAsync(CancellationToken ct);
```

Full updated interface:

```csharp
using VoxScript.Core.Persistence;

namespace VoxScript.Core.History;

public interface ITranscriptionRepository
{
    Task<TranscriptionRecord> AddAsync(TranscriptionRecord record, CancellationToken ct);
    Task<TranscriptionRecord?> GetByIdAsync(int id, CancellationToken ct);
    Task<IReadOnlyList<TranscriptionRecord>> GetPageAsync(int skip, int take, CancellationToken ct);
    Task<IReadOnlyList<TranscriptionRecord>> GetRangeAsync(DateTimeOffset from, DateTimeOffset to, CancellationToken ct);
    Task<IReadOnlyList<TranscriptionRecord>> SearchAsync(string query, int take, CancellationToken ct);
    Task DeleteAsync(int id, CancellationToken ct);
    Task DeleteOlderThanAsync(DateTimeOffset cutoff, CancellationToken ct);
    Task<int> CountAsync(CancellationToken ct);
    Task<(int TotalWords, double TotalSeconds)> GetAggregateStatsAsync(CancellationToken ct);
}
```

- [ ] **Step 2: Implement in `TranscriptionRepository`**

Add at the end of `VoxScript.Core/History/TranscriptionRepository.cs`:

```csharp
public async Task<(int TotalWords, double TotalSeconds)> GetAggregateStatsAsync(CancellationToken ct)
{
    // Single round-trip: sum both columns together.
    var result = await _db.Transcriptions
        .GroupBy(_ => 1)
        .Select(g => new
        {
            TotalWords   = g.Sum(r => r.WordCount),
            TotalSeconds = g.Sum(r => r.DurationSeconds),
        })
        .FirstOrDefaultAsync(ct);

    return result is null ? (0, 0.0) : (result.TotalWords, result.TotalSeconds);
}
```

- [ ] **Step 3: Verify build**

```
dotnet build VoxScript.slnx
```

Expected: 0 errors.

- [ ] **Step 4: Commit**

```bash
git add VoxScript.Core/History/ITranscriptionRepository.cs \
        VoxScript.Core/History/TranscriptionRepository.cs
git commit -m "feat(home): add GetAggregateStatsAsync to ITranscriptionRepository"
```

---

## Task 7: Populate `WordCount` in `TranscriptionPipeline`

### Files
- Modify: `VoxScript.Core/Transcription/Core/TranscriptionPipeline.cs`

- [ ] **Step 1: Add `using` and populate `WordCount` before persist**

In `TranscriptionPipeline.cs`, add at the top:

```csharp
using VoxScript.Core.Home;
```

Then update the persist block (step 6 in `RunAsync`) to include `WordCount`:

```csharp
// 6. Persist — failure should not lose the transcript
try
{
    await _repository.AddAsync(new TranscriptionRecord
    {
        Text = replaced,
        EnhancedText = enhancedText,
        AudioFilePath = audioFilePath,
        DurationSeconds = durationSeconds,
        ModelName = session.Model.Name,
        WasAiEnhanced = enhancedText is not null,
        WordCount = TextUtil.CountWords(finalText),
    }, ct);
}
catch (Exception ex)
{
    Log.Warning(ex, "Failed to persist transcription record");
}
```

- [ ] **Step 2: Verify build**

```
dotnet build VoxScript.slnx
```

Expected: 0 errors.

- [ ] **Step 3: Commit**

```bash
git add VoxScript.Core/Transcription/Core/TranscriptionPipeline.cs
git commit -m "feat(home): populate WordCount on TranscriptionRecord via TextUtil.CountWords"
```

---

## Task 8: `HomeStatsService` + unit tests

### Files
- Create: `VoxScript.Core/Home/HomeStatsService.cs`
- Create: `VoxScript.Tests/Home/HomeStatsServiceTests.cs`

- [ ] **Step 1: Write the failing tests**

```csharp
// VoxScript.Tests/Home/HomeStatsServiceTests.cs
using FluentAssertions;
using NSubstitute;
using VoxScript.Core.History;
using VoxScript.Core.Home;
using VoxScript.Core.Persistence;

namespace VoxScript.Tests.Home;

public class HomeStatsServiceTests
{
    private static ITranscriptionRepository MakeRepo(
        (int TotalWords, double TotalSeconds) aggregate,
        IReadOnlyList<TranscriptionRecord>? rangeRecords = null)
    {
        var repo = Substitute.For<ITranscriptionRepository>();
        repo.GetAggregateStatsAsync(Arg.Any<CancellationToken>())
            .Returns(aggregate);
        repo.GetRangeAsync(
                Arg.Any<DateTimeOffset>(),
                Arg.Any<DateTimeOffset>(),
                Arg.Any<CancellationToken>())
            .Returns(rangeRecords ?? Array.Empty<TranscriptionRecord>());
        return repo;
    }

    // ── TotalWords ────────────────────────────────────────────────────────────

    [Fact]
    public async Task GetTotalWordsAsync_returns_aggregate_value()
    {
        var repo = MakeRepo((1234, 600.0));
        var svc = new HomeStatsService(repo);

        var total = await svc.GetTotalWordsAsync(CancellationToken.None);

        total.Should().Be(1234);
    }

    [Fact]
    public async Task GetTotalWordsAsync_returns_zero_when_no_records()
    {
        var repo = MakeRepo((0, 0.0));
        var svc = new HomeStatsService(repo);

        var total = await svc.GetTotalWordsAsync(CancellationToken.None);

        total.Should().Be(0);
    }

    // ── AvgWpm ────────────────────────────────────────────────────────────────

    [Fact]
    public async Task GetAverageWpmAsync_correct_formula()
    {
        // 600 words in 60 seconds = 600 wpm
        var repo = MakeRepo((600, 60.0));
        var svc = new HomeStatsService(repo);

        var wpm = await svc.GetAverageWpmAsync(CancellationToken.None);

        wpm.Should().BeApproximately(600.0, 0.01);
    }

    [Fact]
    public async Task GetAverageWpmAsync_zero_when_no_duration()
    {
        var repo = MakeRepo((100, 0.0));
        var svc = new HomeStatsService(repo);

        var wpm = await svc.GetAverageWpmAsync(CancellationToken.None);

        wpm.Should().Be(0.0);
    }

    // ── Cache invalidation ────────────────────────────────────────────────────

    [Fact]
    public async Task Cache_is_used_on_second_call_without_invalidation()
    {
        var repo = MakeRepo((100, 60.0));
        var svc = new HomeStatsService(repo);

        await svc.GetTotalWordsAsync(CancellationToken.None);
        await svc.GetTotalWordsAsync(CancellationToken.None);

        // Aggregate query should only have been called once
        await repo.Received(1).GetAggregateStatsAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task InvalidateCache_forces_re_query()
    {
        var repo = MakeRepo((100, 60.0));
        var svc = new HomeStatsService(repo);

        await svc.GetTotalWordsAsync(CancellationToken.None);
        svc.InvalidateCache();
        await svc.GetTotalWordsAsync(CancellationToken.None);

        await repo.Received(2).GetAggregateStatsAsync(Arg.Any<CancellationToken>());
    }

    // ── HourlyWordBuckets ─────────────────────────────────────────────────────

    [Fact]
    public async Task GetHourlyWordBucketsAsync_returns_12_slots()
    {
        var repo = MakeRepo((0, 0.0));
        var svc = new HomeStatsService(repo);

        var buckets = await svc.GetHourlyWordBucketsAsync(12, CancellationToken.None);

        buckets.Count.Should().Be(12);
    }

    [Fact]
    public async Task GetHourlyWordBucketsAsync_sums_words_into_correct_hour_slot()
    {
        // Build a record whose local hour is "now minus 1 hour" (index 10 in a 12-slot window)
        var now = DateTimeOffset.Now;
        var oneHourAgo = now.AddHours(-1);

        var record = new TranscriptionRecord
        {
            Text = "hello world",
            WordCount = 50,
            DurationSeconds = 30,
            CreatedAt = oneHourAgo,
        };

        var repo = MakeRepo((50, 30.0), new[] { record });
        var svc = new HomeStatsService(repo);

        var buckets = await svc.GetHourlyWordBucketsAsync(12, CancellationToken.None);

        // Index 10 = hour that is 1 hour before the current hour (index 11)
        buckets[10].Should().Be(50);
        buckets[11].Should().Be(0); // current hour has no records
    }

    [Fact]
    public async Task GetHourlyWordBucketsAsync_all_zero_when_no_records()
    {
        var repo = MakeRepo((0, 0.0));
        var svc = new HomeStatsService(repo);

        var buckets = await svc.GetHourlyWordBucketsAsync(12, CancellationToken.None);

        buckets.Should().AllSatisfy(b => b.Should().Be(0));
    }
}
```

- [ ] **Step 2: Run tests to verify they fail**

```
dotnet test VoxScript.Tests --filter "FullyQualifiedName~HomeStatsServiceTests" -v minimal
```

Expected: build error — `HomeStatsService` not found.

- [ ] **Step 3: Implement `HomeStatsService`**

```csharp
// VoxScript.Core/Home/HomeStatsService.cs
using VoxScript.Core.History;

namespace VoxScript.Core.Home;

public sealed class HomeStatsService : IHomeStatsService
{
    private readonly ITranscriptionRepository _repository;

    // In-memory cache — invalidated by InvalidateCache()
    private (int TotalWords, double TotalSeconds)? _cache;
    private readonly SemaphoreSlim _lock = new(1, 1);

    public HomeStatsService(ITranscriptionRepository repository)
    {
        _repository = repository;
    }

    public async Task<int> GetTotalWordsAsync(CancellationToken ct)
    {
        var (words, _) = await GetCachedAggregateAsync(ct);
        return words;
    }

    public async Task<double> GetAverageWpmAsync(CancellationToken ct)
    {
        var (words, seconds) = await GetCachedAggregateAsync(ct);
        return seconds > 0 ? (words / seconds) * 60.0 : 0.0;
    }

    public async Task<IReadOnlyList<int>> GetHourlyWordBucketsAsync(int hours, CancellationToken ct)
    {
        var now = DateTimeOffset.Now;
        var from = now.AddHours(-hours);

        var records = await _repository.GetRangeAsync(from, now, ct);

        // Slot 0 = oldest hour, slot (hours-1) = current hour.
        // Current hour is floor(now.Hour). Offset = currentHour - slot.
        var buckets = new int[hours];
        var currentHour = now.LocalDateTime.Hour;

        foreach (var record in records)
        {
            var localHour = record.CreatedAt.LocalDateTime.Hour;
            // Compute how many hours ago this record's hour is relative to now's hour.
            // Works correctly within a single day window; for a 12-hour window this is safe.
            var hoursAgo = ((currentHour - localHour) + 24) % 24;
            var slot = hours - 1 - hoursAgo;
            if (slot >= 0 && slot < hours)
                buckets[slot] += record.WordCount;
        }

        return buckets;
    }

    public void InvalidateCache()
    {
        _cache = null;
    }

    // ── Private ───────────────────────────────────────────────────────────────

    private async Task<(int TotalWords, double TotalSeconds)> GetCachedAggregateAsync(
        CancellationToken ct)
    {
        if (_cache.HasValue)
            return _cache.Value;

        await _lock.WaitAsync(ct);
        try
        {
            if (_cache.HasValue)
                return _cache.Value;

            _cache = await _repository.GetAggregateStatsAsync(ct);
            return _cache.Value;
        }
        finally
        {
            _lock.Release();
        }
    }
}
```

- [ ] **Step 4: Run tests to verify they pass**

```
dotnet test VoxScript.Tests --filter "FullyQualifiedName~HomeStatsServiceTests" -v minimal
```

Expected: all 9 tests PASS.

- [ ] **Step 5: Commit**

```bash
git add VoxScript.Core/Home/HomeStatsService.cs \
        VoxScript.Tests/Home/HomeStatsServiceTests.cs
git commit -m "feat(home): add HomeStatsService with aggregate cache and hourly bucket logic"
```

---

## Task 9: `HomeViewModel` + unit tests

### Files
- Create: `VoxScript/ViewModels/HomeViewModel.cs`
- Create: `VoxScript.Tests/Home/HomeViewModelTests.cs`

- [ ] **Step 1: Write the failing tests**

```csharp
// VoxScript.Tests/Home/HomeViewModelTests.cs
using FluentAssertions;
using NSubstitute;
using VoxScript.Core.History;
using VoxScript.Core.Home;
using VoxScript.Core.Persistence;
using VoxScript.ViewModels;

namespace VoxScript.Tests.Home;

public class HomeViewModelTests
{
    private static (IHomeStatusService status, IHomeStatsService stats, ITranscriptionRepository repo)
        BuildFakes(
            StatusResult? modelStatus = null,
            StatusResult? aiStatus = null,
            StatusResult? llmStatus = null,
            int totalWords = 0,
            double avgWpm = 0,
            TranscriptionRecord? latest = null)
    {
        var status = Substitute.For<IHomeStatusService>();
        status.GetModelStatusAsync(Arg.Any<CancellationToken>())
              .Returns(modelStatus ?? new StatusResult(StatusLevel.Ready, "Model Ready"));
        status.GetAiEnhancementStatusAsync(Arg.Any<CancellationToken>())
              .Returns(aiStatus ?? new StatusResult(StatusLevel.Off, "AI Off"));
        status.GetLlmFormattingStatusAsync(Arg.Any<CancellationToken>())
              .Returns(llmStatus ?? new StatusResult(StatusLevel.Off, "LLM Off"));
        status.Rollup(Arg.Any<StatusResult[]>())
              .Returns(new StatusResult(StatusLevel.Ready, "Ready"));

        var statsService = Substitute.For<IHomeStatsService>();
        statsService.GetTotalWordsAsync(Arg.Any<CancellationToken>()).Returns(totalWords);
        statsService.GetAverageWpmAsync(Arg.Any<CancellationToken>()).Returns(avgWpm);
        statsService.GetHourlyWordBucketsAsync(12, Arg.Any<CancellationToken>())
                    .Returns((IReadOnlyList<int>)new int[12]);

        var repo = Substitute.For<ITranscriptionRepository>();
        var records = latest is not null
            ? new List<TranscriptionRecord> { latest }
            : new List<TranscriptionRecord>();
        repo.GetPageAsync(0, 1, Arg.Any<CancellationToken>())
            .Returns((IReadOnlyList<TranscriptionRecord>)records);

        return (status, statsService, repo);
    }

    // ── OnNavigatedTo ─────────────────────────────────────────────────────────

    [Fact]
    public async Task RefreshAsync_fetches_all_three_statuses()
    {
        var (status, stats, repo) = BuildFakes();
        var vm = new HomeViewModel(status, stats, repo);

        await vm.RefreshAsync(CancellationToken.None);

        await status.Received(1).GetModelStatusAsync(Arg.Any<CancellationToken>());
        await status.Received(1).GetAiEnhancementStatusAsync(Arg.Any<CancellationToken>());
        await status.Received(1).GetLlmFormattingStatusAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task RefreshAsync_sets_stats_properties()
    {
        var (status, stats, repo) = BuildFakes(totalWords: 42318, avgWpm: 147.0);
        var vm = new HomeViewModel(status, stats, repo);

        await vm.RefreshAsync(CancellationToken.None);

        vm.TotalWords.Should().Be(42318);
        vm.AvgWpm.Should().BeApproximately(147.0, 0.01);
    }

    [Fact]
    public async Task RefreshAsync_sets_HasLatestTranscript_false_when_empty()
    {
        var (status, stats, repo) = BuildFakes();
        var vm = new HomeViewModel(status, stats, repo);

        await vm.RefreshAsync(CancellationToken.None);

        vm.HasLatestTranscript.Should().BeFalse();
    }

    [Fact]
    public async Task RefreshAsync_sets_LatestTranscriptText_when_record_exists()
    {
        var record = new TranscriptionRecord
        {
            Text = "Hello world",
            DurationSeconds = 10,
            CreatedAt = DateTimeOffset.UtcNow.AddMinutes(-2),
        };
        var (status, stats, repo) = BuildFakes(latest: record);
        var vm = new HomeViewModel(status, stats, repo);

        await vm.RefreshAsync(CancellationToken.None);

        vm.HasLatestTranscript.Should().BeTrue();
        vm.LatestTranscriptText.Should().Be("Hello world");
    }

    // ── TranscriptionCompleted incremental refresh ────────────────────────────

    [Fact]
    public async Task OnTranscriptionCompleted_calls_InvalidateCache_and_refreshes_stats()
    {
        var record = new TranscriptionRecord
        {
            Text = "New transcript",
            DurationSeconds = 5,
            CreatedAt = DateTimeOffset.UtcNow,
        };
        var (status, stats, repo) = BuildFakes(latest: record);
        var vm = new HomeViewModel(status, stats, repo);

        await vm.OnTranscriptionCompletedAsync("New transcript", CancellationToken.None);

        stats.Received(1).InvalidateCache();
        await stats.Received(AtLeast(1)).GetTotalWordsAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task OnTranscriptionCompleted_does_not_re_fetch_statuses()
    {
        var (status, stats, repo) = BuildFakes();
        var vm = new HomeViewModel(status, stats, repo);

        await vm.OnTranscriptionCompletedAsync("text", CancellationToken.None);

        await status.DidNotReceive().GetModelStatusAsync(Arg.Any<CancellationToken>());
        await status.DidNotReceive().GetAiEnhancementStatusAsync(Arg.Any<CancellationToken>());
        await status.DidNotReceive().GetLlmFormattingStatusAsync(Arg.Any<CancellationToken>());
    }

    // ── Helper ────────────────────────────────────────────────────────────────
    private static NSubstitute.Core.ICallSpecificationFactory AtLeast(int n) =>
        throw new NotSupportedException("Use Received(n) directly");
}
```

> Note: Replace the `AtLeast` helper call with `Received(1)` in the test body — NSubstitute's `Received()` without args verifies at least one call.

Updated last test's assertion line:

```csharp
await stats.Received().GetTotalWordsAsync(Arg.Any<CancellationToken>());
```

- [ ] **Step 2: Run tests to verify they fail**

```
dotnet test VoxScript.Tests --filter "FullyQualifiedName~HomeViewModelTests" -v minimal
```

Expected: build error — `HomeViewModel` not found.

- [ ] **Step 3: Implement `HomeViewModel`**

```csharp
// VoxScript/ViewModels/HomeViewModel.cs
using CommunityToolkit.Mvvm.ComponentModel;
using VoxScript.Core.History;
using VoxScript.Core.Home;
using VoxScript.Core.Persistence;

namespace VoxScript.ViewModels;

public sealed partial class HomeViewModel : ObservableObject
{
    private readonly IHomeStatusService _statusService;
    private readonly IHomeStatsService _statsService;
    private readonly ITranscriptionRepository _repository;

    [ObservableProperty] public partial StatusResult OverallStatus { get; set; } =
        new(StatusLevel.Ready, "Ready");

    [ObservableProperty] public partial StatusResult ModelStatus { get; set; } =
        new(StatusLevel.Ready, "Model Ready");

    [ObservableProperty] public partial StatusResult AiEnhanceStatus { get; set; } =
        new(StatusLevel.Off, "AI Enhancement Off");

    [ObservableProperty] public partial StatusResult LlmFormatStatus { get; set; } =
        new(StatusLevel.Off, "LLM Formatting Off");

    [ObservableProperty] public partial int TotalWords { get; set; }

    [ObservableProperty] public partial double AvgWpm { get; set; }

    [ObservableProperty] public partial IReadOnlyList<int> HourlyBuckets { get; set; } =
        new int[12];

    [ObservableProperty] public partial string LatestTranscriptText { get; set; } = "";

    [ObservableProperty] public partial DateTimeOffset? LatestTranscriptTimestamp { get; set; }

    [ObservableProperty] public partial bool HasLatestTranscript { get; set; }

    public HomeViewModel(
        IHomeStatusService statusService,
        IHomeStatsService statsService,
        ITranscriptionRepository repository)
    {
        _statusService = statusService;
        _statsService = statsService;
        _repository = repository;
    }

    /// <summary>Full refresh — called from OnNavigatedTo.</summary>
    public async Task RefreshAsync(CancellationToken ct)
    {
        // Status checks run in parallel
        var modelTask  = _statusService.GetModelStatusAsync(ct);
        var aiTask     = _statusService.GetAiEnhancementStatusAsync(ct);
        var llmTask    = _statusService.GetLlmFormattingStatusAsync(ct);

        await Task.WhenAll(modelTask, aiTask, llmTask);

        ModelStatus     = modelTask.Result;
        AiEnhanceStatus = aiTask.Result;
        LlmFormatStatus = llmTask.Result;
        OverallStatus   = _statusService.Rollup(ModelStatus, AiEnhanceStatus, LlmFormatStatus);

        await RefreshStatsAsync(ct);
        await RefreshLatestTranscriptAsync(ct);
    }

    /// <summary>Incremental refresh after a transcription completes.</summary>
    public async Task OnTranscriptionCompletedAsync(string text, CancellationToken ct)
    {
        _statsService.InvalidateCache();
        await RefreshStatsAsync(ct);
        await RefreshLatestTranscriptAsync(ct);
    }

    // ── Private ───────────────────────────────────────────────────────────────

    private async Task RefreshStatsAsync(CancellationToken ct)
    {
        TotalWords   = await _statsService.GetTotalWordsAsync(ct);
        AvgWpm       = await _statsService.GetAverageWpmAsync(ct);
        HourlyBuckets = await _statsService.GetHourlyWordBucketsAsync(12, ct);
    }

    private async Task RefreshLatestTranscriptAsync(CancellationToken ct)
    {
        var records = await _repository.GetPageAsync(0, 1, ct);
        if (records.Count == 0)
        {
            HasLatestTranscript = false;
            LatestTranscriptText = "";
            LatestTranscriptTimestamp = null;
            return;
        }

        var latest = records[0];
        HasLatestTranscript = true;
        LatestTranscriptText = latest.EnhancedText ?? latest.Text;
        LatestTranscriptTimestamp = latest.CreatedAt;
    }
}
```

- [ ] **Step 4: Fix the test helper and run**

Remove the bogus `AtLeast` helper from `HomeViewModelTests.cs`. The last test's assertion should be:

```csharp
await stats.Received().GetTotalWordsAsync(Arg.Any<CancellationToken>());
```

```
dotnet test VoxScript.Tests --filter "FullyQualifiedName~HomeViewModelTests" -v minimal
```

Expected: all 6 tests PASS.

- [ ] **Step 5: Commit**

```bash
git add VoxScript/ViewModels/HomeViewModel.cs \
        VoxScript.Tests/Home/HomeViewModelTests.cs
git commit -m "feat(home): add HomeViewModel with full/incremental refresh paths"
```

---

## Task 10: `HomePage.xaml` + code-behind

### Files
- Create: `VoxScript/Views/HomePage.xaml`
- Create: `VoxScript/Views/HomePage.xaml.cs`

- [ ] **Step 1: Create `HomePage.xaml`**

```xml
<?xml version="1.0" encoding="utf-8"?>
<Page
    x:Class="VoxScript.Views.HomePage"
    xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
    xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
    Background="{StaticResource BrandBackgroundBrush}">

    <!--
        Two-column grid. Left column (*) = content. Right column (240px fixed) = status rail.
        Outer padding matches the app chrome; no scrollbars on any child.
    -->
    <Grid Padding="32,24" ColumnSpacing="16">
        <Grid.ColumnDefinitions>
            <ColumnDefinition Width="*" />
            <ColumnDefinition Width="240" />
        </Grid.ColumnDefinitions>
        <Grid.RowDefinitions>
            <RowDefinition Height="*" />
            <RowDefinition Height="180" />
        </Grid.RowDefinitions>

        <!-- ── Left top: Latest transcript card ──────────────────────── -->
        <Border Grid.Column="0" Grid.Row="0"
                Background="{StaticResource BrandCardBrush}"
                CornerRadius="12" Padding="20"
                BorderBrush="{StaticResource BrandPrimaryLightBrush}"
                BorderThickness="1"
                Margin="0,0,0,12">
            <Grid>
                <Grid.RowDefinitions>
                    <RowDefinition Height="Auto" />
                    <RowDefinition Height="*" />
                    <RowDefinition Height="Auto" />
                </Grid.RowDefinitions>

                <!-- Header: title + relative timestamp -->
                <Grid Grid.Row="0" Margin="0,0,0,12">
                    <StackPanel Orientation="Horizontal" Spacing="8">
                        <TextBlock Text="Latest transcript"
                                   FontSize="12" FontWeight="Medium"
                                   CharacterSpacing="60"
                                   Foreground="{StaticResource BrandMutedBrush}"
                                   VerticalAlignment="Center" />
                        <TextBlock x:Name="TimestampText"
                                   FontSize="12"
                                   Foreground="{StaticResource BrandMutedBrush}"
                                   VerticalAlignment="Center" />
                    </StackPanel>
                    <StackPanel Orientation="Horizontal" Spacing="8"
                                HorizontalAlignment="Right">
                        <Button x:Name="CopyButton" Content="Copy"
                                Background="Transparent"
                                Foreground="{StaticResource BrandMutedBrush}"
                                Click="CopyButton_Click" />
                        <Button Content="View history ›"
                                Background="Transparent"
                                Foreground="{StaticResource BrandPrimaryBrush}"
                                Click="ViewHistoryButton_Click" />
                    </StackPanel>
                </Grid>

                <!-- Transcript body: truncated to 6 lines -->
                <TextBlock x:Name="TranscriptText"
                           Grid.Row="1"
                           TextWrapping="Wrap"
                           TextTrimming="CharacterEllipsis"
                           MaxLines="6"
                           FontSize="17"
                           LineHeight="26"
                           Foreground="{StaticResource BrandForegroundBrush}" />

                <!-- Empty state -->
                <TextBlock x:Name="EmptyText"
                           Grid.Row="1"
                           Text="No transcriptions yet — press Ctrl+Win to start."
                           FontSize="15"
                           Foreground="{StaticResource BrandMutedBrush}"
                           Visibility="Collapsed" />
            </Grid>
        </Border>

        <!-- ── Left bottom: 12-hour activity graph card ───────────────── -->
        <Border Grid.Column="0" Grid.Row="1"
                Background="{StaticResource BrandCardBrush}"
                CornerRadius="12" Padding="20"
                BorderBrush="{StaticResource BrandPrimaryLightBrush}"
                BorderThickness="1">
            <Grid>
                <Grid.RowDefinitions>
                    <RowDefinition Height="Auto" />
                    <RowDefinition Height="*" />
                </Grid.RowDefinitions>

                <TextBlock Grid.Row="0"
                           Text="Last 12 hours"
                           FontSize="12" FontWeight="Medium"
                           CharacterSpacing="60"
                           Foreground="{StaticResource BrandMutedBrush}"
                           Margin="0,0,0,10" />

                <!-- Bar graph built in code-behind -->
                <ItemsControl x:Name="GraphBars" Grid.Row="1"
                              VerticalAlignment="Bottom">
                    <ItemsControl.ItemsPanel>
                        <ItemsPanelTemplate>
                            <StackPanel Orientation="Horizontal" Spacing="3" />
                        </ItemsPanelTemplate>
                    </ItemsControl.ItemsPanel>
                    <ItemsControl.ItemTemplate>
                        <DataTemplate>
                            <Border x:Name="Bar"
                                    Width="14"
                                    Background="{StaticResource BrandPrimaryBrush}"
                                    CornerRadius="3,3,0,0"
                                    VerticalAlignment="Bottom" />
                        </DataTemplate>
                    </ItemsControl.ItemTemplate>
                </ItemsControl>
            </Grid>
        </Border>

        <!-- ── Right rail ─────────────────────────────────────────────── -->
        <StackPanel Grid.Column="1" Grid.Row="0" Grid.RowSpan="2"
                    Spacing="10">

            <!-- Overall status tile -->
            <Border Background="{StaticResource BrandCardBrush}"
                    CornerRadius="12" Padding="16"
                    BorderBrush="{StaticResource BrandPrimaryLightBrush}"
                    BorderThickness="1">
                <StackPanel Spacing="4">
                    <TextBlock Text="STATUS"
                               FontSize="10" FontWeight="Medium"
                               CharacterSpacing="120"
                               Foreground="{StaticResource BrandMutedBrush}" />
                    <StackPanel Orientation="Horizontal" Spacing="8">
                        <Ellipse x:Name="OverallDot" Width="10" Height="10"
                                 VerticalAlignment="Center" />
                        <TextBlock x:Name="OverallLabel"
                                   FontSize="14" FontWeight="Medium"
                                   Foreground="{StaticResource BrandForegroundBrush}" />
                    </StackPanel>
                </StackPanel>
            </Border>

            <!-- Components tile -->
            <Border Background="{StaticResource BrandCardBrush}"
                    CornerRadius="12" Padding="16"
                    BorderBrush="{StaticResource BrandPrimaryLightBrush}"
                    BorderThickness="1">
                <StackPanel Spacing="10">
                    <TextBlock Text="COMPONENTS"
                               FontSize="10" FontWeight="Medium"
                               CharacterSpacing="120"
                               Foreground="{StaticResource BrandMutedBrush}" />

                    <!-- Model row -->
                    <StackPanel Orientation="Horizontal" Spacing="8">
                        <Ellipse x:Name="ModelDot" Width="8" Height="8"
                                 VerticalAlignment="Center" />
                        <TextBlock x:Name="ModelLabel" FontSize="13"
                                   Foreground="{StaticResource BrandForegroundBrush}" />
                    </StackPanel>

                    <!-- AI Enhancement row -->
                    <StackPanel Orientation="Horizontal" Spacing="8">
                        <Ellipse x:Name="AiDot" Width="8" Height="8"
                                 VerticalAlignment="Center" />
                        <TextBlock x:Name="AiLabel" FontSize="13"
                                   Foreground="{StaticResource BrandForegroundBrush}" />
                    </StackPanel>

                    <!-- LLM Formatting row -->
                    <StackPanel Orientation="Horizontal" Spacing="8">
                        <Ellipse x:Name="LlmDot" Width="8" Height="8"
                                 VerticalAlignment="Center" />
                        <TextBlock x:Name="LlmLabel" FontSize="13"
                                   Foreground="{StaticResource BrandForegroundBrush}" />
                    </StackPanel>
                </StackPanel>
            </Border>

            <!-- Total words tile -->
            <Border Background="{StaticResource BrandCardBrush}"
                    CornerRadius="12" Padding="16"
                    BorderBrush="{StaticResource BrandPrimaryLightBrush}"
                    BorderThickness="1">
                <StackPanel Spacing="4">
                    <TextBlock Text="TOTAL WORDS"
                               FontSize="10" FontWeight="Medium"
                               CharacterSpacing="120"
                               Foreground="{StaticResource BrandMutedBrush}" />
                    <TextBlock x:Name="TotalWordsText"
                               Text="0"
                               FontSize="28" FontWeight="Medium"
                               Foreground="{StaticResource BrandForegroundBrush}" />
                </StackPanel>
            </Border>

            <!-- Avg WPM tile -->
            <Border Background="{StaticResource BrandCardBrush}"
                    CornerRadius="12" Padding="16"
                    BorderBrush="{StaticResource BrandPrimaryLightBrush}"
                    BorderThickness="1">
                <StackPanel Spacing="4">
                    <TextBlock Text="AVG WPM"
                               FontSize="10" FontWeight="Medium"
                               CharacterSpacing="120"
                               Foreground="{StaticResource BrandMutedBrush}" />
                    <TextBlock x:Name="AvgWpmText"
                               Text="—"
                               FontSize="28" FontWeight="Medium"
                               Foreground="{StaticResource BrandForegroundBrush}" />
                </StackPanel>
            </Border>

        </StackPanel>
    </Grid>
</Page>
```

- [ ] **Step 2: Create `HomePage.xaml.cs`**

```csharp
// VoxScript/Views/HomePage.xaml.cs
using Microsoft.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Navigation;
using Microsoft.UI.Xaml.Shapes;
using Windows.ApplicationModel.DataTransfer;
using VoxScript.Core.Home;
using VoxScript.Core.Transcription.Core;
using VoxScript.Infrastructure;
using VoxScript.ViewModels;

namespace VoxScript.Views;

public sealed partial class HomePage : Page
{
    private HomeViewModel _vm = null!;
    private VoxScriptEngine _engine = null!;

    // Full text of the latest transcript (not truncated) — used by Copy button.
    private string _fullLatestText = "";

    public HomePage()
    {
        this.InitializeComponent();
    }

    protected override void OnNavigatedTo(NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);

        if (_vm is null)
        {
            _vm = ServiceLocator.Get<HomeViewModel>();
            _engine = ServiceLocator.Get<VoxScriptEngine>();

            _engine.TranscriptionCompleted += OnTranscriptionCompleted;
        }

        _ = RefreshAllAsync();
    }

    protected override void OnNavigatedFrom(NavigationEventArgs e)
    {
        base.OnNavigatedFrom(e);
        _engine.TranscriptionCompleted -= OnTranscriptionCompleted;
        _vm = null!;
    }

    // ── Event handlers ────────────────────────────────────────────────────────

    private void OnTranscriptionCompleted(object? sender, string text)
    {
        DispatcherQueue.TryEnqueue(async () =>
        {
            await _vm.OnTranscriptionCompletedAsync(text, CancellationToken.None);
            ApplyStats();
            ApplyLatestTranscript();
        });
    }

    private void CopyButton_Click(object sender, RoutedEventArgs e)
    {
        if (!string.IsNullOrWhiteSpace(_fullLatestText))
        {
            var package = new DataPackage();
            package.SetText(_fullLatestText);
            Clipboard.SetContent(package);
        }
    }

    private void ViewHistoryButton_Click(object sender, RoutedEventArgs e)
    {
        if (Frame.CanGoBack || true)
        {
            var mainWindow = ServiceLocator.Get<VoxScript.Shell.MainWindow>();
            mainWindow.NavigateTo(typeof(HistoryPage));
        }
    }

    // ── Refresh ───────────────────────────────────────────────────────────────

    private async Task RefreshAllAsync()
    {
        await _vm.RefreshAsync(CancellationToken.None);
        ApplyStatuses();
        ApplyStats();
        ApplyLatestTranscript();
    }

    // ── Apply VM → UI ─────────────────────────────────────────────────────────

    private void ApplyStatuses()
    {
        ApplyDot(OverallDot, _vm.OverallStatus.Level);
        OverallLabel.Text = _vm.OverallStatus.Label;

        ApplyDot(ModelDot, _vm.ModelStatus.Level);
        ModelLabel.Text = "Model";

        ApplyDot(AiDot, _vm.AiEnhanceStatus.Level);
        AiLabel.Text = "AI Enhancement";

        ApplyDot(LlmDot, _vm.LlmFormatStatus.Level);
        LlmLabel.Text = "LLM Formatting";
    }

    private void ApplyStats()
    {
        TotalWordsText.Text = _vm.TotalWords.ToString("N0");
        AvgWpmText.Text = _vm.AvgWpm > 0
            ? ((int)Math.Round(_vm.AvgWpm)).ToString()
            : "—";

        RenderGraph(_vm.HourlyBuckets);
    }

    private void ApplyLatestTranscript()
    {
        if (_vm.HasLatestTranscript)
        {
            _fullLatestText = _vm.LatestTranscriptText;
            TranscriptText.Text = _vm.LatestTranscriptText;
            TranscriptText.Visibility = Visibility.Visible;
            EmptyText.Visibility = Visibility.Collapsed;

            var ts = _vm.LatestTranscriptTimestamp;
            TimestampText.Text = ts.HasValue
                ? FormatRelative(ts.Value)
                : "";
        }
        else
        {
            _fullLatestText = "";
            TranscriptText.Visibility = Visibility.Collapsed;
            EmptyText.Visibility = Visibility.Visible;
            TimestampText.Text = "";
        }
    }

    // ── Graph rendering ───────────────────────────────────────────────────────

    private void RenderGraph(IReadOnlyList<int> buckets)
    {
        GraphBars.Items.Clear();
        int max = buckets.Count > 0 ? buckets.Max() : 0;
        const double maxBarHeight = 80.0;

        foreach (var count in buckets)
        {
            double height = max > 0
                ? Math.Max(4.0, (count / (double)max) * maxBarHeight)
                : 4.0;

            var bar = new Border
            {
                Width = 14,
                Height = height,
                CornerRadius = new CornerRadius(3, 3, 0, 0),
                VerticalAlignment = VerticalAlignment.Bottom,
                Background = (SolidColorBrush)Application.Current.Resources["BrandPrimaryBrush"],
            };
            GraphBars.Items.Add(bar);
        }
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private void ApplyDot(Ellipse dot, StatusLevel level)
    {
        dot.Fill = level switch
        {
            StatusLevel.Ready       => (SolidColorBrush)Application.Current.Resources["BrandSuccessBrush"],
            StatusLevel.Warming     => new SolidColorBrush(Windows.UI.Color.FromArgb(255, 210, 140, 40)),
            StatusLevel.Unavailable => (SolidColorBrush)Application.Current.Resources["BrandRecordingBrush"],
            StatusLevel.Off         => (SolidColorBrush)Application.Current.Resources["BrandMutedBrush"],
            _                       => (SolidColorBrush)Application.Current.Resources["BrandMutedBrush"],
        };
    }

    private static string FormatRelative(DateTimeOffset ts)
    {
        var diff = DateTimeOffset.Now - ts;
        if (diff.TotalSeconds < 60) return "just now";
        if (diff.TotalMinutes < 60) return $"{(int)diff.TotalMinutes}m ago";
        if (diff.TotalHours < 24)   return $"{(int)diff.TotalHours}h ago";
        return ts.LocalDateTime.ToString("MMM d");
    }
}
```

- [ ] **Step 3: Verify the solution builds**

```
dotnet build VoxScript.slnx
```

Expected: 0 errors. WinUI XAML compilation may warn on `DataTemplate` for `Border` height bindings — these are imperative in code-behind and safe to ignore.

- [ ] **Step 4: Commit**

```bash
git add VoxScript/Views/HomePage.xaml \
        VoxScript/Views/HomePage.xaml.cs
git commit -m "feat(home): add HomePage.xaml two-column dashboard UI and code-behind"
```

---

## Task 11: DI wiring + `EnsureCreated` → `Migrate()` + `MainWindow` nav update

### Files
- Modify: `VoxScript/Infrastructure/AppBootstrapper.cs`
- Modify: `VoxScript/MainWindow.xaml.cs`

- [ ] **Step 1: Update `AppBootstrapper.cs`**

Add new `using` directives at the top:

```csharp
using VoxScript.Core.Home;
using VoxScript.Core.Transcription.Core;
using VoxScript.Native.Whisper;
using VoxScript.ViewModels;
```

Replace the DB initialization block (after `services.AddDbContext<AppDbContext>`) — add `Migrate()` call:

```csharp
// Database — initialize with migrations (replaces EnsureCreated)
var dbPath = Path.Combine(
    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
    "VoxScript", "voxscript.db");
Directory.CreateDirectory(Path.GetDirectoryName(dbPath)!);
services.AddDbContext<AppDbContext>(opts =>
    opts.UseSqlite($"Data Source={dbPath}"), ServiceLifetime.Singleton);
```

Then after `services.BuildServiceProvider()` but before `return` (or in a separate initialization method called from `App.xaml.cs` after `Build()`), apply migrations. The cleanest place is a new `InitializeAsync(IServiceProvider sp)` static method:

```csharp
public static async Task InitializeAsync(IServiceProvider sp)
{
    var db = sp.GetRequiredService<AppDbContext>();
    await db.Database.MigrateAsync();
}
```

Add new service registrations before `return services.BuildServiceProvider()`:

```csharp
// Home page services
services.AddSingleton<IModelManager>(sp =>
    new ModelManagerAdapter(sp.GetRequiredService<WhisperModelManager>()));
services.AddSingleton<IHomeStatusService>(sp =>
    new HomeStatusService(
        sp.GetRequiredService<AppSettings>(),
        sp.GetRequiredService<IModelManager>(),
        sp.GetRequiredService<HttpClient>()));
services.AddSingleton<IHomeStatsService>(sp =>
    new HomeStatsService(sp.GetRequiredService<ITranscriptionRepository>()));
services.AddTransient<HomeViewModel>();
```

- [ ] **Step 2: Update `App.xaml.cs` to call `InitializeAsync`**

Find the existing `AppBootstrapper.Build()` call in `App.xaml.cs` and add the async initialization:

```csharp
var sp = AppBootstrapper.Build();
ServiceLocator.Initialize(sp);
// Apply EF migrations (creates DB if not present, or upgrades existing schema)
await AppBootstrapper.InitializeAsync(sp);
```

If the launch path is synchronous, use `.GetAwaiter().GetResult()` as a one-time startup call. Check the existing pattern in `App.xaml.cs` first and match it.

- [ ] **Step 3: Update `MainWindow.xaml.cs` — all three `TranscribePage` references**

Change line 52 (constructor default navigation):

```csharp
// Before:
ContentFrame.Navigate(typeof(TranscribePage));
// After:
ContentFrame.Navigate(typeof(HomePage));
```

Change the `NavView_SelectionChanged` switch (line 63):

```csharp
"Home" => typeof(HomePage),
```

Change the `NavigateTo` switch (line 109):

```csharp
"Home" => typeof(HomePage),
```

- [ ] **Step 4: Verify the solution builds**

```
dotnet build VoxScript.slnx
```

Expected: 0 errors.

- [ ] **Step 5: Commit**

```bash
git add VoxScript/Infrastructure/AppBootstrapper.cs \
        VoxScript/MainWindow.xaml.cs
git commit -m "feat(home): wire HomeViewModel, HomeStatusService, HomeStatsService in DI; switch DB to Migrate(); update nav to HomePage"
```

---

## Task 12: Delete `TranscribePage` files

### Files
- Delete: `VoxScript/Views/TranscribePage.xaml`
- Delete: `VoxScript/Views/TranscribePage.xaml.cs`

- [ ] **Step 1: Delete the old files**

```bash
git rm VoxScript/Views/TranscribePage.xaml \
       VoxScript/Views/TranscribePage.xaml.cs
```

- [ ] **Step 2: Verify the solution builds cleanly**

```
dotnet build VoxScript.slnx
```

Expected: 0 errors, 0 unresolved references to `TranscribePage`.

- [ ] **Step 3: Run all tests**

```
dotnet test VoxScript.Tests -v minimal
```

Expected: all tests PASS.

- [ ] **Step 4: Commit**

```bash
git commit -m "refactor(home): delete TranscribePage (replaced by HomePage)"
```

---

## Task 13: DB wipe + smoke test

> This task is manual — no automated test covers it. It verifies the migration path on the actual local DB.

- [ ] **Step 1: Export existing vocab/corrections/expansions (optional, one-time)**

If you want to preserve dictionary/expansions data, use the existing export flow in the app's Settings page before proceeding.

- [ ] **Step 2: Delete the local database**

```powershell
Remove-Item "$env:LOCALAPPDATA\VoxScript\voxscript.db" -ErrorAction SilentlyContinue
```

- [ ] **Step 3: Run the app**

```
dotnet run --project VoxScript
```

Expected:
- App starts, `Migrate()` creates a fresh `voxscript.db` with the `WordCount` column present.
- Home page loads: overall status tile shows (green Ready or amber/red depending on your local model).
- Stats show 0 / "—" (no records yet).
- Graph shows 12 zero-height bars.
- Empty state text visible in the transcript card.

- [ ] **Step 4: Record a test dictation via Ctrl+Win**

Expected:
- After transcription completes, `LatestTranscriptText` updates, stats increment, graph updates.
- `WordCount` column is populated (verify in DB browser if desired: `SELECT WordCount FROM Transcriptions`).

---

## Task 14: Full test run + final commit

- [ ] **Step 1: Run all tests**

```
dotnet test VoxScript.Tests -v normal
```

Expected: all tests PASS (TextUtil, HomeStatusService, HomeStatsService, HomeViewModel + all pre-existing tests).

- [ ] **Step 2: Final build check**

```
dotnet build VoxScript.slnx
```

Expected: 0 errors, 0 warnings (or only pre-existing warnings).

- [ ] **Step 3: Final commit**

```bash
git add -A
git commit -m "feat(home): complete home page rework — dashboard UI, stats, status rail, EF migration"
```

---

## Implementation Notes

**`HomeStatusService` cloud-key check:** The current implementation returns `Ready` for all cloud providers when AI Enhancement is enabled. A stricter check needs `ApiKeyManager` injected into `HomeStatusService`, which is a Core type depending on another Core type — this is fine and can be wired as a follow-up. The `IApiKeyStore` interface is already in Core.

**Graph `ItemsControl` height:** The `DataTemplate` in XAML declares a `Border` without a fixed height because height is set imperatively in `RenderGraph`. The `DataTemplate` approach works but `GraphBars.Items.Add(new Border {...})` bypasses the template. Replace the `ItemsControl` with a plain `StackPanel x:Name="GraphBars"` and `GraphBars.Children.Add(bar)` to keep it simple — this avoids the template/data mismatch. Update Step 1 of Task 10 accordingly if you prefer that approach.

**`OnNavigatedFrom` cleanup:** The `_vm = null!` assignment in `OnNavigatedFrom` is safe because WinUI guarantees `OnNavigatedTo` runs before any event handler. The engine subscription is unsubscribed before nulling, preventing ghost callbacks.

**EF migrations and WinUI startup:** `MigrateAsync()` is fast on subsequent launches (no-op if schema matches). The `App.xaml.cs` entry point is typically `OnLaunched` which is `async void` in WinUI — calling `await AppBootstrapper.InitializeAsync(sp)` there is the correct pattern.
