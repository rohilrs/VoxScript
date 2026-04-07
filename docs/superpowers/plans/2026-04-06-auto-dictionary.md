# Auto-Add to Dictionary Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** After each transcription, automatically extract specialized words (proper nouns, technical terms, jargon) and add them to the vocabulary dictionary, filtering out common English words.

**Architecture:** A new `AutoVocabularyService` in Core extracts words from transcribed text, checks them against a ~10K common English word list (`CommonWordList`), deduplicates against existing vocabulary, and batch-adds new words via `IVocabularyRepository`. Integrated as a new pipeline step in `TranscriptionPipeline` (after word replacement, before AI enhancement), gated by `AppSettings.AutoAddToDictionary`.

**Tech Stack:** .NET 10 / C#, EF Core + SQLite, xUnit + FluentAssertions for tests.

---

## File Structure

| File | Action | Purpose |
|------|--------|---------|
| `VoxScript.Core/Dictionary/ICommonWordList.cs` | Create | Interface for common word lookup |
| `VoxScript.Core/Dictionary/CommonWordList.cs` | Create | HashSet-backed implementation, loads from file |
| `VoxScript.Core/Dictionary/IAutoVocabularyService.cs` | Create | Interface for auto-add service |
| `VoxScript.Core/Dictionary/AutoVocabularyService.cs` | Create | Extracts + filters + adds words |
| `VoxScript/Assets/Data/common-words.txt` | Create | ~10K common English words, one per line |
| `VoxScript/VoxScript.csproj` | Modify | Add content file reference for common-words.txt |
| `VoxScript.Core/Transcription/Core/TranscriptionPipeline.cs` | Modify | Add auto-vocabulary step after word replacement |
| `VoxScript/Infrastructure/AppBootstrapper.cs` | Modify | Register new services in DI |
| `VoxScript/Views/SettingsPage.xaml` | Modify | Enable the disabled toggle |
| `VoxScript.Tests/Dictionary/CommonWordListTests.cs` | Create | Tests for word list loading and lookup |
| `VoxScript.Tests/Dictionary/AutoVocabularyServiceTests.cs` | Create | Tests for word extraction and filtering |

---

### Task 1: ICommonWordList interface + CommonWordList implementation

**Files:**
- Create: `VoxScript.Core/Dictionary/ICommonWordList.cs`
- Create: `VoxScript.Core/Dictionary/CommonWordList.cs`
- Create: `VoxScript.Tests/Dictionary/CommonWordListTests.cs`

- [ ] **Step 1: Create the interface**

```csharp
// VoxScript.Core/Dictionary/ICommonWordList.cs
namespace VoxScript.Core.Dictionary;

public interface ICommonWordList
{
    bool Contains(string word);
}
```

- [ ] **Step 2: Write failing tests**

```csharp
// VoxScript.Tests/Dictionary/CommonWordListTests.cs
using FluentAssertions;
using VoxScript.Core.Dictionary;
using Xunit;

namespace VoxScript.Tests.Dictionary;

public sealed class CommonWordListTests : IDisposable
{
    private readonly string _tempFile;
    private readonly CommonWordList _list;

    public CommonWordListTests()
    {
        _tempFile = Path.GetTempFileName();
        File.WriteAllLines(_tempFile, ["the", "and", "hello", "world", "computer"]);
        _list = new CommonWordList(_tempFile);
    }

    public void Dispose() => File.Delete(_tempFile);

    [Fact]
    public void Contains_returns_true_for_listed_word()
    {
        _list.Contains("hello").Should().BeTrue();
    }

    [Fact]
    public void Contains_is_case_insensitive()
    {
        _list.Contains("HELLO").Should().BeTrue();
        _list.Contains("Hello").Should().BeTrue();
    }

    [Fact]
    public void Contains_returns_false_for_unlisted_word()
    {
        _list.Contains("kubernetes").Should().BeFalse();
    }

    [Fact]
    public void Contains_handles_empty_and_null()
    {
        _list.Contains("").Should().BeFalse();
        _list.Contains(null!).Should().BeFalse();
    }
}
```

- [ ] **Step 3: Run tests to verify they fail**

Run: `dotnet test VoxScript.Tests --filter "FullyQualifiedName~CommonWordListTests" -v n`
Expected: Compilation error — `CommonWordList` does not exist yet.

- [ ] **Step 4: Write the implementation**

```csharp
// VoxScript.Core/Dictionary/CommonWordList.cs
namespace VoxScript.Core.Dictionary;

public sealed class CommonWordList : ICommonWordList
{
    private readonly Lazy<HashSet<string>> _words;

    public CommonWordList(string filePath)
    {
        _words = new Lazy<HashSet<string>>(() =>
        {
            if (!File.Exists(filePath))
                return new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            var lines = File.ReadAllLines(filePath)
                .Where(l => !string.IsNullOrWhiteSpace(l))
                .Select(l => l.Trim());
            return new HashSet<string>(lines, StringComparer.OrdinalIgnoreCase);
        });
    }

    public bool Contains(string word) =>
        !string.IsNullOrEmpty(word) && _words.Value.Contains(word);
}
```

