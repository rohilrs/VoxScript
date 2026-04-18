# LLM Structural Formatting Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add an optional LLM pass between `SmartTextFormatter` and `WordReplacement` that reformats voice transcriptions into numbered lists and semantic paragraphs without changing any words, backed by a shared `IAiCompleter` abstraction that also replaces the HTTP plumbing in `AIService`.

**Architecture:** Extract provider-switching HTTP logic from `AIService` into `IAiCompleter`/`AiCompleter`; create `IStructuralFormattingService`/`StructuralFormattingService` as the new consumer of `IAiCompleter`; insert step 3b into `TranscriptionPipeline` after `SmartTextFormatter` gated by `StructuralFormattingEnabled && IsConfigured`. Both AI features configure their provider/model/credentials independently. Output is validated by a word-count ratio check before acceptance; any failure silently falls back to rule-based output.

**Tech Stack:** C# 13 / .NET 10, xUnit, FluentAssertions, NSubstitute, existing `ISettingsStore`/`IApiKeyStore` infrastructure, WinUI 3 XAML (SettingsPage card)

---

## File Structure

### New files — `VoxScript.Core`
- `VoxScript.Core/AI/AiCompletionConfig.cs` — `AiCompletionConfig` record + no additional deps
- `VoxScript.Core/AI/IAiCompleter.cs` — single-method interface
- `VoxScript.Core/AI/AiCompleter.cs` — concrete: OpenAI / Anthropic / Ollama HTTP, delegates from `AIService`
- `VoxScript.Core/AI/IStructuralFormattingService.cs` — `IsConfigured` + `FormatAsync`
- `VoxScript.Core/AI/StructuralFormattingPrompt.cs` — static `System` string + `ValidateOutput` pure logic
- `VoxScript.Core/AI/StructuralFormattingService.cs` — calls `IAiCompleter`, enforces 5s timeout, returns `string?`

### Modified — `VoxScript.Core`
- `VoxScript.Core/AI/AIService.cs` — inject `IAiCompleter`; delete inline HTTP methods; delegate `CompleteAsync` through `AiCompleter`
- `VoxScript.Core/Settings/AppSettings.cs` — add 4 `Structural*` properties
- `VoxScript.Core/Settings/ApiKeyManager.cs` — add 4 `Structural` key methods
- `VoxScript.Core/Transcription/Core/TranscriptionPipeline.cs` — add `IStructuralFormattingService` constructor param; add step 3b; fix `Text` column to use `replaced` (already correct) and store structured text in `replaced` before word replacement

### New files — `VoxScript.Tests`
- `VoxScript.Tests/AI/AiCompleterTests.cs`
- `VoxScript.Tests/AI/StructuralFormattingPromptTests.cs`
- `VoxScript.Tests/AI/StructuralFormattingServiceTests.cs`
- `VoxScript.Tests/Transcription/TranscriptionPipelineStructuralTests.cs`

### Modified — `VoxScript` (UI)
- `VoxScript/Infrastructure/AppBootstrapper.cs` — register `IAiCompleter`, `IStructuralFormattingService`; update `AIService` registration
- `VoxScript/ViewModels/SettingsViewModel.cs` — add structural formatting observable props + provider-change default model swap
- `VoxScript/Views/SettingsPage.xaml` — new LLM FORMATTING card between AI Enhancement and APP
- `VoxScript/Views/SettingsPage.xaml.cs` — `SetStructuralApiKeyButton_Click` / `ClearStructuralApiKeyButton_Click`

---

## Task 1: `AiCompletionConfig` record and `IAiCompleter` interface

**Files:**
- Create: `VoxScript.Core/AI/AiCompletionConfig.cs`
- Create: `VoxScript.Core/AI/IAiCompleter.cs`

- [ ] **Step 1: Create `AiCompletionConfig.cs`**

```csharp
namespace VoxScript.Core.AI;

public sealed record AiCompletionConfig(
    AiProvider Provider,
    string Model,
    string OllamaEndpoint,
    string? ApiKey);
```

- [ ] **Step 2: Create `IAiCompleter.cs`**

```csharp
namespace VoxScript.Core.AI;

public interface IAiCompleter
{
    Task<string> CompleteAsync(
        AiCompletionConfig config,
        string systemPrompt,
        string userMessage,
        CancellationToken ct);
}
```

- [ ] **Step 3: Build to confirm no compile errors**

Run: `dotnet build VoxScript.Core/VoxScript.Core.csproj`
Expected: Build succeeded, 0 errors

- [ ] **Step 4: Commit**

```bash
git add VoxScript.Core/AI/AiCompletionConfig.cs VoxScript.Core/AI/IAiCompleter.cs
git commit -m "feat: add AiCompletionConfig record and IAiCompleter interface"
```

---

## Task 2: `AiCompleter` concrete implementation

**Files:**
- Create: `VoxScript.Core/AI/AiCompleter.cs`
- Test: `VoxScript.Tests/AI/AiCompleterTests.cs`

- [ ] **Step 1: Write failing tests**

