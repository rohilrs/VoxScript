# Custom Sounds + Paste Last Transcript

Date: 2026-04-06

## Overview

Two independent features: (1) audible feedback cues for recording lifecycle events, and (2) a hotkey to re-paste the most recent transcription into any window.

---

## Feature 1: Custom Sounds

### Audio Assets

Three WAV files generated via Python script (percussive woodblock/click transients):

| File | Frequency | Pattern | Trigger |
|------|-----------|---------|---------|
| `start.wav` | ~800Hz | Single click | Recording begins |
| `toggle.wav` | ~650Hz | Single click | Hold mode converts to toggle mode |
| `stop.wav` | ~500Hz | Double click (two quick taps) | Recording ends |

Each tone: sharp attack, fast exponential decay, band-passed noise. Duration ~100-150ms per click. 44100Hz sample rate, 16-bit mono PCM WAV.

**Asset location:** `VoxScript/Assets/Sounds/{start,toggle,stop}.wav` — included as Content files (CopyToOutputDirectory).

### Generation Script

Python script at `scripts/generate_sounds.py` using numpy + scipy (or just numpy + struct for raw WAV writing). Generates all three files. Run once, commit the WAVs, script stays for reproducibility.

### Playback Service

**Interface** in `VoxScript.Core/Audio/ISoundEffectsService.cs`:

```csharp
public interface ISoundEffectsService
{
    void PlayStart();
    void PlayToggle();
    void PlayStop();
}
```

**Implementation** in `VoxScript.Native/Audio/SoundEffectsService.cs`:

- Uses `System.Media.SoundPlayer` (built-in, lightweight, WAV-only)
- Pre-loads all three SoundPlayer instances on construction (avoids file I/O on each play)
- Resolves WAV paths relative to the application base directory
- Each method checks `AppSettings.SoundEffectsEnabled` before playing

### Integration Points

In `VoxScriptEngine` (which already manages state transitions):

- `StartRecordingAsync()` — after `State = RecordingState.Recording` (line ~131): call `PlayStart()`
- Toggle mode activation — when hold converts to toggle: call `PlayToggle()`
- `StopAndTranscribeAsync()` — before transcription begins (line ~173): call `PlayStop()`
- `CancelRecordingAsync()` — call `PlayStop()`

The engine receives `ISoundEffectsService` via constructor injection.

### Toggle Sound Trigger

The hold-to-toggle conversion happens in `GlobalHotkeyService` (when Space is pressed during a hold). The engine doesn't currently know about this transition. Two options:

**Option chosen:** Add a `ToggleLockActivated` event to `GlobalHotkeyService`. The wiring in `App.xaml.cs` subscribes to this event and calls `ISoundEffectsService.PlayToggle()` directly — keeps the engine focused on recording state, and the sound is immediate (no async round-trip).

---

## Feature 2: Paste Last Transcript

### Storage

`VoxScriptEngine` gains a public property:

```csharp
public string? LastTranscription { get; private set; }
```

Set after `_pipeline.RunAsync()` returns successfully in `StopAndTranscribeAsync()`, before the `TranscriptionCompleted` event fires. This captures the fully processed result (filtered, formatted, enhanced).

Persists across recordings within the app session. Resets to null on app start (no cross-session persistence).

### Hotkey

`GlobalHotkeyService` additions:

- `public event Action? PasteLastRequested;`
- `private HotkeyCombo? _pasteLastCombo;`
- `public void SetPasteLastHotkey(ModifierKeys modifiers, int triggerKey)` — same pattern as existing SetToggleHotkey/SetHoldHotkey/SetCancelHotkey
- Hook callback: new section checking `_pasteLastCombo` match, fires `PasteLastRequested`

The paste-last combo is a standard modifier+key combo (default Alt+Shift+Z), not a hold/toggle pattern. It fires on key-down.

### Wiring in App.xaml.cs

```
// In RegisterHotkeys():
var pasteLast = HotkeySerializer.Parse(settings.PasteLastHotkey);
if (pasteLast != null)
    _hotkey.SetPasteLastHotkey(pasteLast.Modifiers, pasteLast.TriggerKey);

// Handler:
_hotkey.PasteLastRequested += async () =>
{
    var text = _engine.LastTranscription;
    if (!string.IsNullOrEmpty(text))
        await _paste.PasteAtCursorAsync(text);
};
```

### Edge Cases

- No transcription yet (app just launched): hotkey silently does nothing
- Works regardless of auto-paste setting (re-paste use case)
- Does not interact with recording state — can paste while idle, or even while recording

---

## DI Registration

In `AppBootstrapper.cs`:

- Register `ISoundEffectsService` -> `SoundEffectsService` (singleton)
- Inject into `VoxScriptEngine`

---

## Files Changed

| File | Change |
|------|--------|
| `scripts/generate_sounds.py` | New — tone generation script |
| `VoxScript/Assets/Sounds/start.wav` | New — generated asset |
| `VoxScript/Assets/Sounds/toggle.wav` | New — generated asset |
| `VoxScript/Assets/Sounds/stop.wav` | New — generated asset |
| `VoxScript.Core/Audio/ISoundEffectsService.cs` | New — interface |
| `VoxScript.Native/Audio/SoundEffectsService.cs` | New — implementation |
| `VoxScript.Core/Transcription/Core/VoiceInkEngine.cs` | Add sound calls + LastTranscription property |
| `VoxScript.Native/Platform/GlobalHotkeyService.cs` | Add PasteLastRequested event + ToggleLockActivated event |
| `VoxScript/App.xaml.cs` | Wire paste-last hotkey + toggle sound |
| `VoxScript/Infrastructure/AppBootstrapper.cs` | Register ISoundEffectsService |
| `VoxScript/VoxScript.csproj` | Add Content items for WAV files |
| `STATUS.md` | Mark both features done |

## Testing

- Unit test: `VoxScriptEngine` sets `LastTranscription` after successful pipeline run
- Manual test: verify sounds play on start/toggle/stop, respect the settings toggle
- Manual test: Alt+Shift+Z pastes last transcript into a different window
