# Auto-Add to Dictionary Design

## Overview

After each transcription, automatically extract specialized words (proper nouns, technical terms, jargon) and add them to the vocabulary. Common English words are filtered out using a static ~10K word list. Respects the existing `AutoAddToDictionary` setting toggle. Silently adds — no UI notification.

## Pipeline Integration

New step in `TranscriptionPipeline`, after word replacement (step 4), before AI enhancement. Wrapped in try-catch like every other pipeline step for graceful degradation.

```
Record → VAD → Whisper → hallucination filter → format → word replacement
  → **auto-vocabulary** → AI enhancement → save to DB → auto-paste
```

## Word Extraction Logic

1. Split transcribed text on whitespace and punctuation (`Regex` split on `[\s\p{P}]+` or similar)
2. For each token:
   - Skip if single character or pure numeric
   - Skip if already in vocabulary (dedup check)
   - Normalize to lowercase for comparison against common word list
   - If the lowercase form is NOT in the common word set → add the original-cased word to vocabulary
3. Batch-add all new words via `IVocabularyRepository`

## Common Word List

- Static `HashSet<string>` of ~10K common English words (lowercase)
- Source: standard English frequency corpus (top 10K by usage)
- Stored as `VoxScript/Assets/Data/common-words.txt`, one word per line
- Loaded once on first use (lazy singleton), held in memory
- Embedded as a content file in the project

## Architecture

**New interface** in `VoxScript.Core/Dictionary/IAutoVocabularyService.cs`:

```csharp
public interface IAutoVocabularyService
{
    Task ProcessTranscriptionAsync(string text, CancellationToken ct);
}
```

**Implementation** in `VoxScript.Core/Dictionary/AutoVocabularyService.cs`:

- Constructor: `IVocabularyRepository vocabRepo, ICommonWordList commonWords`
- `ProcessTranscriptionAsync`: extracts words, filters, deduplicates, batch-adds
- No platform dependencies — lives entirely in Core

**Common word list interface** in `VoxScript.Core/Dictionary/ICommonWordList.cs`:

```csharp
public interface ICommonWordList
{
    bool Contains(string word);
}
```

**Implementation** in `VoxScript.Core/Dictionary/CommonWordList.cs`:

- Accepts word list file path in constructor
- Reads `common-words.txt` on first access (lazy load)
- Populates a `HashSet<string>` (case-insensitive via `StringComparer.OrdinalIgnoreCase`)
- Thread-safe lazy initialization

**Pipeline modification** in `TranscriptionPipeline.cs`:

- Inject `IAutoVocabularyService` and `AppSettings`
- After word replacement step, if `settings.AutoAddToDictionary` is true, call `ProcessTranscriptionAsync`
- Wrapped in try-catch (same pattern as other steps)

**DI registration** in `AppBootstrapper.cs`:

- `services.AddSingleton<ICommonWordList, CommonWordList>()`
- `services.AddSingleton<IAutoVocabularyService, AutoVocabularyService>()`

## Settings

Uses existing `AppSettings.AutoAddToDictionary` property (defaults to `false`). Toggle already exists in Settings UI but is currently disabled — enable it.

## What's NOT in Scope

- Frequency tracking (add on Nth occurrence)
- User notification/badge on new words
- Custom word list management
- Language-specific word lists (English only for now)
- Feeding vocabulary into Whisper's `initialPrompt` (separate enhancement)