- [ ] **Step 5: Run tests to verify they pass**

Run: `dotnet test VoxScript.Tests --filter "FullyQualifiedName~CommonWordListTests" -v n`
Expected: All 4 tests PASS.

- [ ] **Step 6: Commit**

```bash
git add VoxScript.Core/Dictionary/ICommonWordList.cs VoxScript.Core/Dictionary/CommonWordList.cs VoxScript.Tests/Dictionary/CommonWordListTests.cs
git commit -m "feat: add CommonWordList with case-insensitive lookup"
```

---

### Task 2: IAutoVocabularyService interface + AutoVocabularyService implementation

**Files:**
- Create: `VoxScript.Core/Dictionary/IAutoVocabularyService.cs`
- Create: `VoxScript.Core/Dictionary/AutoVocabularyService.cs`
- Create: `VoxScript.Tests/Dictionary/AutoVocabularyServiceTests.cs`

- [ ] **Step 1: Create the interface**

```csharp
// VoxScript.Core/Dictionary/IAutoVocabularyService.cs
namespace VoxScript.Core.Dictionary;

public interface IAutoVocabularyService
{
    Task ProcessTranscriptionAsync(string text, CancellationToken ct);
}
```

- [ ] **Step 2: Write failing tests**

```csharp
// VoxScript.Tests/Dictionary/AutoVocabularyServiceTests.cs
using FluentAssertions;
using NSubstitute;
using VoxScript.Core.Dictionary;
using Xunit;

namespace VoxScript.Tests.Dictionary;

public sealed class AutoVocabularyServiceTests
{
    private readonly IVocabularyRepository _repo = Substitute.For<IVocabularyRepository>();
    private readonly ICommonWordList _commonWords = Substitute.For<ICommonWordList>();
    private readonly AutoVocabularyService _service;

    public AutoVocabularyServiceTests()
    {
        _repo.GetWordsAsync(Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<IReadOnlyList<string>>(new List<string>()));
        _service = new AutoVocabularyService(_repo, _commonWords);
    }

    [Fact]
    public async Task Adds_uncommon_words_to_vocabulary()
    {
        _commonWords.Contains("hello").Returns(true);
        _commonWords.Contains("kubernetes").Returns(false);

        await _service.ProcessTranscriptionAsync("hello Kubernetes", default);

        await _repo.Received(1).AddWordAsync("Kubernetes", Arg.Any<CancellationToken>());
        await _repo.DidNotReceive().AddWordAsync("hello", Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Skips_single_character_words()
    {
        _commonWords.Contains("a").Returns(false);
        _commonWords.Contains("i").Returns(false);

        await _service.ProcessTranscriptionAsync("a I test", default);

        await _repo.DidNotReceive().AddWordAsync("a", Arg.Any<CancellationToken>());
        await _repo.DidNotReceive().AddWordAsync("I", Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Skips_pure_numbers()
    {
        _commonWords.Contains("123").Returns(false);
        _commonWords.Contains("45").Returns(false);

        await _service.ProcessTranscriptionAsync("123 45 test", default);

        await _repo.DidNotReceive().AddWordAsync("123", Arg.Any<CancellationToken>());
        await _repo.DidNotReceive().AddWordAsync("45", Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Skips_words_already_in_vocabulary()
    {
        _commonWords.Contains("fastapi").Returns(false);
        _repo.GetWordsAsync(Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<IReadOnlyList<string>>(new List<string> { "FastAPI" }));

        await _service.ProcessTranscriptionAsync("FastAPI is great", default);

        await _repo.DidNotReceive().AddWordAsync("FastAPI", Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Deduplicates_within_same_transcription()
    {
        _commonWords.Contains("kubernetes").Returns(false);

        await _service.ProcessTranscriptionAsync("Kubernetes and Kubernetes again", default);

        await _repo.Received(1).AddWordAsync("Kubernetes", Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handles_empty_and_null_text()
    {
        await _service.ProcessTranscriptionAsync("", default);
        await _service.ProcessTranscriptionAsync(null!, default);

        await _repo.DidNotReceive().AddWordAsync(Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Strips_punctuation_from_words()
    {
        _commonWords.Contains("kubernetes").Returns(false);
        _commonWords.Contains("great").Returns(true);

        await _service.ProcessTranscriptionAsync("Kubernetes, great!", default);

        await _repo.Received(1).AddWordAsync("Kubernetes", Arg.Any<CancellationToken>());
    }
}
```

- [ ] **Step 3: Run tests to verify they fail**