```csharp
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using VoxScript.Core.AI;

namespace VoxScript.Tests.AI;

public class AiCompleterTests
{
    // ── Helper ────────────────────────────────────────────────────────────

    private static (AiCompleter sut, List<HttpRequestMessage> captured) BuildSut(
        HttpStatusCode status, string jsonBody)
    {
        var captured = new List<HttpRequestMessage>();
        var handler = new FakeHandler(status, jsonBody, captured);
        var http = new HttpClient(handler);
        return (new AiCompleter(http), captured);
    }

    // ── OpenAI ────────────────────────────────────────────────────────────

    [Fact]
    public async Task CompleteAsync_OpenAI_sends_correct_url_and_bearer_header()
    {
        var body = """{"choices":[{"message":{"content":"hello"}}]}""";
        var (sut, captured) = BuildSut(HttpStatusCode.OK, body);
        var config = new AiCompletionConfig(AiProvider.OpenAI, "gpt-4o-mini",
            "http://localhost:11434", "sk-test");

        await sut.CompleteAsync(config, "sys", "user", CancellationToken.None);

        captured.Should().HaveCount(1);
        captured[0].RequestUri!.ToString().Should().Be("https://api.openai.com/v1/chat/completions");
        captured[0].Headers.Authorization!.Scheme.Should().Be("Bearer");
        captured[0].Headers.Authorization!.Parameter.Should().Be("sk-test");
    }

    [Fact]
    public async Task CompleteAsync_OpenAI_returns_content_string()
    {
        var body = """{"choices":[{"message":{"content":"structured output"}}]}""";
        var (sut, _) = BuildSut(HttpStatusCode.OK, body);
        var config = new AiCompletionConfig(AiProvider.OpenAI, "gpt-4o-mini",
            "http://localhost:11434", "sk-test");

        var result = await sut.CompleteAsync(config, "sys", "user", CancellationToken.None);

        result.Should().Be("structured output");
    }

    [Fact]
    public async Task CompleteAsync_OpenAI_throws_on_non_success()
    {
        var (sut, _) = BuildSut(HttpStatusCode.Unauthorized, "{}");
        var config = new AiCompletionConfig(AiProvider.OpenAI, "gpt-4o-mini",
            "http://localhost:11434", "bad-key");

        var act = () => sut.CompleteAsync(config, "sys", "user", CancellationToken.None);

        await act.Should().ThrowAsync<HttpRequestException>();
    }

    // ── Anthropic ─────────────────────────────────────────────────────────

    [Fact]
    public async Task CompleteAsync_Anthropic_sends_correct_url_and_api_key_header()
    {
        var body = """{"content":[{"text":"anthropic response"}]}""";
        var (sut, captured) = BuildSut(HttpStatusCode.OK, body);
        var config = new AiCompletionConfig(AiProvider.Anthropic, "claude-haiku-4-5-20251001",
            "http://localhost:11434", "anthro-key");

        await sut.CompleteAsync(config, "sys", "user", CancellationToken.None);

        captured[0].RequestUri!.ToString().Should().Be("https://api.anthropic.com/v1/messages");
        captured[0].Headers.GetValues("x-api-key").First().Should().Be("anthro-key");
    }

    [Fact]
    public async Task CompleteAsync_Anthropic_returns_content_text()
    {
        var body = """{"content":[{"text":"formatted text"}]}""";
        var (sut, _) = BuildSut(HttpStatusCode.OK, body);
        var config = new AiCompletionConfig(AiProvider.Anthropic, "claude-haiku-4-5-20251001",
            "http://localhost:11434", "anthro-key");

        var result = await sut.CompleteAsync(config, "sys", "user", CancellationToken.None);

        result.Should().Be("formatted text");
    }

    // ── Ollama ────────────────────────────────────────────────────────────

    [Fact]
    public async Task CompleteAsync_Ollama_sends_to_configured_endpoint()
    {
        var body = """{"message":{"content":"local response"}}""";
        var (sut, captured) = BuildSut(HttpStatusCode.OK, body);
        var config = new AiCompletionConfig(AiProvider.Local, "qwen2.5:3b",
            "http://localhost:11434", null);

        await sut.CompleteAsync(config, "sys", "user", CancellationToken.None);

        captured[0].RequestUri!.ToString().Should().Be("http://localhost:11434/api/chat");
    }

    [Fact]
    public async Task CompleteAsync_Ollama_returns_message_content()
    {
        var body = """{"message":{"content":"list formatted"}}""";
        var (sut, _) = BuildSut(HttpStatusCode.OK, body);
        var config = new AiCompletionConfig(AiProvider.Local, "qwen2.5:3b",
            "http://localhost:11434", null);

        var result = await sut.CompleteAsync(config, "sys", "user", CancellationToken.None);

        result.Should().Be("list formatted");
    }

    [Fact]
    public async Task CompleteAsync_Ollama_strips_trailing_slash_from_endpoint()
    {
        var body = """{"message":{"content":"ok"}}""";
        var (sut, captured) = BuildSut(HttpStatusCode.OK, body);
        var config = new AiCompletionConfig(AiProvider.Local, "qwen2.5:3b",
            "http://localhost:11434/", null);

        await sut.CompleteAsync(config, "sys", "user", CancellationToken.None);

        captured[0].RequestUri!.ToString().Should().Be("http://localhost:11434/api/chat");
    }
}

// ── Test infrastructure ───────────────────────────────────────────────────

internal sealed class FakeHandler(
    HttpStatusCode status,
    string body,
    List<HttpRequestMessage> captured) : HttpMessageHandler
{
    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request, CancellationToken cancellationToken)
    {
        // Clone the request so headers/content are readable after disposal
        var clone = new HttpRequestMessage(request.Method, request.RequestUri);
        foreach (var h in request.Headers) clone.Headers.TryAddWithoutValidation(h.Key, h.Value);
        if (request.Content is not null)
            clone.Content = new StringContent(await request.Content.ReadAsStringAsync(cancellationToken));
        captured.Add(clone);

        return new HttpResponseMessage(status)
        {
            Content = new StringContent(body, System.Text.Encoding.UTF8, "application/json")
        };
    }
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test VoxScript.Tests --filter "FullyQualifiedName~AiCompleterTests" -v minimal`
Expected: FAIL — `AiCompleter` type not found

- [ ] **Step 3: Implement `AiCompleter.cs`**

```csharp
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;

namespace VoxScript.Core.AI;

public sealed class AiCompleter(HttpClient http) : IAiCompleter
{
    public Task<string> CompleteAsync(
        AiCompletionConfig config,
        string systemPrompt,
        string userMessage,
        CancellationToken ct) => config.Provider switch
        {
            AiProvider.OpenAI => CompleteOpenAiAsync(
                config.ApiKey ?? throw new InvalidOperationException("OpenAI API key not set"),
                config.Model, systemPrompt, userMessage, ct),

            AiProvider.Anthropic => CompleteAnthropicAsync(
                config.ApiKey ?? throw new InvalidOperationException("Anthropic API key not set"),
                config.Model, systemPrompt, userMessage, ct),

            _ => CompleteOllamaAsync(config.OllamaEndpoint, config.Model, systemPrompt, userMessage, ct),
        };

    private async Task<string> CompleteOpenAiAsync(string apiKey, string model,
        string systemPrompt, string userMessage, CancellationToken ct)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post,
            "https://api.openai.com/v1/chat/completions");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);
        request.Content = JsonContent.Create(new
        {
            model,
            messages = new[]
            {
                new { role = "system", content = systemPrompt },
                new { role = "user",   content = userMessage  }
            },
            max_tokens = 2048,
        });

        var response = await http.SendAsync(request, ct);
        response.EnsureSuccessStatusCode();

        using var doc = await JsonDocument.ParseAsync(
            await response.Content.ReadAsStreamAsync(ct), cancellationToken: ct);
        return doc.RootElement
            .GetProperty("choices")[0]
            .GetProperty("message")
            .GetProperty("content")
            .GetString() ?? string.Empty;
    }

    private async Task<string> CompleteAnthropicAsync(string apiKey, string model,
        string systemPrompt, string userMessage, CancellationToken ct)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post,
            "https://api.anthropic.com/v1/messages");
        request.Headers.Add("x-api-key", apiKey);
        request.Headers.Add("anthropic-version", "2023-06-01");
        request.Content = JsonContent.Create(new
        {
            model,
            system = systemPrompt,
            messages = new[] { new { role = "user", content = userMessage } },
            max_tokens = 2048,
        });

        var response = await http.SendAsync(request, ct);
        response.EnsureSuccessStatusCode();

        using var doc = await JsonDocument.ParseAsync(
            await response.Content.ReadAsStreamAsync(ct), cancellationToken: ct);
        return doc.RootElement
            .GetProperty("content")[0]
            .GetProperty("text")
            .GetString() ?? string.Empty;
    }

    private async Task<string> CompleteOllamaAsync(string endpoint, string model,
        string systemPrompt, string userMessage, CancellationToken ct)
    {
        var baseUrl = endpoint.TrimEnd('/');
        using var request = new HttpRequestMessage(HttpMethod.Post, $"{baseUrl}/api/chat");
        request.Content = JsonContent.Create(new
        {
            model,
            stream = false,
            messages = new[]
            {
                new { role = "system", content = systemPrompt },
                new { role = "user",   content = userMessage  }
            }
        });

        var response = await http.SendAsync(request, ct);
        response.EnsureSuccessStatusCode();

        using var doc = await JsonDocument.ParseAsync(
            await response.Content.ReadAsStreamAsync(ct), cancellationToken: ct);
        return doc.RootElement
            .GetProperty("message")
            .GetProperty("content")
            .GetString() ?? string.Empty;
    }
}
```

- [ ] **Step 4: Run tests to verify they pass**

Run: `dotnet test VoxScript.Tests --filter "FullyQualifiedName~AiCompleterTests" -v minimal`
Expected: All 8 tests PASS

- [ ] **Step 5: Commit**

```bash
git add VoxScript.Core/AI/AiCompleter.cs VoxScript.Tests/AI/AiCompleterTests.cs
git commit -m "feat: add AiCompleter with OpenAI/Anthropic/Ollama HTTP provider implementations"
```

---

## Task 3: Refactor `AIService` to delegate to `IAiCompleter`

**Files:**
- Modify: `VoxScript.Core/AI/AIService.cs`

The public contract (`CompleteAsync`, `IsConfigured`) is unchanged. This task only swaps the HTTP internals. No new tests needed — existing `AIEnhancementServiceTests` will serve as a smoke check after the build.

- [ ] **Step 1: Rewrite `AIService.cs` to delegate through `IAiCompleter`**

