# Custom Sounds, Paste Last Transcript, Pause Media — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add audible recording cues (woodblock/click tones), a hotkey to re-paste the last transcription, and media pause/resume during dictation.

**Architecture:** Three independent features sharing common touch-points (VoxScriptEngine, GlobalHotkeyService, App.xaml.cs, AppBootstrapper). Each feature adds an interface in Core, an implementation in Native, and wiring in the app layer.

**Tech Stack:** C# / .NET 10 / WinUI 3, Python (numpy) for WAV generation, `System.Media.SoundPlayer` for playback, `keybd_event` for media key simulation.

---

### Task 1: Generate WAV Sound Assets

**Files:**
- Create: `scripts/generate_sounds.py`
- Create: `VoxScript/Assets/Sounds/start.wav`
- Create: `VoxScript/Assets/Sounds/toggle.wav`
- Create: `VoxScript/Assets/Sounds/stop.wav`

- [ ] **Step 1: Create the generation script**

```python
#!/usr/bin/env python3
"""Generate woodblock/click-track WAV files for VoxScript recording cues."""

import struct
import math
import os

SAMPLE_RATE = 44100
BITS = 16
CHANNELS = 1


def generate_click(freq: float, duration_ms: float = 100, attack_ms: float = 1,
                   decay_ms: float = 60) -> list[int]:
    """Generate a single woodblock click: sharp attack, fast exponential decay."""
    n_samples = int(SAMPLE_RATE * duration_ms / 1000)
    attack_samples = int(SAMPLE_RATE * attack_ms / 1000)
    decay_samples = int(SAMPLE_RATE * decay_ms / 1000)
    samples = []
    for i in range(n_samples):
        # Sine tone at the given frequency
        t = i / SAMPLE_RATE
        tone = math.sin(2 * math.pi * freq * t)

        # Add a second harmonic for woodblock character
        tone += 0.3 * math.sin(2 * math.pi * freq * 2.8 * t)

        # Envelope: linear attack, exponential decay
        if i < attack_samples:
            env = i / max(attack_samples, 1)
        elif i < attack_samples + decay_samples:
            decay_pos = (i - attack_samples) / max(decay_samples, 1)
            env = math.exp(-5 * decay_pos)
        else:
            env = 0.0

        sample = int(tone * env * 28000)
        sample = max(-32768, min(32767, sample))
        samples.append(sample)
    return samples


def write_wav(filepath: str, samples: list[int]) -> None:
    """Write 16-bit mono PCM WAV."""
    data_size = len(samples) * 2
    file_size = 36 + data_size
    with open(filepath, "wb") as f:
        # RIFF header
        f.write(b"RIFF")
        f.write(struct.pack("<I", file_size))
        f.write(b"WAVE")
        # fmt chunk
        f.write(b"fmt ")
        f.write(struct.pack("<I", 16))  # chunk size
        f.write(struct.pack("<H", 1))   # PCM format
        f.write(struct.pack("<H", CHANNELS))
        f.write(struct.pack("<I", SAMPLE_RATE))
        f.write(struct.pack("<I", SAMPLE_RATE * CHANNELS * BITS // 8))
        f.write(struct.pack("<H", CHANNELS * BITS // 8))
        f.write(struct.pack("<H", BITS))
        # data chunk
        f.write(b"data")
        f.write(struct.pack("<I", data_size))
        for s in samples:
            f.write(struct.pack("<h", s))


def main() -> None:
    out_dir = os.path.join(os.path.dirname(__file__), "..", "VoxScript", "Assets", "Sounds")
    os.makedirs(out_dir, exist_ok=True)

    # Start: ~800Hz single click
    start_samples = generate_click(800, duration_ms=120)
    write_wav(os.path.join(out_dir, "start.wav"), start_samples)

    # Toggle: ~650Hz single click
    toggle_samples = generate_click(650, duration_ms=120)
    write_wav(os.path.join(out_dir, "toggle.wav"), toggle_samples)

    # Stop: ~500Hz double click (two taps with 80ms gap)
    tap1 = generate_click(500, duration_ms=100)
    gap = [0] * int(SAMPLE_RATE * 0.08)  # 80ms silence
    tap2 = generate_click(500, duration_ms=100)
    stop_samples = tap1 + gap + stop_samples_placeholder
    stop_samples = tap1 + gap + tap2
    write_wav(os.path.join(out_dir, "stop.wav"), stop_samples)

    print(f"Generated WAVs in {out_dir}")


if __name__ == "__main__":
    main()
```

