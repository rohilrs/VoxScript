# Structural LLM Formatting — Design Spec

**Date:** 2026-04-17
**Supersedes:** `2026-04-07-llm-formatting.md` (plan), `2026-04-08-llm-formatting-design.md` (spec)

## Overview

Add an optional LLM pass to the transcription pipeline that handles **structural** formatting: numbered lists spanning multiple paragraphs, semantic paragraph breaks, and ordinal disambiguation ("first" as list marker vs "we did this first"). Sits between the rule-based `SmartTextFormatter` and `WordReplacement`. Strict word preservation enforced via output validation. Supports both local (Ollama) and cloud (OpenAI, Anthropic) providers, with provider/model/credentials independent of AI Enhancement.

Grammar, tone, and style remain the responsibility of the existing AI Enhancement feature. This service does **not** rewrite content.

## Architecture

### `IAiCompleter` abstraction

The provider-switching HTTP code is extracted from `AIService` into a new low-level component shared by both AI features.

```
IAiCompleter (interface)
    └─ AiCompleter (concrete)
        ├─ CompleteOpenAiAsync()
        ├─ CompleteAnthropicAsync()
        └─ CompleteOllamaAsync()
```

Single method:

```csharp
public interface IAiCompleter
{
    Task<string> CompleteAsync(
        AiCompletionConfig config,
        string systemPrompt,
        string userMessage,
        CancellationToken ct);
}

public sealed record AiCompletionConfig(
    AiProvider Provider,
    string Model,
    string OllamaEndpoint,
    string? ApiKey);
```

`AIService` is refactored to delegate to `IAiCompleter` while keeping its public contract identical (same `CompleteAsync` signature, same `IsConfigured` semantics, no settings change). It builds an `AiCompletionConfig` from `AppSettings.AiProvider/AiModelName/OllamaEndpoint` and `ApiKeyManager.GetOpenAiKey/GetAnthropicKey`.

`StructuralFormattingService` is the new consumer. It builds its config from the new `Structural*` settings + new credential entries, calls `IAiCompleter`, validates the response, and returns `string?` (null = use rule-based output).

Tests mock `IAiCompleter`. No HTTP plumbing in tests for either feature.

### Pipeline position

Current:
```
Filter → SmartFormatter → WordReplacement → AutoVocab → AIEnhancement → Save
```

New (step 3b inserted):
```
Filter → SmartFormatter → [LLM Structural] → WordReplacement → AutoVocab → AIEnhancement → Save
```

Rationale: structural formatting works on natural-language flow; word replacement is small string substitutions that don't affect structure. Putting structural first means it never has to reason about user vocab. AI Enhancement still runs last and sees the structurally-formatted text.

### Separation of concerns

| Aspect | SmartTextFormatter | LLM Structural | AI Enhancement |
|---|---|---|---|
| Purpose | Token-level: punctuation, numbers, dates, URLs, paragraph gaps from timing | Structural: lists across paragraphs, semantic paragraph breaks, ordinal disambiguation | Semantic: tone, style, grammar, fillers |
| Provider | None (rules) | Local OR cloud (independent config) | Local OR cloud (independent config) |
| Word changes? | No | No (validated) | Yes |
| Trigger | Always (when enabled) | Always (when enabled + configured) | Only when Power Mode matches active app |
| Default | ON | OFF | OFF |
| Latency | µs | ~1s local / ~1–3s cloud | ~1–3s |

## File Layout

### New files (Core)
- `VoxScript.Core/AI/IAiCompleter.cs`
- `VoxScript.Core/AI/AiCompleter.cs`
- `VoxScript.Core/AI/AiCompletionConfig.cs`
- `VoxScript.Core/AI/IStructuralFormattingService.cs`
- `VoxScript.Core/AI/StructuralFormattingService.cs`
- `VoxScript.Core/AI/StructuralFormattingPrompt.cs`