```csharp
using VoxScript.Core.Settings;

namespace VoxScript.Core.AI;

public sealed class AIService
{
    private readonly IAiCompleter _completer;
    private readonly ApiKeyManager _keyManager;
    private readonly AppSettings _settings;

    public bool IsConfigured => _settings.AiProvider switch
    {
        AiProvider.OpenAI    => _keyManager.GetOpenAiKey() is { Length: > 0 },
        AiProvider.Anthropic => _keyManager.GetAnthropicKey() is { Length: > 0 },
        AiProvider.Local     => true,
        _                    => false,
    };

    public AIService(IAiCompleter completer, ApiKeyManager keyManager, AppSettings settings)
    {
        _completer  = completer;
        _keyManager = keyManager;
        _settings   = settings;
    }

    public Task<string> CompleteAsync(string systemPrompt, string userMessage, CancellationToken ct)
    {
        var config = new AiCompletionConfig(
            _settings.AiProvider,
            _settings.AiModelName,
            _settings.OllamaEndpoint,
            _settings.AiProvider switch
            {
                AiProvider.OpenAI    => _keyManager.GetOpenAiKey(),
                AiProvider.Anthropic => _keyManager.GetAnthropicKey(),
                _                    => null,
            });
        return _completer.CompleteAsync(config, systemPrompt, userMessage, ct);
    }
}
```

- [ ] **Step 2: Build solution to confirm no errors**

Run: `dotnet build VoxScript.Core/VoxScript.Core.csproj`
Expected: Build succeeded, 0 errors

- [ ] **Step 3: Commit**

```bash
git add VoxScript.Core/AI/AIService.cs
git commit -m "refactor: AIService delegates HTTP to IAiCompleter, removes inline provider methods"
```

---

## Task 4: `StructuralFormattingPrompt` — system prompt and output validator

**Files:**
- Create: `VoxScript.Core/AI/StructuralFormattingPrompt.cs`
- Test: `VoxScript.Tests/AI/StructuralFormattingPromptTests.cs`

- [ ] **Step 1: Write failing tests**

```csharp
using FluentAssertions;
using VoxScript.Core.AI;

namespace VoxScript.Tests.AI;

public class StructuralFormattingPromptTests
{
    // ── Null / empty / whitespace ──────────────────────────────────────────

    [Fact]
    public void ValidateOutput_returns_null_for_null_result()
    {
        StructuralFormattingPrompt.ValidateOutput(null, "some original text").Should().BeNull();
    }

    [Fact]
    public void ValidateOutput_returns_null_for_empty_result()
    {
        StructuralFormattingPrompt.ValidateOutput("", "some original text").Should().BeNull();
    }

    [Fact]
    public void ValidateOutput_returns_null_for_whitespace_result()
    {
        StructuralFormattingPrompt.ValidateOutput("   ", "some original text").Should().BeNull();
    }

    [Fact]
    public void ValidateOutput_returns_null_when_original_has_no_content_words()
    {
        // original is pure punctuation/numbers → origCount == 0 → return null
        StructuralFormattingPrompt.ValidateOutput("hello world", "123 456 !!! ---").Should().BeNull();
    }

    // ── Ratio boundary: lower bound ────────────────────────────────────────

    [Fact]
    public void ValidateOutput_returns_null_when_ratio_is_below_0_85()
    {
        // original: 20 words. result: 16 words → ratio = 0.80 → reject
        string original = string.Join(" ", Enumerable.Repeat("word", 20));
        string result   = string.Join(" ", Enumerable.Repeat("word", 16));
        StructuralFormattingPrompt.ValidateOutput(result, original).Should().BeNull();
    }

    [Fact]
    public void ValidateOutput_accepts_ratio_at_exactly_0_85()
    {
        // original: 20 words. result: 17 words → ratio = 0.85 → accept
        string original = string.Join(" ", Enumerable.Repeat("word", 20));
        string result   = string.Join(" ", Enumerable.Repeat("word", 17));
        StructuralFormattingPrompt.ValidateOutput(result, original).Should().NotBeNull();
    }

    // ── Ratio boundary: upper bound ────────────────────────────────────────

    [Fact]
    public void ValidateOutput_accepts_ratio_at_exactly_1_15()
    {
        // original: 20 words. result: 23 words → ratio = 1.15 → accept
        string original = string.Join(" ", Enumerable.Repeat("word", 20));
        string result   = string.Join(" ", Enumerable.Repeat("word", 23));
        StructuralFormattingPrompt.ValidateOutput(result, original).Should().NotBeNull();
    }

    [Fact]
    public void ValidateOutput_returns_null_when_ratio_exceeds_1_15()
    {
        // original: 20 words. result: 24 words → ratio = 1.20 → reject
        string original = string.Join(" ", Enumerable.Repeat("word", 20));
        string result   = string.Join(" ", Enumerable.Repeat("word", 24));
        StructuralFormattingPrompt.ValidateOutput(result, original).Should().BeNull();
    }

    // ── List markers are not counted as content words ──────────────────────

    [Fact]
    public void ValidateOutput_excludes_pure_numeric_list_markers_from_count()
    {
        // original: 3 content words. result: same 3 words + "1." "2." "3." markers
        // Without exclusion, result would be 6 "words" → ratio 2.0 → reject
        // With exclusion, result is 3 content words → ratio 1.0 → accept
        const string original = "fix auth logging deployment";
        const string result = "1. fix\n2. auth logging\n3. deployment";
        StructuralFormattingPrompt.ValidateOutput(result, original).Should().NotBeNull();
    }

    [Fact]
    public void ValidateOutput_excludes_dash_tokens_from_count()
    {
        const string original = "alpha beta gamma";
        const string result = "- alpha\n- beta\n- gamma";
        StructuralFormattingPrompt.ValidateOutput(result, original).Should().NotBeNull();
    }

    // ── Result is trimmed ──────────────────────────────────────────────────

    [Fact]
    public void ValidateOutput_trims_result()
    {
        const string original = "hello world";
        const string result   = "  hello world  ";
        StructuralFormattingPrompt.ValidateOutput(result, original).Should().Be("hello world");
    }

    // ── System prompt is non-empty ─────────────────────────────────────────

    [Fact]
    public void System_prompt_is_not_null_or_whitespace()
    {
        StructuralFormattingPrompt.System.Should().NotBeNullOrWhiteSpace();
    }
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test VoxScript.Tests --filter "FullyQualifiedName~StructuralFormattingPromptTests" -v minimal`
Expected: FAIL — `StructuralFormattingPrompt` type not found

- [ ] **Step 3: Implement `StructuralFormattingPrompt.cs`**

```csharp
namespace VoxScript.Core.AI;

public static class StructuralFormattingPrompt
{
    public static readonly string System =
        """
        You are a text structure formatter for voice transcriptions. The text has
        already been transcribed and had basic formatting applied (punctuation,
        numbers converted, etc.).

        Your ONLY job is to fix structural formatting that requires contextual
        understanding:

        1. LIST DETECTION: When the speaker enumerates items (using "first/second/
           third", "1, 2, 3", "one, two, three" as markers), format them as a
           numbered list with each item on its own line, prefixed with "N. ", even
           if separated by long paragraphs of discussion between items.

        2. PARAGRAPH BREAKS: Insert paragraph breaks (blank lines) where the
           speaker shifts topics within a single discussion block. Do NOT break
           within a single thought.

        3. AMBIGUOUS WORDS: Resolve context-dependent words:
           - "first/second/third" as list ordinals → "1./2./3."
           - "first" in prose ("we did this first") → leave as "first"
           - "one" as a number in enumeration → "1"
           - "one" as a pronoun ("one of the things") → leave as "one"

        RULES:
        - Output ONLY the reformatted text. No explanations, no preamble.
        - Do NOT change any words. Do NOT fix grammar. Do NOT rephrase.
        - Do NOT add or remove content.
        - Preserve ALL original words exactly. Only change structure (line breaks,
          numbering format, paragraph grouping).
        - If the text needs no structural changes, return it exactly as-is.
        """;

    /// <summary>
    /// Validates LLM output by comparing content word counts against the original.
    /// Returns null if the output should be rejected; returns the trimmed result if accepted.
    /// A "content word" is any whitespace-delimited token containing at least one letter —
    /// pure numeric/punctuation tokens like "1.", "-", "2)" are excluded so list markers
    /// added by the LLM don't falsely fail the ratio check.
    /// </summary>
    public static string? ValidateOutput(string? result, string original)
    {
        if (string.IsNullOrWhiteSpace(result)) return null;

        int origCount   = CountContentWords(original);
        int resultCount = CountContentWords(result);

        if (origCount == 0) return null;

        double ratio = (double)resultCount / origCount;
        if (ratio < 0.85 || ratio > 1.15) return null;

        return result.Trim();
    }

    private static int CountContentWords(string text) =>
        text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries)
            .Count(t => t.Any(char.IsLetter));
}
```