Wait — there's a bug on the `stop_samples` line. Here's the corrected `main()`:

```python
def main() -> None:
    out_dir = os.path.join(os.path.dirname(__file__), "..", "VoxScript", "Assets", "Sounds")
    os.makedirs(out_dir, exist_ok=True)

    # Start: ~800Hz single click
    start_samples = generate_click(800, duration_ms=120)
    write_wav(os.path.join(out_dir, "start.wav"), start_samples)

    # Toggle: ~650Hz single click
    toggle_samples = generate_click(650, duration_ms=120)
    write_wav(os.path.join(out_dir, "toggle.wav"), toggle_samples)

    # Stop: ~500Hz double click (two taps with 80ms gap)
    tap1 = generate_click(500, duration_ms=100)
    gap = [0] * int(SAMPLE_RATE * 0.08)  # 80ms silence
    tap2 = generate_click(500, duration_ms=100)
    stop_samples = tap1 + gap + tap2
    write_wav(os.path.join(out_dir, "stop.wav"), stop_samples)

    print(f"Generated WAVs in {out_dir}")
```

- [ ] **Step 2: Run the script to generate WAVs**

Run: `python scripts/generate_sounds.py`
Expected: Three WAV files created in `VoxScript/Assets/Sounds/`

- [ ] **Step 3: Verify the WAVs are valid**

Run: `python -c "import wave; [print(f'{n}: {wave.open(f\"VoxScript/Assets/Sounds/{n}\").getnframes()} frames') for n in ('start.wav','toggle.wav','stop.wav')]"`
Expected: Each file has >1000 frames, no errors.

- [ ] **Step 4: Add Content items to VoxScript.csproj**

In `VoxScript/VoxScript.csproj`, add inside the existing `<ItemGroup>` that has `<None Include="NativeBinaries\...">`, or as a new ItemGroup:

```xml
<ItemGroup>
  <Content Include="Assets\Sounds\*.wav">
    <CopyToOutputDirectory>PreserveNewest</CopyToOutputDirectory>
  </Content>
</ItemGroup>
```

- [ ] **Step 5: Commit**

```bash
git add scripts/generate_sounds.py VoxScript/Assets/Sounds/ VoxScript/VoxScript.csproj
git commit -m "feat: generate woodblock click WAVs for recording cues"
```

---

### Task 2: ISoundEffectsService Interface (Core)

**Files:**
- Create: `VoxScript.Core/Audio/ISoundEffectsService.cs`

- [ ] **Step 1: Create the interface**

```csharp
namespace VoxScript.Core.Audio;

public interface ISoundEffectsService
{
    void PlayStart();
    void PlayToggle();
    void PlayStop();
}
```

- [ ] **Step 2: Build to verify**

Run: `dotnet build VoxScript.Core`
Expected: Build succeeded.

- [ ] **Step 3: Commit**

```bash
git add VoxScript.Core/Audio/ISoundEffectsService.cs
git commit -m "feat: add ISoundEffectsService interface in Core"
```

---

### Task 3: SoundEffectsService Implementation (Native)

**Files:**
- Create: `VoxScript.Native/Audio/SoundEffectsService.cs`
- Modify: `VoxScript/Infrastructure/AppBootstrapper.cs`

- [ ] **Step 1: Create the implementation**

