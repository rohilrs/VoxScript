# Parakeet End-to-End Verification + Whisper Swappability — Design Spec

**Date:** 2026-04-06
**Scope:** Phase A (verify pipeline), Phase B (make swappable with Whisper)

## Overview

Verify the existing Parakeet ONNX Runtime backend works end-to-end, fix the tokenizer stub, and wire it into the app as a selectable transcription backend alongside Whisper.

## Phase A: End-to-End Pipeline Verification

### Python Export Script

**File:** `scripts/export_parakeet.py`

Requires: `pip install nemo_toolkit[asr] onnx`

The script:
1. Loads `nvidia/parakeet-tdt-0.6b-v2` from HuggingFace via NeMo
2. Exports the model to ONNX format
3. Extracts the SentencePiece tokenizer `.model` file
4. Saves both to a user-specified output directory (default: `models/parakeet/`)
5. Prints file paths and sizes on completion

The `models/` directory is gitignored. Users run this script once to get model files, then import them into VoxScript via the Model Management dialog's file picker.

### Fix the Tokenizer

**Replace:** `VoxScript.Native/Parakeet/ParakeetTokenizer.cs` (current stub)
**With:** A wrapper around `Microsoft.ML.Tokenizers.SentencePieceTokenizer`

- Add `Microsoft.ML.Tokenizers` NuGet package to `VoxScript.Native.csproj`
- Constructor takes a `.model` file path, loads via `SentencePieceTokenizer.Create(stream)`
- `Decode(List<int> tokenIds)` → delegates to library's decode method
- Remove the `.vocab.txt` fallback and TODO comment

### Unit Tests

**MelSpectrogram tests:**
- Feed a known-length silence array, verify output dimensions are `[80, expectedFrames]`
- Feed a synthetic sine wave, verify output is non-zero and within reasonable dB range
- Verify frame count formula: `(samples.Length - winLength) / hopLength + 1`

**ParakeetTokenizer tests:**
- Verify `Decode` produces text from token IDs (requires a small test `.model` file or mocking the library)
- If mocking isn't feasible, test with a real `.model` file as an integration test gated by file existence

**CTC Decoder tests:**
- Feed synthetic logits where token 5 has highest probability for 3 consecutive frames → should collapse to single token `[5]`
- Verify blank token (index 0) is skipped
- Verify repeated tokens are collapsed

### Integration Test (Manual)

Not automated — requires the ~1.2GB model file:
1. Run `scripts/export_parakeet.py` to get model files
2. Load ONNX model via `ParakeetBackend.LoadModelAsync`
3. Feed a short WAV file through the full pipeline
4. Verify intelligible English text output
5. Document results in a test log or STATUS.md

## Phase B: Swappable with Whisper

### Make ParakeetBackend Implement ILocalTranscriptionBackend

Current `IParakeetBackend` interface has a different signature. Changes to `ParakeetBackend`:

- Add `ILocalTranscriptionBackend` to the implements list (keep `IParakeetBackend` too)
- Add `UnloadModel()` — dispose the ONNX session and tokenizer, set `_session = null`
- Add/modify `TranscribeAsync(float[] samples, string? language, string? initialPrompt, CancellationToken ct)`:
  - Ignores `language` (Parakeet is English-only)
  - Ignores `initialPrompt` (no Whisper-style prompting)
  - Runs the existing inference pipeline
  - Returns `ParakeetResult.Text` as string

### Create ParakeetTranscriptionService

**New file:** `VoxScript.Core/Transcription/Batch/ParakeetTranscriptionService.cs`

Mirrors `LocalTranscriptionService` but with `Provider => ModelProvider.Parakeet`:
- Implements `ITranscriptionService`
- Reads WAV file, converts to float[], calls backend's `TranscribeAsync`
- Does NOT pass vocabulary as `initialPrompt` (Parakeet ignores it)

**DI approach:** `ParakeetTranscriptionService` takes `ParakeetBackend` directly (concrete type injection) rather than `ILocalTranscriptionBackend`. This avoids ambiguity — `LocalTranscriptionService` continues to resolve `ILocalTranscriptionBackend` → `WhisperBackend`, while `ParakeetTranscriptionService` gets its own backend. Both are registered as `ITranscriptionService` and the existing `TranscriptionServiceRegistry` routes by `ModelProvider` enum — no changes needed to the registry or pipeline.

### DI Registration

In `AppBootstrapper.cs`:
```
services.AddSingleton<ParakeetBackend>();
services.AddSingleton<ITranscriptionService, ParakeetTranscriptionService>();
```

`ParakeetTranscriptionService` constructor takes `ParakeetBackend` directly (registered as concrete singleton). `LocalTranscriptionService` continues to take `ILocalTranscriptionBackend` which resolves to `WhisperBackend`.

### Add to PredefinedModels

Add one entry to `PredefinedModels.cs`:
```csharp
public static readonly TranscriptionModel ParakeetTdt = new(
    ModelProvider.Parakeet, "parakeet-tdt-0.6b-v2", "Parakeet TDT 0.6B v2",
    false, true, null, 1_200_000_000L);
```

- `DownloadUrl` is `null` — user imports via file picker after running export script
- Shows up in Model Management dialog's predefined list
- Import button lets user point to the local `.onnx` file

### Model Loading

When user selects a Parakeet model in the Model Management dialog:
- `WhisperBackend` stays loaded (or gets unloaded — doesn't matter, they're independent)
- `ParakeetBackend.LoadModelAsync` is called with the `.onnx` path
- The `.model` tokenizer file must be co-located (same directory, same base name with `.model` extension)
- `AppSettings.SelectedModelName` stores `"parakeet-tdt-0.6b-v2"`
- On next transcription, `TranscriptionServiceRegistry` routes to `ParakeetTranscriptionService` because the model's provider is `ModelProvider.Parakeet`

### Model Management Dialog Changes

The dialog already supports:
- Listing predefined models with download/use/delete
- Importing local `.bin` files via file picker

Changes needed:
- File picker filter: add `.onnx` alongside `.bin`
- When importing a `.onnx` file, also copy the co-located `.model` file (tokenizer) if it exists in the same source directory
- When selecting a Parakeet model as active, load it via `ParakeetBackend` instead of `WhisperBackend`

## Out of Scope

- Word-level timing (returns empty list)
- Streaming via WordAgreementEngine
- Auto-downloading Parakeet models from HuggingFace
- Python invocation from the app
- Non-English language support for Parakeet
- Confidence scoring in WordToken