### Modified (Core)
- `VoxScript.Core/AI/AIService.cs` — refactor to delegate to `IAiCompleter`; constructor takes `IAiCompleter` instead of `HttpClient`. Public `CompleteAsync` and `IsConfigured` unchanged.
- `VoxScript.Core/Settings/AppSettings.cs` — add 4 properties (see below).
- `VoxScript.Core/Settings/ApiKeyManager.cs` — add `GetStructuralOpenAiKey` / `SetStructuralOpenAiKey` / `GetStructuralAnthropicKey` / `SetStructuralAnthropicKey`. Stored under separate keys (`"Structural.OpenAI"`, `"Structural.Anthropic"`) so they're independently rotatable from the AI Enhancement keys.
- `VoxScript.Core/Transcription/Core/TranscriptionPipeline.cs` — add step 3b after `SmartFormatter`, before `WordReplacement`; constructor gains `IStructuralFormattingService` parameter (last position).

### Modified (UI)
- `VoxScript/Infrastructure/AppBootstrapper.cs` — register `IAiCompleter` → `AiCompleter`, `IStructuralFormattingService` → `StructuralFormattingService`. Update `AIService` registration if HttpClient was injected directly.
- `VoxScript/ViewModels/SettingsViewModel.cs` — add observable properties for the 4 new settings + API key Set/Clear handlers for the new card.
- `VoxScript/Views/SettingsPage.xaml` — new "LLM FORMATTING" card inserted between AI Enhancement and APP. Mirror of AI Enhancement card structure.
- `VoxScript/Views/SettingsPage.xaml.cs` — `SetStructuralApiKeyButton_Click` / `ClearStructuralApiKeyButton_Click`.

### New tests
- `VoxScript.Tests/AI/AiCompleterTests.cs`
- `VoxScript.Tests/AI/StructuralFormattingPromptTests.cs`
- `VoxScript.Tests/AI/StructuralFormattingServiceTests.cs`
- `VoxScript.Tests/Transcription/TranscriptionPipelineTests.cs`

## Settings

### `AppSettings` additions

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

### Credential Manager

Parallel to existing AI Enhancement keys, stored under distinct `IApiKeyStore` keys so they're independently rotatable.

| AI Enhancement | Structural |
|---|---|
| `"OpenAI"` | `"Structural.OpenAI"` |
| `"Anthropic"` | `"Structural.Anthropic"` |

## UI

### New "LLM FORMATTING" card

Inserted in `SettingsPage.xaml` immediately after the existing "AI Enhancement" card, before "APP". Same shape as AI Enhancement card so the pattern is familiar.

| Row | Control | Binding | Description |
|---|---|---|---|
| Header | Icon (`&#xE8FD;` list-style glyph) + "LLM FORMATTING" | — | — |
| Enable | `ToggleSwitch` | `StructuralFormattingEnabled` | "Use AI to detect lists and paragraph structure across long transcriptions" |
| Provider | `ComboBox` (Local / OpenAI / Anthropic) | `StructuralAiProvider` | Status text mirrors AI Enhancement's `AiStatusText` pattern |
| Model | `TextBox` | `StructuralAiModel` | "AI model name for structural formatting" |
| Ollama endpoint | `TextBox` (visible when `Provider == Local`) | `StructuralOllamaEndpoint` | "Local Ollama server URL" |
| API key | Set / Clear buttons (visible when `Provider == OpenAI` or `Anthropic`) | Credential Manager via `ApiKeyManager` | Display masked key status |

### Smart per-provider model defaults

When the user changes provider in `SettingsViewModel.OnStructuralAiProviderChanged`, **only if the model field equals the previous default**, swap to the new default:

| Provider | Default model |
|---|---|
| Local | `qwen2.5:3b` |
| OpenAI | `gpt-4o-mini` |
| Anthropic | `claude-haiku-4-5-20251001` |

If the user has typed a custom model name, leave it alone. This prevents the common footgun of pointing OpenAI at `qwen2.5:3b` after switching providers.

## Pipeline Integration

### `TranscriptionPipeline.RunAsync` — new step 3b

Inserted between SmartFormatter (step 3) and WordReplacement (step 4):

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

Pattern matches existing AIEnhancement step. Service swallows expected failures; the catch is a safety net for unexpected bugs.

### Storage semantics

| Column | Contents |
|---|---|
| `TranscriptionRecord.Text` | Final pipeline output **including** structural formatting (if it ran). Canonical user-facing text. |
| `EnhancementRecord.EnhancedText` | Only AI Enhancement's content rewrite. Unchanged. |