```csharp
using System.Media;
using VoxScript.Core.Audio;
using VoxScript.Core.Settings;
using Serilog;

namespace VoxScript.Native.Audio;

public sealed class SoundEffectsService : ISoundEffectsService
{
    private readonly AppSettings _settings;
    private readonly SoundPlayer? _startPlayer;
    private readonly SoundPlayer? _togglePlayer;
    private readonly SoundPlayer? _stopPlayer;

    public SoundEffectsService(AppSettings settings)
    {
        _settings = settings;

        var baseDir = AppContext.BaseDirectory;
        _startPlayer = LoadPlayer(Path.Combine(baseDir, "Assets", "Sounds", "start.wav"));
        _togglePlayer = LoadPlayer(Path.Combine(baseDir, "Assets", "Sounds", "toggle.wav"));
        _stopPlayer = LoadPlayer(Path.Combine(baseDir, "Assets", "Sounds", "stop.wav"));
    }

    public void PlayStart()
    {
        if (_settings.SoundEffectsEnabled)
            _startPlayer?.Play();
    }

    public void PlayToggle()
    {
        if (_settings.SoundEffectsEnabled)
            _togglePlayer?.Play();
    }

    public void PlayStop()
    {
        if (_settings.SoundEffectsEnabled)
            _stopPlayer?.Play();
    }

    private static SoundPlayer? LoadPlayer(string path)
    {
        try
        {
            if (!File.Exists(path))
            {
                Log.Warning("Sound file not found: {Path}", path);
                return null;
            }
            var player = new SoundPlayer(path);
            player.Load();
            return player;
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Failed to load sound: {Path}", path);
            return null;
        }
    }
}
```

- [ ] **Step 2: Register in DI container**

In `VoxScript/Infrastructure/AppBootstrapper.cs`, add after the `services.AddSingleton<IAudioCaptureService, WasapiCaptureService>();` line (line 39):

```csharp
services.AddSingleton<ISoundEffectsService, VoxScript.Native.Audio.SoundEffectsService>();
```

Add the using at the top if needed — `VoxScript.Core.Audio` should already be imported; if not, add it.

- [ ] **Step 3: Build to verify**

Run: `dotnet build VoxScript.slnx`
Expected: Build succeeded.

- [ ] **Step 4: Commit**

```bash
git add VoxScript.Native/Audio/SoundEffectsService.cs VoxScript/Infrastructure/AppBootstrapper.cs
git commit -m "feat: implement SoundEffectsService with pre-loaded WAV playback"
```

---

### Task 4: Wire Sound Effects into VoxScriptEngine

**Files:**
- Modify: `VoxScript.Core/Transcription/Core/VoiceInkEngine.cs`

- [ ] **Step 1: Add ISoundEffectsService to VoxScriptEngine constructor**

Add `ISoundEffectsService` as the last constructor parameter:

In the fields section (after `private readonly IPasteService _paste;`, line 17), add:

```csharp
private readonly ISoundEffectsService _sounds;
```

Update the constructor (lines 42-54) to accept and store it:

```csharp
public VoxScriptEngine(
    IAudioCaptureService audio,
    TranscriptionServiceRegistry registry,
    TranscriptionPipeline pipeline,
    AppSettings settings,
    IPasteService paste,
    ISoundEffectsService sounds)
{
    _audio = audio;
    _registry = registry;
    _pipeline = pipeline;
    _settings = settings;
    _paste = paste;
    _sounds = sounds;
}
```

- [ ] **Step 2: Play start sound after State = Recording**

In `StartRecordingAsync()`, right after line 131 (`State = RecordingState.Recording;`), add:

```csharp
_sounds.PlayStart();
```

- [ ] **Step 3: Play stop sound in StopAndTranscribeAsync**

In `StopAndTranscribeAsync()`, right after line 173 (`State = RecordingState.Transcribing;`), add:

```csharp
_sounds.PlayStop();
```

- [ ] **Step 4: Play stop sound in CancelRecordingAsync**

In `CancelRecordingAsync()`, right after line 267 (`State = RecordingState.Idle;`), add:

```csharp
_sounds.PlayStop();
```

- [ ] **Step 5: Build to verify**

Run: `dotnet build VoxScript.slnx`
Expected: Build succeeded.

- [ ] **Step 6: Commit**