Run: `dotnet test VoxScript.Tests --filter "FullyQualifiedName~AutoVocabularyServiceTests" -v n`
Expected: Compilation error — `AutoVocabularyService` does not exist yet.

- [ ] **Step 4: Write the implementation**

```csharp
// VoxScript.Core/Dictionary/AutoVocabularyService.cs
using System.Text.RegularExpressions;

namespace VoxScript.Core.Dictionary;

public sealed partial class AutoVocabularyService : IAutoVocabularyService
{
    private readonly IVocabularyRepository _repo;
    private readonly ICommonWordList _commonWords;

    public AutoVocabularyService(IVocabularyRepository repo, ICommonWordList commonWords)
    {
        _repo = repo;
        _commonWords = commonWords;
    }

    public async Task ProcessTranscriptionAsync(string text, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(text)) return;

        var existingWords = await _repo.GetWordsAsync(ct);
        var existingSet = new HashSet<string>(existingWords, StringComparer.OrdinalIgnoreCase);

        var tokens = WordSplitRegex().Split(text)
            .Where(t => !string.IsNullOrWhiteSpace(t))
            .ToList();

        var added = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var token in tokens)
        {
            // Skip single characters
            if (token.Length <= 1) continue;

            // Skip pure numbers
            if (token.All(char.IsDigit)) continue;

            // Skip common English words
            if (_commonWords.Contains(token)) continue;

            // Skip already in vocabulary
            if (existingSet.Contains(token)) continue;

            // Skip duplicates within this transcription
            if (!added.Add(token)) continue;

            await _repo.AddWordAsync(token, ct);
        }
    }

    [GeneratedRegex(@"[\s\p{P}]+", RegexOptions.Compiled)]
    private static partial Regex WordSplitRegex();
}
```

- [ ] **Step 5: Run tests to verify they pass**

Run: `dotnet test VoxScript.Tests --filter "FullyQualifiedName~AutoVocabularyServiceTests" -v n`
Expected: All 7 tests PASS.

- [ ] **Step 6: Commit**

```bash
git add VoxScript.Core/Dictionary/IAutoVocabularyService.cs VoxScript.Core/Dictionary/AutoVocabularyService.cs VoxScript.Tests/Dictionary/AutoVocabularyServiceTests.cs
git commit -m "feat: add AutoVocabularyService with word extraction and filtering"
```

---

### Task 3: Common words data file

**Files:**
- Create: `VoxScript/Assets/Data/common-words.txt`
- Modify: `VoxScript/VoxScript.csproj`

- [ ] **Step 1: Generate the common words file**

Create `VoxScript/Assets/Data/common-words.txt` containing ~10K common English words, one per line, all lowercase. Use a standard frequency list. The file should include the most common words in English (the, and, is, are, was, were, have, has, had, do, does, did, will, would, could, should, etc.) plus common nouns, verbs, adjectives, and adverbs.

A Python one-liner can generate this from NLTK or a web source, but for the plan we'll use a curated list. The file should be ~10,000 lines.

```bash
# Download a standard 10K word list (MIT licensed)
# Source: https://github.com/first20hours/google-10000-english
curl -sL "https://raw.githubusercontent.com/first20hours/google-10000-english/master/google-10000-english-usa-no-swears.txt" -o VoxScript/Assets/Data/common-words.txt
```

Verify the file has roughly 10K lines:
```bash
wc -l VoxScript/Assets/Data/common-words.txt
```
Expected: ~9,800-10,000 lines.

- [ ] **Step 2: Add as content file in csproj**

In `VoxScript/VoxScript.csproj`, add inside the existing `<ItemGroup>` that has other content/asset entries (or create a new `<ItemGroup>`):

```xml
<ItemGroup>
  <Content Include="Assets\Data\common-words.txt">
    <CopyToOutputDirectory>PreserveNewest</CopyToOutputDirectory>
  </Content>
</ItemGroup>
```

- [ ] **Step 3: Verify build**

Run: `dotnet build VoxScript.slnx`
Expected: Build succeeded. File should appear in output directory.

- [ ] **Step 4: Commit**

```bash
git add VoxScript/Assets/Data/common-words.txt VoxScript/VoxScript.csproj
git commit -m "feat: add 10K common English words list for auto-vocabulary filtering"
```

---

### Task 4: DI registration + pipeline integration

**Files:**
- Modify: `VoxScript/Infrastructure/AppBootstrapper.cs`
- Modify: `VoxScript.Core/Transcription/Core/TranscriptionPipeline.cs`

- [ ] **Step 1: Register services in AppBootstrapper**

In `AppBootstrapper.cs`, add `using VoxScript.Core.Dictionary;` if not already present.

After the `services.AddSingleton<WordReplacementService>();` line, add:

