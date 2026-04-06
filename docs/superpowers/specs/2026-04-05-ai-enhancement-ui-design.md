# AI Enhancement UI + Personalization Styles

## Overview

Wire the existing AI enhancement backend to the UI via two pages:
- **Settings page** — new "AI Enhancement" card for provider config, API keys, on/off toggle
- **Personalize page** — style presets (Formal/Semi-casual/Casual/Custom) with guided options and editable system prompt

The app is offline-first. Local inference via Ollama is the primary path; OpenAI/Anthropic are optional cloud alternatives.

---

## Settings Page — AI Enhancement Card

New card in SettingsPage.xaml between the Input and App cards.

### Controls

| Control | Type | Maps to | Default |
|---------|------|---------|---------|
| Enable AI Enhancement | Toggle | `AppSettings.AiEnhancementEnabled` | `false` |
| Provider | Dropdown: Local (Ollama) / OpenAI / Anthropic | `AppSettings.AiProvider` | `Local` |
| Status indicator | Read-only text | Computed | — |

### Provider-specific fields (shown/hidden based on Provider selection)

**Local (Ollama):**
- Ollama endpoint — text field, default `http://localhost:11434`, maps to `AppSettings.OllamaEndpoint`
- Model name — text field, default `llama3.2`, maps to `AppSettings.AiModelName`

**OpenAI:**
- API key — password-masked text field + Save/Clear buttons, stored via `ApiKeyManager.SetOpenAiKey()`
- Model name — text field, default `gpt-4o-mini`, maps to `AppSettings.AiModelName`

**Anthropic:**
- API key — password-masked text field + Save/Clear buttons, stored via `ApiKeyManager.SetAnthropicKey()`
- Model name — text field, default `claude-sonnet-4-20250514`, maps to `AppSettings.AiModelName`

### Status indicator logic

- Local: "Connected" if Ollama responds at endpoint, "Ollama not running" otherwise
- OpenAI/Anthropic: "Configured" if API key is set, "Not configured" if empty
- Enhancement disabled: no status shown

---

## Personalize Page — Transcription Style

Replace the current placeholder page content with a "Transcription Style" section.

### Preset selector

Radio buttons or segmented control. Selecting a preset populates the guided options and system prompt text area.

| Preset | System prompt |
|--------|--------------|
| **Formal** | "You are a transcription editor. Use professional tone, complete sentences, proper capitalization and punctuation. Avoid contractions and colloquialisms. Return only the corrected text with no explanation." |
| **Semi-casual** | "You are a transcription editor. Fix grammar and punctuation. Keep a natural conversational tone. Contractions are fine. Return only the corrected text with no explanation." |
| **Casual** | "You are a transcription editor. Light cleanup only. Keep informal language, lowercase is fine. Just fix obvious errors. Return only the corrected text with no explanation." |
| **Custom** | User-defined (initially populated from last selected preset) |

Stored as `AppSettings.EnhancementPreset` (enum: Formal/SemiCasual/Casual/Custom).

### Guided options

Always visible, pre-filled based on preset, user-editable. Changing any option switches the preset to Custom.

| Option | Type | Default (Semi-casual) | Maps to |
|--------|------|-----------------------|---------|
| Punctuation style | Dropdown: Standard / Minimal | Standard | `AppSettings.EnhancementPunctuation` |
| Capitalization | Dropdown: Sentence case / As spoken | Sentence case | `AppSettings.EnhancementCapitalization` |
| Remove filler words | Toggle | On | `AppSettings.EnhancementRemoveFillers` |

### System prompt text area

- Editable multi-line text area showing the full prompt
- Selecting a preset regenerates it from the preset template + guided options
- Editing the text area manually switches the preset to Custom
- This is the source of truth — what the LLM actually receives
- Stored as `AppSettings.EnhancementSystemPrompt`

### Prompt composition

When a preset is selected or guided options change, the prompt is rebuilt:
```
{preset base prompt}
{if Minimal punctuation: "Use minimal punctuation — avoid commas and semicolons where possible."}
{if As spoken capitalization: "Preserve the speaker's original capitalization choices."}
{if Remove fillers on: "Remove filler words like um, uh, like, you know."}
```