```bash
git add VoxScript.Core/Transcription/Core/VoiceInkEngine.cs
git commit -m "feat: play start/stop sounds on recording state transitions"
```

---

### Task 5: Wire Toggle Sound via GlobalHotkeyService Event

**Files:**
- Modify: `VoxScript.Native/Platform/GlobalHotkeyService.cs`
- Modify: `VoxScript/App.xaml.cs`

- [ ] **Step 1: Add ToggleLockActivated event to GlobalHotkeyService**

In `GlobalHotkeyService.cs`, after line 27 (`public event EventHandler? RecordingCancelRequested;`), add:

```csharp
public event Action? ToggleLockActivated;
```

- [ ] **Step 2: Fire the event when hold converts to toggle**

In the `HookCallback` method, find the block at lines 215-221 where hold converts to toggle mode (the `Log.Debug("Hold converted to toggle-locked mode")` line). After that log line (line 219), add:

```csharp
ThreadPool.QueueUserWorkItem(_ => ToggleLockActivated?.Invoke());
```

- [ ] **Step 3: Wire the toggle sound in App.xaml.cs**

In `App.xaml.cs`, after the `_hotkey.RecordingCancelRequested` handler block (around line 162), before the hotkey settings parsing, add:

```csharp
var soundService = ServiceLocator.Get<ISoundEffectsService>();
_hotkey.ToggleLockActivated += () =>
{
    soundService.PlayToggle();
};
```

Add the using at the top of the file:

```csharp
using VoxScript.Core.Audio;
```

- [ ] **Step 4: Build to verify**

Run: `dotnet build VoxScript.slnx`
Expected: Build succeeded.

- [ ] **Step 5: Commit**

```bash
git add VoxScript.Native/Platform/GlobalHotkeyService.cs VoxScript/App.xaml.cs
git commit -m "feat: play toggle sound when hold converts to toggle mode"
```

---

### Task 6: LastTranscription Property on VoxScriptEngine

**Files:**
- Modify: `VoxScript.Core/Transcription/Core/VoiceInkEngine.cs`
- Create: `VoxScript.Tests/Transcription/VoxScriptEngineTests.cs`

- [ ] **Step 1: Write the test**

```csharp
using FluentAssertions;
using NSubstitute;
using VoxScript.Core.Audio;
using VoxScript.Core.Platform;
using VoxScript.Core.Settings;
using VoxScript.Core.Transcription.Core;
using VoxScript.Core.Transcription.Models;
using Xunit;

namespace VoxScript.Tests.Transcription;

public class VoxScriptEngineTests
{
    [Fact]
    public void LastTranscription_is_null_by_default()
    {
        var engine = CreateEngine();
        engine.LastTranscription.Should().BeNull();
    }

    private static VoxScriptEngine CreateEngine()
    {
        var audio = Substitute.For<IAudioCaptureService>();
        var registry = Substitute.For<TranscriptionServiceRegistry>(
            Enumerable.Empty<ITranscriptionService>(),
            Enumerable.Empty<IStreamingProvider>());
        var pipeline = Substitute.For<TranscriptionPipeline>(
            Substitute.For<ILocalTranscriptionBackend>(),
            new TranscriptionOutputFilter(),
            new WhisperTextFormatter(),
            Substitute.For<WordReplacementService>(
                Substitute.For<IWordReplacementRepository>(),
                Substitute.For<ICorrectionRepository>(),
                Substitute.For<IVocabularyRepository>()),
            Substitute.For<IAIEnhancementService>(),
            Substitute.For<ITranscriptionRepository>(),
            Substitute.For<PowerModeSessionManager>(
                Substitute.For<PowerModeManager>(),
                Substitute.For<IActiveWindowService>()));
        var settings = new AppSettings(new InMemorySettingsStore());
        var paste = Substitute.For<IPasteService>();
        var sounds = Substitute.For<ISoundEffectsService>();
        return new VoxScriptEngine(audio, registry, pipeline, settings, paste, sounds);
    }

    private sealed class InMemorySettingsStore : ISettingsStore
    {
        private readonly Dictionary<string, object?> _data = new();
        public T? Get<T>(string key) => _data.TryGetValue(key, out var v) ? (T?)v : default;
        public void Set<T>(string key, T value) => _data[key] = value;
        public bool Contains(string key) => _data.ContainsKey(key);
        public void Remove(string key) => _data.Remove(key);
    }
}
```