```csharp
services.AddSingleton<ICommonWordList>(sp =>
{
    var path = Path.Combine(AppContext.BaseDirectory, "Assets", "Data", "common-words.txt");
    return new CommonWordList(path);
});
services.AddSingleton<IAutoVocabularyService, AutoVocabularyService>();
```

- [ ] **Step 2: Add auto-vocabulary step to TranscriptionPipeline**

In `TranscriptionPipeline.cs`, add the new dependencies:

Add to the field declarations:
```csharp
private readonly IAutoVocabularyService _autoVocabulary;
private readonly Settings.AppSettings _settings;
```

Update the constructor signature and body to include the new parameters:

```csharp
public TranscriptionPipeline(
    TranscriptionOutputFilter filter,
    WhisperTextFormatter formatter,
    WordReplacementService wordReplacement,
    IAIEnhancementService aiEnhancement,
    ITranscriptionRepository repository,
    PowerModeSessionManager powerMode,
    IAutoVocabularyService autoVocabulary,
    Settings.AppSettings settings)
{
    _filter = filter;
    _formatter = formatter;
    _wordReplacement = wordReplacement;
    _aiEnhancement = aiEnhancement;
    _repository = repository;
    _powerMode = powerMode;
    _autoVocabulary = autoVocabulary;
    _settings = settings;
}
```

In the `RunAsync` method, add a new step between the word replacement block (step 4) and the AI enhancement block (step 5). Insert after the `replaced = ...` catch block closes:

```csharp
        // 4b. Auto-add uncommon words to vocabulary
        if (_settings.AutoAddToDictionary)
        {
            try
            {
                await _autoVocabulary.ProcessTranscriptionAsync(replaced, ct);
            }
            catch (Exception ex)
            {
                Log.Warning(ex, "Auto-vocabulary failed, skipping");
            }
        }
```

- [ ] **Step 3: Verify build**

Run: `dotnet build VoxScript.slnx`
Expected: Build succeeded.

- [ ] **Step 4: Run all tests**

Run: `dotnet test VoxScript.Tests -v n`
Expected: All tests pass.

- [ ] **Step 5: Commit**

```bash
git add VoxScript/Infrastructure/AppBootstrapper.cs VoxScript.Core/Transcription/Core/TranscriptionPipeline.cs
git commit -m "feat: wire AutoVocabularyService into DI and TranscriptionPipeline"
```

---

### Task 5: Enable the Settings toggle

**Files:**
- Modify: `VoxScript/Views/SettingsPage.xaml`

- [ ] **Step 1: Remove IsEnabled="False" from the toggle**

In `VoxScript/Views/SettingsPage.xaml`, find the `ToggleSwitch` bound to `AutoAddToDictionary` (around line 505-506):

```xml
<ToggleSwitch Grid.Column="1" MinWidth="0"
              OnContent="" OffContent=""
              IsOn="{x:Bind ViewModel.AutoAddToDictionary, Mode=TwoWay}"
              IsEnabled="False"
              VerticalAlignment="Center" />
```

Remove the `IsEnabled="False"` line so it becomes:

```xml
<ToggleSwitch Grid.Column="1" MinWidth="0"
              OnContent="" OffContent=""
              IsOn="{x:Bind ViewModel.AutoAddToDictionary, Mode=TwoWay}"
              VerticalAlignment="Center" />
```

- [ ] **Step 2: Verify build**

Run: `dotnet build VoxScript.slnx`
Expected: Build succeeded.

- [ ] **Step 3: Commit**

```bash
git add VoxScript/Views/SettingsPage.xaml
git commit -m "feat: enable auto-add to dictionary toggle in Settings"
```

---

### Task 6: Smoke test

- [ ] **Step 1: Run all tests**

Run: `dotnet test VoxScript.Tests -v n`
Expected: All tests pass (existing 63 + 4 CommonWordList + 7 AutoVocabularyService = 74 total).

- [ ] **Step 2: Verify build**

Run: `dotnet build VoxScript.slnx`
Expected: Build succeeded with no errors.

- [ ] **Step 3: Commit any final fixes**

If any issues were found and fixed:

```bash
git add -A
git commit -m "fix: resolve auto-vocabulary build issues"
```

---

## Summary

| Task | Files | What |
|------|-------|------|
| 1 | `ICommonWordList.cs`, `CommonWordList.cs`, tests | Word list interface + lazy HashSet implementation |
| 2 | `IAutoVocabularyService.cs`, `AutoVocabularyService.cs`, tests | Word extraction, filtering, dedup, batch-add |
| 3 | `common-words.txt`, `VoxScript.csproj` | 10K common English words data file |
| 4 | `AppBootstrapper.cs`, `TranscriptionPipeline.cs` | DI wiring + new pipeline step |
| 5 | `SettingsPage.xaml` | Enable the toggle |
| 6 | — | Smoke test |