- [ ] **Step 4: Run tests to verify they pass**

Run: `dotnet test VoxScript.Tests --filter "FullyQualifiedName~StructuralFormattingPromptTests" -v minimal`
Expected: All 11 tests PASS

- [ ] **Step 5: Commit**

```bash
git add VoxScript.Core/AI/StructuralFormattingPrompt.cs VoxScript.Tests/AI/StructuralFormattingPromptTests.cs
git commit -m "feat: add StructuralFormattingPrompt with system prompt and output validator"
```

---

## Task 5: `IStructuralFormattingService` interface and `StructuralFormattingService` implementation

**Files:**
- Create: `VoxScript.Core/AI/IStructuralFormattingService.cs`
- Create: `VoxScript.Core/AI/StructuralFormattingService.cs`
- Test: `VoxScript.Tests/AI/StructuralFormattingServiceTests.cs`

- [ ] **Step 1: Create `IStructuralFormattingService.cs`**

```csharp
namespace VoxScript.Core.AI;

public interface IStructuralFormattingService
{
    bool IsConfigured { get; }
    Task<string?> FormatAsync(string text, CancellationToken ct);
}
```

- [ ] **Step 2: Write failing tests**

```csharp
using FluentAssertions;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using VoxScript.Core.AI;
using VoxScript.Core.Settings;

namespace VoxScript.Tests.AI;

public class StructuralFormattingServiceTests
{
    private static (StructuralFormattingService sut, IAiCompleter completer)
        BuildSut(AiProvider provider = AiProvider.Local,
                 string model = "qwen2.5:3b",
                 string? apiKey = null)
    {
        var completer = Substitute.For<IAiCompleter>();
        var keyStore  = Substitute.For<IApiKeyStore>();
        keyStore.GetKey("Structural.OpenAI").Returns(
            provider == AiProvider.OpenAI ? apiKey : null);
        keyStore.GetKey("Structural.Anthropic").Returns(
            provider == AiProvider.Anthropic ? apiKey : null);

        var keyManager = new ApiKeyManager(keyStore);
        var settings   = new AppSettings(new InMemorySettingsStore());
        settings.StructuralAiProvider  = provider;
        settings.StructuralAiModel     = model;
        settings.StructuralFormattingEnabled = true;

        var sut = new StructuralFormattingService(completer, keyManager, settings);
        return (sut, completer);
    }

    // ── IsConfigured ──────────────────────────────────────────────────────

    [Fact]
    public void IsConfigured_local_is_always_true()
    {
        var (sut, _) = BuildSut(AiProvider.Local);
        sut.IsConfigured.Should().BeTrue();
    }

    [Fact]
    public void IsConfigured_OpenAI_false_when_no_key()
    {
        var (sut, _) = BuildSut(AiProvider.OpenAI, apiKey: null);
        sut.IsConfigured.Should().BeFalse();
    }

    [Fact]
    public void IsConfigured_OpenAI_true_when_key_present()
    {
        var (sut, _) = BuildSut(AiProvider.OpenAI, apiKey: "sk-test-key");
        sut.IsConfigured.Should().BeTrue();
    }

    // ── FormatAsync: early exits ───────────────────────────────────────────

    [Fact]
    public async Task FormatAsync_returns_null_when_not_configured()
    {
        var (sut, completer) = BuildSut(AiProvider.OpenAI, apiKey: null);

        var result = await sut.FormatAsync("some text", CancellationToken.None);

        result.Should().BeNull();
        await completer.DidNotReceive().CompleteAsync(
            Arg.Any<AiCompletionConfig>(), Arg.Any<string>(), Arg.Any<string>(),
            Arg.Any<CancellationToken>());
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public async Task FormatAsync_returns_null_for_empty_input(string? input)
    {
        var (sut, completer) = BuildSut(AiProvider.Local);

        var result = await sut.FormatAsync(input!, CancellationToken.None);

        result.Should().BeNull();
        await completer.DidNotReceive().CompleteAsync(
            Arg.Any<AiCompletionConfig>(), Arg.Any<string>(), Arg.Any<string>(),
            Arg.Any<CancellationToken>());
    }

    // ── FormatAsync: success path ──────────────────────────────────────────

    [Fact]
    public async Task FormatAsync_returns_LLM_output_when_validator_accepts()
    {
        var (sut, completer) = BuildSut(AiProvider.Local);
        const string input    = "first fix auth second improve logging third deployment script";
        const string llmOutput = "1. fix auth\n2. improve logging\n3. deployment script";
        completer.CompleteAsync(Arg.Any<AiCompletionConfig>(), Arg.Any<string>(),
            Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(llmOutput);

        var result = await sut.FormatAsync(input, CancellationToken.None);

        result.Should().Be(llmOutput);
    }

    // ── FormatAsync: validator rejects ────────────────────────────────────

    [Fact]
    public async Task FormatAsync_returns_null_when_validator_rejects_output()
    {
        var (sut, completer) = BuildSut(AiProvider.Local);
        const string input     = "hello world foo bar baz";         // 5 content words
        const string llmOutput = "completely different hallucinated words added added added added added added added"; // >> 1.15 ratio
        completer.CompleteAsync(Arg.Any<AiCompletionConfig>(), Arg.Any<string>(),
            Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(llmOutput);

        var result = await sut.FormatAsync(input, CancellationToken.None);

        result.Should().BeNull();
    }

    // ── FormatAsync: HTTP failure ─────────────────────────────────────────

    [Fact]
    public async Task FormatAsync_returns_null_on_HttpRequestException()
    {
        var (sut, completer) = BuildSut(AiProvider.Local);
        completer.CompleteAsync(Arg.Any<AiCompletionConfig>(), Arg.Any<string>(),
            Arg.Any<string>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(new HttpRequestException("connection refused"));

        var result = await sut.FormatAsync("some text here", CancellationToken.None);

        result.Should().BeNull();
    }

    // ── FormatAsync: external cancellation propagates ────────────────────

    [Fact]
    public async Task FormatAsync_propagates_OperationCanceledException_from_external_ct()
    {
        var (sut, completer) = BuildSut(AiProvider.Local);
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        // The completer throws OCE when the external token is already cancelled
        completer.CompleteAsync(Arg.Any<AiCompletionConfig>(), Arg.Any<string>(),
            Arg.Any<string>(), Arg.Is<CancellationToken>(t => t.IsCancellationRequested))
            .ThrowsAsync(new OperationCanceledException(cts.Token));

        var act = () => sut.FormatAsync("some text here", cts.Token);

        await act.Should().ThrowAsync<OperationCanceledException>();
    }

    // ── FormatAsync: internal timeout returns null ─────────────────────────

    [Fact]
    public async Task FormatAsync_returns_null_on_internal_timeout()
    {
        var (sut, completer) = BuildSut(AiProvider.Local);
        // Simulate timeout by having the completer delay longer than the service timeout.
        // We inject a very short timeout by ... well, we can't without the timeout being
        // injectable.  Instead we verify the contract via a TaskCanceledException that
        // does NOT carry the external token.
        using var externalCts = new CancellationTokenSource();
        using var internalCts = new CancellationTokenSource();
        // internalCts represents the internal 5s timer firing
        completer.CompleteAsync(Arg.Any<AiCompletionConfig>(), Arg.Any<string>(),
            Arg.Any<string>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(new OperationCanceledException(internalCts.Token));

        var result = await sut.FormatAsync("some text here", externalCts.Token);

        result.Should().BeNull();
    }
}

// ── In-memory settings store for tests ───────────────────────────────────

internal sealed class InMemorySettingsStore : ISettingsStore
{
    private readonly Dictionary<string, object?> _data = new();
    public T? Get<T>(string key) => _data.TryGetValue(key, out var v) ? (T?)v : default;
    public void Set<T>(string key, T? value) => _data[key] = value;
}
```