Note: The `CreateEngine()` helper may need adjustment depending on exact constructor signatures of `TranscriptionServiceRegistry`, `TranscriptionPipeline`, etc. Use `Substitute.For<T>()` for interfaces and check if concrete classes need real or mock dependencies. If NSubstitute can't proxy sealed/non-virtual classes, adjust to use real instances with mock dependencies.

- [ ] **Step 2: Run the test to verify it fails**

Run: `dotnet test VoxScript.Tests --filter "FullyQualifiedName~VoxScriptEngineTests.LastTranscription_is_null_by_default"`
Expected: FAIL — `LastTranscription` property doesn't exist yet.

- [ ] **Step 3: Add the LastTranscription property**

In `VoiceInkEngine.cs`, after the `IsToggleMode` property (line 37), add:

```csharp
public string? LastTranscription { get; private set; }
```

- [ ] **Step 4: Set LastTranscription after successful transcription**

In `StopAndTranscribeAsync()`, after the line `if (text is not null)` (line 183) and before the auto-paste block, add:

```csharp
LastTranscription = text;
```

So the block becomes:

```csharp
if (text is not null)
{
    LastTranscription = text;

    if (_settings.AutoPasteEnabled)
    {
        // ... existing code ...
    }

    TranscriptionCompleted?.Invoke(this, text);
}
```

- [ ] **Step 5: Run the test to verify it passes**

Run: `dotnet test VoxScript.Tests --filter "FullyQualifiedName~VoxScriptEngineTests.LastTranscription_is_null_by_default"`
Expected: PASS

- [ ] **Step 6: Run all tests**

Run: `dotnet test VoxScript.Tests`
Expected: All tests pass (existing tests may need updating due to the new constructor parameter — add `Substitute.For<ISoundEffectsService>()` to any test that constructs VoxScriptEngine).

- [ ] **Step 7: Commit**

```bash
git add VoxScript.Core/Transcription/Core/VoiceInkEngine.cs VoxScript.Tests/Transcription/VoxScriptEngineTests.cs
git commit -m "feat: add LastTranscription property to VoxScriptEngine"
```

---

### Task 7: PasteLastRequested Hotkey in GlobalHotkeyService

**Files:**
- Modify: `VoxScript.Native/Platform/GlobalHotkeyService.cs`

- [ ] **Step 1: Add paste-last fields and event**

In `GlobalHotkeyService.cs`, after the `_cancelCombo` field (line 52), add:

```csharp
// Paste-last hotkey: re-paste the most recent transcription
private HotkeyCombo? _pasteLastCombo;
public event Action? PasteLastRequested;
```

- [ ] **Step 2: Add the setter method**

After `SetCancelHotkey` (line 119), add:

```csharp
/// <summary>
/// Configure the paste-last hotkey (re-paste most recent transcription).
/// </summary>
public void SetPasteLastHotkey(ModifierKeys modifiers, int? triggerKey)
{
    _pasteLastCombo = new HotkeyCombo(modifiers, triggerKey);
}
```

- [ ] **Step 3: Add paste-last detection in HookCallback**

In the `HookCallback` method, after the hold combo section (section 2, around line 280, before the `passThrough:` label), add:

```csharp
// 3. Check paste-last hotkey (e.g. Alt+Shift+Z)
if (!consumed && _pasteLastCombo is { TriggerKey: not null } plc
    && isDown && vkCode == plc.TriggerKey
    && currentMods == plc.Modifiers)
{
    ThreadPool.QueueUserWorkItem(_ => PasteLastRequested?.Invoke());
    consumed = true;
}
```