---

## Backend Changes

### AppSettings — new properties

```csharp
bool AiEnhancementEnabled          // default false
AiProvider AiProvider               // enum: Local, OpenAI, Anthropic — default Local
string AiModelName                  // default "llama3.2"
string OllamaEndpoint              // default "http://localhost:11434"
EnhancementPreset EnhancementPreset // enum: Formal, SemiCasual, Casual, Custom — default SemiCasual
string EnhancementPunctuation       // "Standard" or "Minimal" — default "Standard"
string EnhancementCapitalization    // "SentenceCase" or "AsSpoken" — default "SentenceCase"
bool EnhancementRemoveFillers       // default true
string EnhancementSystemPrompt      // the full prompt text
```

### AiProvider enum (new)

```csharp
public enum AiProvider { Local, OpenAI, Anthropic }
```

### EnhancementPreset enum (new)

```csharp
public enum EnhancementPreset { Formal, SemiCasual, Casual, Custom }
```

### AIService refactor

Currently uses hardcoded priority fallback (OpenAI key? use OpenAI; Anthropic key? use Anthropic; else Ollama). Change to explicit provider routing:

- Read `AppSettings.AiProvider` to determine which provider to call
- Read `AppSettings.AiModelName` to determine which model (instead of hardcoded `gpt-4o-mini` etc.)
- Read `AppSettings.OllamaEndpoint` for local endpoint (instead of hardcoded `localhost:11434`)
- Remove the priority fallback logic

The three private methods (`CompleteOpenAiAsync`, `CompleteAnthropicAsync`, `CompleteOllamaAsync`) stay as-is, just called explicitly based on config.

### AIEnhancementService change

Currently uses a hardcoded default prompt. Change to:
- Read `AppSettings.EnhancementSystemPrompt`
- Call `EnhanceWithPromptAsync(text, prompt, ct)` instead of `EnhanceAsync(text, ct)`

### LlmModelManager (new, future)

For the future llama.cpp integration pass. Not part of this implementation — Ollama handles model management for now. When built, it will mirror `WhisperModelManager`: separate directory (`%LOCALAPPDATA%\VoxScript\LlmModels\`), same download/import pattern.

---

## Data Flow

```
User speaks
  -> Whisper transcribes
  -> Pipeline post-processing (filter, format, word replacement)
  -> if AiEnhancementEnabled:
      -> read EnhancementSystemPrompt from AppSettings
      -> AIEnhancementService.EnhanceWithPromptAsync(text, prompt, ct)
          -> AIService.CompleteAsync(prompt, text, ct)
              -> routes to configured provider (Local/OpenAI/Anthropic)
          -> AIEnhancementOutputFilter validates output
      -> store both original + enhanced in DB
  -> paste final text
```

---

## Error Handling

- Enhancement failure returns null; pipeline falls back to raw text (existing behavior)
- Settings page status indicator gives feedback before transcription
- No transcript is ever lost due to enhancement failure (existing guarantee)

---

## Files Changed

| File | Change |
|------|--------|
| `VoxScript.Core/Settings/AppSettings.cs` | Add new properties listed above |
| `VoxScript.Core/AI/AIService.cs` | Refactor to explicit provider routing + configurable model/endpoint |
| `VoxScript.Core/AI/AIEnhancementService.cs` | Read system prompt from settings |
| `VoxScript.Core/AI/AiProvider.cs` | New enum file |
| `VoxScript.Core/AI/EnhancementPreset.cs` | New enum file |
| `VoxScript/Views/SettingsPage.xaml` | Add AI Enhancement card |
| `VoxScript/ViewModels/SettingsViewModel.cs` | Add AI-related properties and provider switching |
| `VoxScript/Views/PersonalizePage.xaml` + `.cs` | Replace placeholder with style presets UI |
| `VoxScript/ViewModels/PersonalizeViewModel.cs` | New — preset selection, guided options, prompt composition |

## Not in scope

- LlmModelManager / built-in llama.cpp (future pass)
- Power Mode UI on Personalize page (future pass)
- PromptDetectionService integration (future pass)
- Enhancement progress indicator on TranscribePage (minor, can add later)