If both structural and enhancement run, enhancement sees the structured text and stores its rewrite in `EnhancedText`.

### Cancellation

External `ct` propagates down. Service additionally enforces a 5-second internal timeout via `CancellationTokenSource.CreateLinkedTokenSource(ct, internalTimeoutCts.Token)`. After the await, if `ct.IsCancellationRequested` is true, re-throw `OperationCanceledException` (user stopped); otherwise treat as internal timeout and return null (silently fall back).

## Service Behavior

### Fixed system prompt

Stored in `StructuralFormattingPrompt.System`. Not user-configurable.

```
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
```

### Output validator

```csharp
public static string? ValidateOutput(string? result, string original)
{
    if (string.IsNullOrWhiteSpace(result)) return null;

    int origCount = CountContentWords(original);
    int resultCount = CountContentWords(result);

    if (origCount == 0) return null;

    double ratio = (double)resultCount / origCount;
    if (ratio < 0.85 || ratio > 1.15) return null;

    return result.Trim();
}

private static int CountContentWords(string text) =>
    text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries)
        .Count(t => t.Any(char.IsLetter));
```

A "content word" is any whitespace-delimited token containing at least one letter. Pure-numeric/punctuation tokens like `"1."`, `"-"`, `"2)"` are excluded so list markers added by the LLM don't inflate the count and falsely fail the ratio check on short inputs.

The 0.85–1.15 band absorbs minor LLM behavior (splitting run-on sentences, adding "Item:") while catching hallucinated content.

### Error handling / fallback chain

`FormatAsync` returns `string?`. `null` always means "use rule-based output."

| Condition | Handling | Log level |
|---|---|---|
| Not configured (provider missing key) | Return null immediately, no HTTP | none |
| Empty/whitespace input | Return null immediately | none |
| External `ct` cancelled (user stopped) | Re-throw `OperationCanceledException` | none |
| Internal 5s timeout | Catch `OperationCanceledException` from linked CTS, return null | Warning |
| Ollama connection refused | Return null | Debug (expected when Ollama not running) |
| Cloud 401/403 | Return null | Warning |
| Cloud 404 (model not found) | Return null | Warning |
| Other HTTP error | Return null | Warning |
| Validation rejected (ratio out of range) | Return null with both word counts | Debug |
| Unexpected exception | Return null with stack | Warning |

### No retries

A single failed structural pass falls back to rule-based output. That's already a fine outcome — no need to delay paste with retries.

## Testing Strategy

| Layer | Test class | What it verifies | Mocks |
|---|---|---|---|
| HTTP/provider | `AiCompleterTests` | Each provider builds correct URL, headers, body shape; parses response; throws on non-success | `HttpMessageHandler` (record requests, return canned responses) |
| Validator (pure) | `StructuralFormattingPromptTests` | Word-count ratio at boundaries: 0.84 rejected, 0.85 accepted, 1.15 accepted, 1.16 rejected; empty rejected; pure-numeric tokens excluded; list-marker tolerance | none |
| Service | `StructuralFormattingServiceTests` | Returns null when not configured; null on empty input; returns LLM output when valid; null when validator rejects; null on `HttpRequestException`; null on internal timeout; **propagates** `OperationCanceledException` when external token cancels | `IAiCompleter` |
| Pipeline | `TranscriptionPipelineTests` | Step 3b skipped when setting off; skipped when service not configured; runs when both true; falls back to SmartFormatter output when service returns null; output stored in `Text` column | `IStructuralFormattingService`, repos |
| AIService refactor smoke | (extend existing or add minimal) | `AIService.CompleteAsync` delegates the right config to `IAiCompleter` | `IAiCompleter` |

### Manual smoke test before merge

1. Configure Ollama with `qwen2.5:3b`, enable structural formatting.
2. Dictate: "There are three things I want to cover. First, we need to fix the auth bug. Then we should improve logging — that's been broken for weeks. And finally, the deployment script needs work."
3. Expect output formatted as a numbered list with each item on its own line.
4. Verify `Text` column in DB contains the structured version.
5. Toggle off, repeat — expect prose paragraph output.
6. Switch to OpenAI/Anthropic, set API key, repeat — same behavior.

## Open questions

None. All decisions made during brainstorming.