Note: The `consumed` variable needs to be set to `true` in sections 0 and 1 as well so paste-last doesn't also fire. Check that sections 0 (cancel) and 1 (toggle) already set `consumed = true` — section 0 does (line 204), section 1 does in its branches. The hold section (2) doesn't use `consumed` but that's fine since it's modifier-only.

- [ ] **Step 4: Build to verify**

Run: `dotnet build VoxScript.slnx`
Expected: Build succeeded.

- [ ] **Step 5: Commit**

```bash
git add VoxScript.Native/Platform/GlobalHotkeyService.cs
git commit -m "feat: add PasteLastRequested hotkey event to GlobalHotkeyService"
```

---

### Task 8: Wire Paste-Last Hotkey in App.xaml.cs

**Files:**
- Modify: `VoxScript/App.xaml.cs`

- [ ] **Step 1: Register paste-last hotkey binding**

In `App.xaml.cs`, in the hotkey settings parsing section (after line 170 where `cancelCombo` is set), add:

```csharp
var pasteLastCombo = VoxScript.Helpers.HotkeySerializer.Parse(settings.PasteLastHotkey);
if (pasteLastCombo is not null) _hotkey.SetPasteLastHotkey(pasteLastCombo.Modifiers, pasteLastCombo.TriggerKey);
```

- [ ] **Step 2: Wire the paste-last handler**

After the `ToggleLockActivated` handler (added in Task 5), add:

```csharp
var paste = ServiceLocator.Get<IPasteService>();
_hotkey.PasteLastRequested += () =>
{
    var text = engine.LastTranscription;
    if (!string.IsNullOrEmpty(text))
    {
        _mainWindow!.DispatcherQueue.TryEnqueue(async () =>
        {
            try
            {
                await paste.PasteAtCursorAsync(text, CancellationToken.None);
            }
            catch (Exception ex)
            {
                Serilog.Log.Warning(ex, "Paste-last failed");
            }
        });
    }
};
```

Add using at the top if not already present:

```csharp
using VoxScript.Core.Platform;
```

- [ ] **Step 3: Update the log line to include paste-last**

Change the log line at line 173-174 from:

```csharp
Serilog.Log.Information("Global hotkeys registered: {Toggle} (toggle), {Hold} (hold), {Cancel} (cancel)",
    settings.ToggleHotkey, settings.HoldHotkey, settings.CancelHotkey);
```

To:

```csharp
Serilog.Log.Information("Global hotkeys registered: {Toggle} (toggle), {Hold} (hold), {Cancel} (cancel), {PasteLast} (paste-last)",
    settings.ToggleHotkey, settings.HoldHotkey, settings.CancelHotkey, settings.PasteLastHotkey);
```

- [ ] **Step 4: Build to verify**

Run: `dotnet build VoxScript.slnx`
Expected: Build succeeded.

- [ ] **Step 5: Commit**

```bash
git add VoxScript/App.xaml.cs
git commit -m "feat: wire paste-last hotkey (Alt+Shift+Z) to re-paste last transcription"
```

---

### Task 9: Pause Media While Dictating

**Files:**
- Create: `VoxScript.Core/Platform/IMediaControlService.cs`
- Create: `VoxScript.Native/Platform/MediaControlService.cs`
- Modify: `VoxScript.Core/Transcription/Core/VoiceInkEngine.cs`
- Modify: `VoxScript/Infrastructure/AppBootstrapper.cs`
- Modify: `VoxScript/Views/SettingsPage.xaml` (enable the toggle)

The simplest approach: send `VK_MEDIA_PLAY_PAUSE` (0xB3) via `keybd_event` when recording starts (to pause) and when recording stops (to resume). This works with Spotify, YouTube (browser), VLC, Windows Media Player, and anything that responds to media keys. No COM interop needed.

- [ ] **Step 1: Create the interface**

```csharp
namespace VoxScript.Core.Platform;

public interface IMediaControlService
{
    void PauseMedia();
    void ResumeMedia();
}
```

- [ ] **Step 2: Create the implementation**