- [ ] **Step 3: Run tests to verify they fail**

Run: `dotnet test VoxScript.Tests --filter "FullyQualifiedName~StructuralFormattingServiceTests" -v minimal`
Expected: FAIL — `StructuralFormattingService` / `AppSettings.StructuralAiProvider` not found

- [ ] **Step 4: Add 4 `Structural*` properties to `AppSettings.cs`**

Insert after the `AutoAddToDictionary` property (before the hotkey properties):

```csharp
public bool StructuralFormattingEnabled
{
    get => _store.Get<bool?>(nameof(StructuralFormattingEnabled)) ?? false;
    set => _store.Set(nameof(StructuralFormattingEnabled), value);
}

public AiProvider StructuralAiProvider
{
    get => _store.Get<AiProvider?>(nameof(StructuralAiProvider)) ?? AiProvider.Local;
    set => _store.Set(nameof(StructuralAiProvider), value);
}

public string StructuralAiModel
{
    get => _store.Get<string>(nameof(StructuralAiModel)) ?? "qwen2.5:3b";
    set => _store.Set(nameof(StructuralAiModel), value);
}

public string StructuralOllamaEndpoint
{
    get => _store.Get<string>(nameof(StructuralOllamaEndpoint)) ?? "http://localhost:11434";
    set => _store.Set(nameof(StructuralOllamaEndpoint), value);
}
```

- [ ] **Step 5: Add 4 credential methods to `ApiKeyManager.cs`**

Append inside the `ApiKeyManager` class body:

```csharp
public void SetStructuralOpenAiKey(string key)     => _store.StoreKey("Structural.OpenAI", key);
public string? GetStructuralOpenAiKey()            => _store.GetKey("Structural.OpenAI");
public void SetStructuralAnthropicKey(string key)  => _store.StoreKey("Structural.Anthropic", key);
public string? GetStructuralAnthropicKey()         => _store.GetKey("Structural.Anthropic");
```

- [ ] **Step 6: Implement `StructuralFormattingService.cs`**

```csharp
using Serilog;
using VoxScript.Core.Settings;

namespace VoxScript.Core.AI;

public sealed class StructuralFormattingService(
    IAiCompleter completer,
    ApiKeyManager keyManager,
    AppSettings settings) : IStructuralFormattingService
{
    private static readonly TimeSpan InternalTimeout = TimeSpan.FromSeconds(5);

    public bool IsConfigured => settings.StructuralAiProvider switch
    {
        AiProvider.OpenAI    => keyManager.GetStructuralOpenAiKey() is { Length: > 0 },
        AiProvider.Anthropic => keyManager.GetStructuralAnthropicKey() is { Length: > 0 },
        AiProvider.Local     => true,
        _                    => false,
    };

    public async Task<string?> FormatAsync(string text, CancellationToken ct)
    {
        if (!IsConfigured) return null;
        if (string.IsNullOrWhiteSpace(text)) return null;

        using var internalCts = new CancellationTokenSource(InternalTimeout);
        using var linked      = CancellationTokenSource.CreateLinkedTokenSource(ct, internalCts.Token);

        try
        {
            var config = new AiCompletionConfig(
                settings.StructuralAiProvider,
                settings.StructuralAiModel,
                settings.StructuralOllamaEndpoint,
                settings.StructuralAiProvider switch
                {
                    AiProvider.OpenAI    => keyManager.GetStructuralOpenAiKey(),
                    AiProvider.Anthropic => keyManager.GetStructuralAnthropicKey(),
                    _                    => null,
                });

            var raw      = await completer.CompleteAsync(config, StructuralFormattingPrompt.System, text, linked.Token);
            var validated = StructuralFormattingPrompt.ValidateOutput(raw, text);

            if (validated is null)
                Log.Debug("Structural formatting: validator rejected LLM output " +
                          "(orig={OrigWords}, result={ResultWords})",
                    text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries)
                        .Count(t => t.Any(char.IsLetter)),
                    raw?.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries)
                        .Count(t => t.Any(char.IsLetter)));

            return validated;
        }
        catch (OperationCanceledException oce)
        {
            // External cancel: propagate so the pipeline knows the user stopped
            if (ct.IsCancellationRequested)
                throw;

            // Internal timeout: fall back silently
            Log.Warning(oce, "Structural formatting timed out after {Timeout}s, using rule-based output",
                InternalTimeout.TotalSeconds);
            return null;
        }
        catch (HttpRequestException hex)
        {
            Log.Debug(hex, "Structural formatting HTTP error (provider not reachable?), using rule-based output");
            return null;
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Structural formatting unexpected error, using rule-based output");
            return null;
        }
    }
}
```

- [ ] **Step 7: Run tests to verify they pass**

Run: `dotnet test VoxScript.Tests --filter "FullyQualifiedName~StructuralFormattingServiceTests" -v minimal`
Expected: All 10 tests PASS

- [ ] **Step 8: Commit**

```bash
git add VoxScript.Core/AI/IStructuralFormattingService.cs \
        VoxScript.Core/AI/StructuralFormattingService.cs \
        VoxScript.Core/Settings/AppSettings.cs \
        VoxScript.Core/Settings/ApiKeyManager.cs \
        VoxScript.Tests/AI/StructuralFormattingServiceTests.cs
git commit -m "feat: add StructuralFormattingService with 5s timeout, validation, and fallback chain"
```

---

## Task 6: Wire step 3b into `TranscriptionPipeline`

**Files:**
- Modify: `VoxScript.Core/Transcription/Core/TranscriptionPipeline.cs`
- Test: `VoxScript.Tests/Transcription/TranscriptionPipelineStructuralTests.cs`

- [ ] **Step 1: Write failing tests**