```csharp
using System.Runtime.InteropServices;
using VoxScript.Core.Platform;
using Serilog;

namespace VoxScript.Native.Platform;

public sealed class MediaControlService : IMediaControlService
{
    private const int VK_MEDIA_PLAY_PAUSE = 0xB3;
    private const int KEYEVENTF_EXTENDEDKEY = 0x0001;
    private const int KEYEVENTF_KEYUP = 0x0002;

    private bool _paused;

    public void PauseMedia()
    {
        if (_paused) return;
        Log.Debug("Sending media pause key");
        SendMediaKey();
        _paused = true;
    }

    public void ResumeMedia()
    {
        if (!_paused) return;
        Log.Debug("Sending media resume key");
        SendMediaKey();
        _paused = false;
    }

    private static void SendMediaKey()
    {
        keybd_event((byte)VK_MEDIA_PLAY_PAUSE, 0, KEYEVENTF_EXTENDEDKEY, 0);
        keybd_event((byte)VK_MEDIA_PLAY_PAUSE, 0, KEYEVENTF_EXTENDEDKEY | KEYEVENTF_KEYUP, 0);
    }

    [DllImport("user32.dll")]
    private static extern void keybd_event(byte bVk, byte bScan, int dwFlags, int dwExtraInfo);
}
```

- [ ] **Step 3: Register in DI**

In `AppBootstrapper.cs`, add after the `ISoundEffectsService` registration:

```csharp
services.AddSingleton<IMediaControlService, MediaControlService>();
```

Add using if needed:

```csharp
using VoxScript.Core.Platform;
```

(This using may already exist for `IPasteService`.)

- [ ] **Step 4: Add IMediaControlService to VoxScriptEngine**

Add field after `_sounds`:

```csharp
private readonly IMediaControlService _media;
```

Update constructor to accept it:

```csharp
public VoxScriptEngine(
    IAudioCaptureService audio,
    TranscriptionServiceRegistry registry,
    TranscriptionPipeline pipeline,
    AppSettings settings,
    IPasteService paste,
    ISoundEffectsService sounds,
    IMediaControlService media)
{
    _audio = audio;
    _registry = registry;
    _pipeline = pipeline;
    _settings = settings;
    _paste = paste;
    _sounds = sounds;
    _media = media;
}
```

- [ ] **Step 5: Pause media when recording starts**

In `StartRecordingAsync()`, right after `_sounds.PlayStart();` (added in Task 4), add:

```csharp
if (_settings.PauseMediaWhileDictating)
    _media.PauseMedia();
```

- [ ] **Step 6: Resume media when recording stops**

In `StopAndTranscribeAsync()`, right after `_sounds.PlayStop();` (added in Task 4), add:

```csharp
if (_settings.PauseMediaWhileDictating)
    _media.ResumeMedia();
```

In `CancelRecordingAsync()`, right after `_sounds.PlayStop();` (added in Task 4), add:

```csharp
if (_settings.PauseMediaWhileDictating)
    _media.ResumeMedia();
```

- [ ] **Step 7: Enable the toggle in SettingsPage.xaml**

In `VoxScript/Views/SettingsPage.xaml`, find the "Pause media while dictating" section (around line 457). Change:

```xml
<Grid Opacity="0.5" ColumnSpacing="32">
```

To:

```xml
<Grid ColumnSpacing="32">
```

And change:

```xml
IsEnabled="False"
```

To:

```xml
IsEnabled="True"
```

- [ ] **Step 8: Build to verify**

Run: `dotnet build VoxScript.slnx`
Expected: Build succeeded.

- [ ] **Step 9: Commit**

```bash
git add VoxScript.Core/Platform/IMediaControlService.cs VoxScript.Native/Platform/MediaControlService.cs VoxScript/Infrastructure/AppBootstrapper.cs VoxScript.Core/Transcription/Core/VoiceInkEngine.cs VoxScript/Views/SettingsPage.xaml
git commit -m "feat: pause/resume media playback during dictation via media key"
```

---

### Task 10: Fix Existing Tests + Run Full Suite

**Files:**
- Modify: Any test files that construct `VoxScriptEngine` directly (need updated constructor args)

- [ ] **Step 1: Search for existing VoxScriptEngine instantiation in tests**

Run: `grep -rn "VoxScriptEngine" VoxScript.Tests/`

If any tests instantiate `VoxScriptEngine`, they need the new `ISoundEffectsService` and `IMediaControlService` parameters added: `Substitute.For<ISoundEffectsService>()` and `Substitute.For<IMediaControlService>()`.

- [ ] **Step 2: Fix any broken tests**

Add the missing constructor arguments wherever `VoxScriptEngine` is instantiated in tests.

- [ ] **Step 3: Run the full test suite**

Run: `dotnet test VoxScript.Tests`
Expected: All tests pass.

- [ ] **Step 4: Commit if any test fixes were needed**

```bash
git add VoxScript.Tests/
git commit -m "fix: update tests for new VoxScriptEngine constructor parameters"
```

---

### Task 11: Enable Dictation Sounds Toggle + Update STATUS.md

**Files:**
- Modify: `VoxScript/Views/SettingsPage.xaml` (if dictation sounds toggle is disabled — check)
- Modify: `STATUS.md`

- [ ] **Step 1: Verify the dictation sounds toggle is already enabled**

Check `SettingsPage.xaml` — the "Dictation sounds" toggle should already be enabled (it was implemented in the Settings page work). If it has `IsEnabled="False"`, change it to `IsEnabled="True"` and remove any `Opacity="0.5"` on the parent Grid.

- [ ] **Step 2: Update STATUS.md**

Mark items 13, 17, and 19 as done:

Item 13 (Custom sounds):
```
13. **Custom sounds** — DONE
    - Three woodblock/click WAVs: start (~800Hz), toggle (~650Hz), stop (~500Hz double-tap)
    - `SoundEffectsService` pre-loads WAVs, plays on state transitions in VoxScriptEngine
    - Toggle sound fires via `ToggleLockActivated` event from GlobalHotkeyService
    - Respects `SoundEffectsEnabled` setting toggle
    - Files: `scripts/generate_sounds.py`, `Assets/Sounds/`, `SoundEffectsService.cs`, `ISoundEffectsService.cs`
```

Item 17 (Pause media):
```
17. **Pause media while dictating** — DONE
    - Sends VK_MEDIA_PLAY_PAUSE via keybd_event on recording start/stop
    - Works with any app that responds to media keys (Spotify, browser, VLC)
    - `MediaControlService` tracks paused state to avoid double-toggle
    - Respects `PauseMediaWhileDictating` setting; toggle now enabled in Settings
    - Files: `IMediaControlService.cs`, `MediaControlService.cs`, `VoiceInkEngine.cs`
```

Item 19 (Paste last):
```
19. **Paste last transcript** — DONE
    - `VoxScriptEngine.LastTranscription` stores post-pipeline result
    - `GlobalHotkeyService.PasteLastRequested` fires on Alt+Shift+Z (configurable)
    - Wired in App.xaml.cs to call `IPasteService.PasteAtCursorAsync`
    - Works regardless of auto-paste setting; silently no-ops if no transcription yet
    - Files: `VoiceInkEngine.cs`, `GlobalHotkeyService.cs`, `App.xaml.cs`
```

- [ ] **Step 3: Commit**

```bash
git add VoxScript/Views/SettingsPage.xaml STATUS.md
git commit -m "docs: mark custom sounds, paste last, and pause media as done"
```

---

### Task 12: Final Build + Test Verification

- [ ] **Step 1: Clean build**

Run: `dotnet build VoxScript.slnx --no-incremental`
Expected: Build succeeded, 0 errors, 0 warnings (or only pre-existing warnings).

- [ ] **Step 2: Run full test suite**

Run: `dotnet test VoxScript.Tests`
Expected: All tests pass.

- [ ] **Step 3: Verify WAV files are in build output**

Run: `ls VoxScript/bin/Debug/net10.0-windows10.0.19041.0/Assets/Sounds/`
Expected: `start.wav  stop.wav  toggle.wav`