```csharp
using FluentAssertions;
using NSubstitute;
using VoxScript.Core.AI;
using VoxScript.Core.Dictionary;
using VoxScript.Core.History;
using VoxScript.Core.Persistence;
using VoxScript.Core.PowerMode;
using VoxScript.Core.Settings;
using VoxScript.Core.Transcription.Core;
using VoxScript.Core.Transcription.Processing;
using VoxScript.Tests.AI; // InMemorySettingsStore

namespace VoxScript.Tests.Transcription;

public class TranscriptionPipelineStructuralTests
{
    private static (TranscriptionPipeline pipeline,
                    IStructuralFormattingService structuralSvc,
                    ITranscriptionRepository repo,
                    AppSettings settings)
        BuildPipeline(bool structuralEnabled, bool structuralConfigured)
    {
        var structuralSvc = Substitute.For<IStructuralFormattingService>();
        structuralSvc.IsConfigured.Returns(structuralConfigured);

        var aiEnhancement = Substitute.For<IAIEnhancementService>();
        aiEnhancement.IsConfigured.Returns(false);

        var repo = Substitute.For<ITranscriptionRepository>();
        var wordReplacementRepo = Substitute.For<IWordReplacementRepository>();
        wordReplacementRepo.GetAllAsync(Arg.Any<CancellationToken>()).Returns([]);

        var vocabRepo      = Substitute.For<IVocabularyRepository>();
        var correctionRepo = Substitute.For<ICorrectionRepository>();
        var autoVocab      = Substitute.For<IAutoVocabularyService>();
        var powerMode      = Substitute.For<PowerModeSessionManager>(
            Substitute.For<IPowerModeRepository>(),
            Substitute.For<IActiveWindowService>());

        var settingsStore = new InMemorySettingsStore();
        var settings      = new AppSettings(settingsStore);
        settings.SmartFormattingEnabled      = false; // isolate structural step
        settings.StructuralFormattingEnabled = structuralEnabled;

        var pipeline = new TranscriptionPipeline(
            new TranscriptionOutputFilter(),
            new SmartTextFormatter(),
            new WordReplacementService(wordReplacementRepo, vocabRepo, correctionRepo),
            aiEnhancement,
            repo,
            powerMode,
            autoVocab,
            settings,
            structuralSvc);  // new last parameter

        return (pipeline, structuralSvc, repo, settings);
    }

    private static ITranscriptionSession FakeSession(string text)
    {
        var session = Substitute.For<ITranscriptionSession>();
        session.TranscribeAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(text);
        session.Model.Returns(new TranscriptionModel("base.en", "base.en", 0));
        return session;
    }

    // ── Step 3b is skipped when setting off ───────────────────────────────

    [Fact]
    public async Task Pipeline_skips_structural_when_setting_disabled()
    {
        var (pipeline, structuralSvc, _, _) = BuildPipeline(
            structuralEnabled: false, structuralConfigured: true);
        var session = FakeSession("some transcribed text here");

        await pipeline.RunAsync(session, "file.wav", 2.0, false, CancellationToken.None);

        await structuralSvc.DidNotReceive()
            .FormatAsync(Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    // ── Step 3b is skipped when service not configured ────────────────────

    [Fact]
    public async Task Pipeline_skips_structural_when_not_configured()
    {
        var (pipeline, structuralSvc, _, _) = BuildPipeline(
            structuralEnabled: true, structuralConfigured: false);
        var session = FakeSession("some transcribed text here");

        await pipeline.RunAsync(session, "file.wav", 2.0, false, CancellationToken.None);

        await structuralSvc.DidNotReceive()
            .FormatAsync(Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    // ── Step 3b runs when both enabled and configured ─────────────────────

    [Fact]
    public async Task Pipeline_calls_structural_when_enabled_and_configured()
    {
        var (pipeline, structuralSvc, _, _) = BuildPipeline(
            structuralEnabled: true, structuralConfigured: true);
        structuralSvc.FormatAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns((string?)null);
        var session = FakeSession("some transcribed text here");

        await pipeline.RunAsync(session, "file.wav", 2.0, false, CancellationToken.None);

        await structuralSvc.Received(1)
            .FormatAsync(Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    // ── Falls back to SmartFormatter output when service returns null ─────

    [Fact]
    public async Task Pipeline_uses_rule_based_output_when_structural_returns_null()
    {
        var (pipeline, structuralSvc, repo, _) = BuildPipeline(
            structuralEnabled: true, structuralConfigured: true);
        structuralSvc.FormatAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns((string?)null);
        var session = FakeSession("hello world");

        var result = await pipeline.RunAsync(session, "file.wav", 1.0, false, CancellationToken.None);

        // Structural returned null so final text == SmartFormatter output
        result.Should().Be("Hello world");
    }

    // ── Structured text flows through to final output ─────────────────────

    [Fact]
    public async Task Pipeline_uses_structured_output_when_structural_returns_text()
    {
        var (pipeline, structuralSvc, repo, _) = BuildPipeline(
            structuralEnabled: true, structuralConfigured: true);
        const string structured = "1. Fix auth\n2. Improve logging\n3. Fix deployment";
        structuralSvc.FormatAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(structured);
        var session = FakeSession("first fix auth second improve logging third fix deployment");

        var result = await pipeline.RunAsync(session, "file.wav", 3.0, false, CancellationToken.None);

        result.Should().Be(structured);
    }

    // ── Structured text is persisted in Text column ───────────────────────

    [Fact]
    public async Task Pipeline_persists_structured_text_in_Text_column()
    {
        var (pipeline, structuralSvc, repo, _) = BuildPipeline(
            structuralEnabled: true, structuralConfigured: true);
        const string structured = "1. Fix auth\n2. Improve logging";
        structuralSvc.FormatAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(structured);
        var session = FakeSession("first fix auth second improve logging");

        await pipeline.RunAsync(session, "file.wav", 2.0, false, CancellationToken.None);

        await repo.Received(1).AddAsync(
            Arg.Is<TranscriptionRecord>(r => r.Text == structured),
            Arg.Any<CancellationToken>());
    }
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test VoxScript.Tests --filter "FullyQualifiedName~TranscriptionPipelineStructuralTests" -v minimal`
Expected: FAIL — constructor parameter count mismatch

- [ ] **Step 3: Add `IStructuralFormattingService` to `TranscriptionPipeline`**

Add the field, constructor parameter (last position), and step 3b. Replace the full constructor and `RunAsync` method:

In the field declarations, add:
```csharp
private readonly IStructuralFormattingService _structuralFormatting;
```

In the constructor signature, add as last parameter:
```csharp
IStructuralFormattingService structuralFormatting
```

In the constructor body, add:
```csharp
_structuralFormatting = structuralFormatting;
```

In `RunAsync`, insert the following block immediately after the step 3 (Format) try-catch and before step 4 (Word replacement):

```csharp
// 3b. LLM-based structural formatting (lists, paragraphs, ordinals)
if (_settings.StructuralFormattingEnabled && _structuralFormatting.IsConfigured)
{
    try
    {
        var structured = await _structuralFormatting.FormatAsync(formatted, ct);
        if (structured is not null)
            formatted = structured;
    }
    catch (Exception ex)
    {
        Log.Warning(ex, "Structural formatting failed, using rule-based output");
    }
}
```

Also update the persist step to use the post-structural `replaced` variable for the `Text` column. The existing code already writes `replaced` to `Text`, and `replaced` is derived from `formatted` (which now holds the structured text) — so no change needed there. Confirm the persist block reads `Text = replaced` (it does in the current code).

- [ ] **Step 4: Run tests to verify they pass**

Run: `dotnet test VoxScript.Tests --filter "FullyQualifiedName~TranscriptionPipelineStructuralTests" -v minimal`
Expected: All 6 tests PASS

- [ ] **Step 5: Run the full test suite to confirm no regressions**

Run: `dotnet test VoxScript.Tests -v minimal`
Expected: All tests PASS

- [ ] **Step 6: Commit**

```bash
git add VoxScript.Core/Transcription/Core/TranscriptionPipeline.cs \
        VoxScript.Tests/Transcription/TranscriptionPipelineStructuralTests.cs
git commit -m "feat: add structural formatting step 3b to TranscriptionPipeline"
```

---

## Task 7: Update `AppBootstrapper` DI registrations

**Files:**
- Modify: `VoxScript/Infrastructure/AppBootstrapper.cs`

- [ ] **Step 1: Register `IAiCompleter` and `IStructuralFormattingService`; update `AIService` registration**

In `AppBootstrapper.Build()`, after the line `services.AddSingleton<HttpClient>();`:

Replace:
```csharp
services.AddSingleton<AIService>();
```

With:
```csharp
services.AddSingleton<IAiCompleter, AiCompleter>();
services.AddSingleton<AIService>();
services.AddSingleton<IStructuralFormattingService, StructuralFormattingService>();
```

`AIService` now takes `IAiCompleter` (resolved automatically), `ApiKeyManager`, and `AppSettings` — all already registered.
`StructuralFormattingService` takes `IAiCompleter`, `ApiKeyManager`, and `AppSettings` — all registered.

- [ ] **Step 2: Build the full solution**

Run: `dotnet build VoxScript.slnx`
Expected: Build succeeded, 0 errors, 0 warnings (or only pre-existing warnings)

- [ ] **Step 3: Commit**

```bash
git add VoxScript/Infrastructure/AppBootstrapper.cs
git commit -m "feat: register IAiCompleter and IStructuralFormattingService in DI container"
```

---

## Task 8: Settings ViewModel — structural formatting properties

**Files:**
- Modify: `VoxScript/ViewModels/SettingsViewModel.cs`

- [ ] **Step 1: Add observable properties for the 4 structural settings**

Add the following region after the `// ── AI Enhancement ──────────────────────────────────────────` region:

```csharp
// ── LLM Structural Formatting ──────────────────────────────────

[ObservableProperty]
public partial bool StructuralFormattingEnabled { get; set; }
partial void OnStructuralFormattingEnabledChanged(bool value) =>
    _settings.StructuralFormattingEnabled = value;

public IReadOnlyList<string> StructuralAiProviders { get; } = ["Local (Ollama)", "OpenAI", "Anthropic"];

[ObservableProperty]
public partial int SelectedStructuralAiProviderIndex { get; set; }

partial void OnSelectedStructuralAiProviderIndexChanged(int value)
{
    var newProvider = value switch
    {
        1 => AiProvider.OpenAI,
        2 => AiProvider.Anthropic,
        _ => AiProvider.Local
    };

    // Swap to provider default only if the model is still the previous default
    var previousDefault = _settings.StructuralAiProvider switch
    {
        AiProvider.OpenAI    => "gpt-4o-mini",
        AiProvider.Anthropic => "claude-haiku-4-5-20251001",
        _                    => "qwen2.5:3b",
    };
    var newDefault = newProvider switch
    {
        AiProvider.OpenAI    => "gpt-4o-mini",
        AiProvider.Anthropic => "claude-haiku-4-5-20251001",
        _                    => "qwen2.5:3b",
    };

    if (_settings.StructuralAiModel == previousDefault)
    {
        _settings.StructuralAiModel = newDefault;
        StructuralAiModel = newDefault;
    }

    _settings.StructuralAiProvider = newProvider;
    OnPropertyChanged(nameof(IsStructuralLocalProvider));
    OnPropertyChanged(nameof(IsStructuralCloudProvider));
    UpdateStructuralStatus();
}

public bool IsStructuralLocalProvider  => SelectedStructuralAiProviderIndex == 0;
public bool IsStructuralCloudProvider  => SelectedStructuralAiProviderIndex > 0;

[ObservableProperty]
public partial string StructuralAiModel { get; set; } = "";
partial void OnStructuralAiModelChanged(string value) => _settings.StructuralAiModel = value;

[ObservableProperty]
public partial string StructuralOllamaEndpoint { get; set; } = "";
partial void OnStructuralOllamaEndpointChanged(string value) => _settings.StructuralOllamaEndpoint = value;

[ObservableProperty]
public partial string StructuralApiKeyDisplay { get; set; } = "";

[ObservableProperty]
public partial string StructuralStatusText { get; set; } = "";

public void SaveStructuralApiKey(string key)
{
    if (string.IsNullOrWhiteSpace(key)) return;
    if (_settings.StructuralAiProvider == AiProvider.OpenAI)
        _keyManager.SetStructuralOpenAiKey(key);
    else if (_settings.StructuralAiProvider == AiProvider.Anthropic)
        _keyManager.SetStructuralAnthropicKey(key);
    UpdateStructuralApiKeyDisplay();
    UpdateStructuralStatus();
}

public void ClearStructuralApiKey()
{
    if (_settings.StructuralAiProvider == AiProvider.OpenAI)
        _keyManager.SetStructuralOpenAiKey("");
    else if (_settings.StructuralAiProvider == AiProvider.Anthropic)
        _keyManager.SetStructuralAnthropicKey("");
    UpdateStructuralApiKeyDisplay();
    UpdateStructuralStatus();
}

private void UpdateStructuralApiKeyDisplay()
{
    var key = _settings.StructuralAiProvider switch
    {
        AiProvider.OpenAI    => _keyManager.GetStructuralOpenAiKey(),
        AiProvider.Anthropic => _keyManager.GetStructuralAnthropicKey(),
        _                    => null,
    };
    StructuralApiKeyDisplay = key is { Length: > 8 }
        ? $"{key[..4]}...{key[^4..]}"
        : key is { Length: > 0 } ? "****" : "";
}

private void UpdateStructuralStatus()
{
    if (!StructuralFormattingEnabled)
    {
        StructuralStatusText = "";
        return;
    }
    StructuralStatusText = _settings.StructuralAiProvider switch
    {
        AiProvider.OpenAI    => _keyManager.GetStructuralOpenAiKey() is { Length: > 0 }
            ? "Configured" : "Not configured — enter API key",
        AiProvider.Anthropic => _keyManager.GetStructuralAnthropicKey() is { Length: > 0 }
            ? "Configured" : "Not configured — enter API key",
        AiProvider.Local     => "Using Ollama at " + _settings.StructuralOllamaEndpoint,
        _                    => "",
    };
}
```

- [ ] **Step 2: Load structural settings in `LoadSettings()`**

Append to the end of `LoadSettings()` before the closing brace:

```csharp
// LLM Structural Formatting
StructuralFormattingEnabled = _settings.StructuralFormattingEnabled;
SelectedStructuralAiProviderIndex = _settings.StructuralAiProvider switch
{
    AiProvider.OpenAI    => 1,
    AiProvider.Anthropic => 2,
    _                    => 0,
};
StructuralAiModel      = _settings.StructuralAiModel;
StructuralOllamaEndpoint = _settings.StructuralOllamaEndpoint;
UpdateStructuralApiKeyDisplay();
UpdateStructuralStatus();
```

- [ ] **Step 3: Build `VoxScript` project to confirm no errors**

Run: `dotnet build VoxScript/VoxScript.csproj`
Expected: Build succeeded, 0 errors

- [ ] **Step 4: Commit**

```bash
git add VoxScript/ViewModels/SettingsViewModel.cs
git commit -m "feat: add structural formatting observable properties to SettingsViewModel"
```

---

## Task 9: Settings UI — LLM FORMATTING card in XAML

**Files:**
- Modify: `VoxScript/Views/SettingsPage.xaml`
- Modify: `VoxScript/Views/SettingsPage.xaml.cs`

- [ ] **Step 1: Add the LLM FORMATTING card to `SettingsPage.xaml`**

Insert the following `<Border>` block in `SettingsPage.xaml` immediately after the closing `</Border>` of the AI Enhancement card (line 354) and before the `<!-- Card 3: App -->` comment:

```xml
<!-- Card: LLM Formatting -->
<Border Background="{StaticResource BrandCardBrush}"
        CornerRadius="16" Padding="28"
        BorderBrush="{StaticResource BrandPrimaryLightBrush}"
        BorderThickness="1">
    <StackPanel Spacing="18">
        <StackPanel Orientation="Horizontal" Spacing="8">
            <FontIcon Glyph="&#xE8FD;" FontSize="20"
                      Foreground="{StaticResource BrandPrimaryBrush}" />
            <TextBlock Text="LLM FORMATTING" FontSize="12" FontWeight="Medium"
                       CharacterSpacing="120"
                       Foreground="{StaticResource BrandMutedBrush}"
                       VerticalAlignment="Center" />
        </StackPanel>

        <!-- Enable toggle -->
        <Grid ColumnSpacing="32">
            <Grid.ColumnDefinitions>
                <ColumnDefinition Width="*" />
                <ColumnDefinition Width="Auto" />
            </Grid.ColumnDefinitions>
            <StackPanel VerticalAlignment="Center">
                <TextBlock Text="Enable LLM formatting"
                           FontSize="15" Foreground="{StaticResource BrandForegroundBrush}" />
                <TextBlock Text="Use AI to detect lists and paragraph structure across long transcriptions"
                           FontSize="13" Foreground="{StaticResource BrandMutedBrush}" />
            </StackPanel>
            <ToggleSwitch Grid.Column="1" MinWidth="0"
                          OnContent="" OffContent=""
                          IsOn="{x:Bind ViewModel.StructuralFormattingEnabled, Mode=TwoWay}"
                          VerticalAlignment="Center" />
        </Grid>

        <!-- Provider -->
        <Grid ColumnSpacing="32">
            <Grid.ColumnDefinitions>
                <ColumnDefinition Width="*" />
                <ColumnDefinition Width="Auto" />
            </Grid.ColumnDefinitions>
            <StackPanel VerticalAlignment="Center">
                <TextBlock Text="Provider"
                           FontSize="15" Foreground="{StaticResource BrandForegroundBrush}" />
                <TextBlock Text="{x:Bind ViewModel.StructuralStatusText, Mode=OneWay}"
                           FontSize="13" Foreground="{StaticResource BrandMutedBrush}" />
            </StackPanel>
            <ComboBox Grid.Column="1"
                      ItemsSource="{x:Bind ViewModel.StructuralAiProviders}"
                      SelectedIndex="{x:Bind ViewModel.SelectedStructuralAiProviderIndex, Mode=TwoWay}"
                      Width="200"
                      VerticalAlignment="Center" />
        </Grid>

        <!-- Model name -->
        <Grid ColumnSpacing="32">
            <Grid.ColumnDefinitions>
                <ColumnDefinition Width="*" />
                <ColumnDefinition Width="Auto" />
            </Grid.ColumnDefinitions>
            <StackPanel VerticalAlignment="Center">
                <TextBlock Text="Model"
                           FontSize="15" Foreground="{StaticResource BrandForegroundBrush}" />
                <TextBlock Text="AI model name for structural formatting"
                           FontSize="13" Foreground="{StaticResource BrandMutedBrush}" />
            </StackPanel>
            <TextBox Grid.Column="1"
                     Text="{x:Bind ViewModel.StructuralAiModel, Mode=TwoWay, UpdateSourceTrigger=PropertyChanged}"
                     Width="200" FontSize="13"
                     VerticalAlignment="Center" />
        </Grid>

        <!-- Ollama endpoint (local only) -->
        <Grid ColumnSpacing="32"
              Visibility="{x:Bind ViewModel.IsStructuralLocalProvider, Mode=OneWay}">
            <Grid.ColumnDefinitions>
                <ColumnDefinition Width="*" />
                <ColumnDefinition Width="Auto" />
            </Grid.ColumnDefinitions>
            <StackPanel VerticalAlignment="Center">
                <TextBlock Text="Ollama endpoint"
                           FontSize="15" Foreground="{StaticResource BrandForegroundBrush}" />
                <TextBlock Text="Local Ollama server URL"
                           FontSize="13" Foreground="{StaticResource BrandMutedBrush}" />
            </StackPanel>
            <TextBox Grid.Column="1"
                     Text="{x:Bind ViewModel.StructuralOllamaEndpoint, Mode=TwoWay, UpdateSourceTrigger=PropertyChanged}"
                     Width="200" FontSize="13"
                     VerticalAlignment="Center" />
        </Grid>

        <!-- API key (cloud only) -->
        <Grid ColumnSpacing="32"
              Visibility="{x:Bind ViewModel.IsStructuralCloudProvider, Mode=OneWay}">
            <Grid.ColumnDefinitions>
                <ColumnDefinition Width="*" />
                <ColumnDefinition Width="Auto" />
            </Grid.ColumnDefinitions>
            <StackPanel VerticalAlignment="Center">
                <TextBlock Text="API key"
                           FontSize="15" Foreground="{StaticResource BrandForegroundBrush}" />
                <TextBlock Text="{x:Bind ViewModel.StructuralApiKeyDisplay, Mode=OneWay}"
                           FontSize="13" Foreground="{StaticResource BrandMutedBrush}" />
            </StackPanel>
            <StackPanel Grid.Column="1" Orientation="Horizontal" Spacing="8">
                <Button Content="Set key"
                        Click="SetStructuralApiKeyButton_Click"
                        Background="{StaticResource BrandBackgroundBrush}"
                        Foreground="{StaticResource BrandForegroundBrush}"
                        FontSize="13" CornerRadius="8" Padding="12,8"
                        BorderBrush="{StaticResource BrandPrimaryLightBrush}"
                        BorderThickness="1" />
                <Button Content="Clear"
                        Click="ClearStructuralApiKeyButton_Click"
                        Background="Transparent"
                        Foreground="{StaticResource BrandMutedBrush}"
                        FontSize="13" CornerRadius="8" Padding="8,8" />
            </StackPanel>
        </Grid>
    </StackPanel>
</Border>
```

- [ ] **Step 2: Add click handlers to `SettingsPage.xaml.cs`**

Insert after `ClearApiKeyButton_Click`:

```csharp
private async void SetStructuralApiKeyButton_Click(object sender, RoutedEventArgs e)
{
    var keyBox = new PasswordBox
    {
        PlaceholderText = "Enter API key",
        Width = 400,
    };
    var dialog = new ContentDialog
    {
        Title = "Set Structural Formatting API Key",
        Content = keyBox,
        PrimaryButtonText = "Save",
        CloseButtonText = "Cancel",
        DefaultButton = ContentDialogButton.Primary,
        XamlRoot = this.XamlRoot,
    };
    if (await dialog.ShowAsync() == ContentDialogResult.Primary)
    {
        ViewModel.SaveStructuralApiKey(keyBox.Password);
    }
}

private void ClearStructuralApiKeyButton_Click(object sender, RoutedEventArgs e)
{
    ViewModel.ClearStructuralApiKey();
}
```

- [ ] **Step 3: Build the full solution**

Run: `dotnet build VoxScript.slnx`
Expected: Build succeeded, 0 errors

- [ ] **Step 4: Commit**

```bash
git add VoxScript/Views/SettingsPage.xaml VoxScript/Views/SettingsPage.xaml.cs
git commit -m "feat: add LLM FORMATTING settings card with provider/model/key controls"
```

---

## Task 10: Final validation — full build and test suite

- [ ] **Step 1: Run all tests**

Run: `dotnet test VoxScript.Tests -v normal`
Expected: All tests PASS, no failures or skips

- [ ] **Step 2: Build release configuration**

Run: `dotnet build VoxScript.slnx -c Release`
Expected: Build succeeded, 0 errors

- [ ] **Step 3: Manual smoke test (local Ollama)**

1. Launch the app (`dotnet run --project VoxScript`).
2. Open Settings > LLM FORMATTING card. Toggle Enable ON. Provider = Local (Ollama). Model = `qwen2.5:3b`. Endpoint = `http://localhost:11434`.
3. Start Ollama: `ollama run qwen2.5:3b`.
4. Dictate: "There are three things I want to cover. First, we need to fix the auth bug. Then we should improve logging — that's been broken for weeks. And finally, the deployment script needs work."
5. Verify output is formatted as a numbered list with each item on its own line.
6. Open History — confirm the `Text` column in DB contains the structured version (list format, not prose).
7. Toggle LLM FORMATTING off. Dictate the same sentence. Confirm prose paragraph output (SmartFormatter only).
8. Switch provider to OpenAI, set a valid API key, repeat step 4 — verify same numbered list output.

- [ ] **Step 4: Create PR**

```bash
git push origin feature/smart-formatting
# Open PR: feature/smart-formatting → main
# Title: "feat: LLM-based structural formatting (lists, paragraphs, ordinal disambiguation)"
```
