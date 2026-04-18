# Onboarding Flow — Design

**Date:** 2026-04-18
**Status:** Approved, ready for implementation plan

## Overview

Currently, VoxScript's first-run experience is silent: the app opens, the main window shows the (mostly empty) Home page, and `ggml-base.en` (~142 MB) downloads in the background with no UI. The user has no idea hotkeys exist, no way to test their mic, and no confirmation the app works until they stumble onto the hotkey combo themselves.

This design introduces a **blocking, one-shot, full-window-takeover wizard** that runs on first launch. Six steps walk the user from welcome to a successful first dictation in about a minute. Once complete, it never appears again.

Scope is deliberately minimal. AI Enhancement, Context Modes, launch-at-login, sounds, and all other customization remain in Settings; the final wizard step points the user there.

## Goals

- New users successfully dictate their first clip within ~60 seconds of first launch.
- Users understand the three core hotkeys before leaving the wizard.
- Model download is visible (progress + size), not silent.
- Implementation surface is minimal — one new UserControl with six step views, no new windows, no new navigation infrastructure.

## Non-goals

- Configuring AI Enhancement, Context Modes, or Power Modes during onboarding.
- Hotkey rebinding during onboarding (deferred to Settings → Keybinds).
- Re-entry into onboarding after completion (no "Run setup again" button; if users want to, they can manually flip the flag in `settings.json`).
- Tutorial-style coach marks on Home/Settings/History pages after wizard completes.
- Telemetry on completion rates.

## User-facing flow

The wizard is a full takeover of `MainWindow`'s content area — no `ContentDialog`, no separate `Window`. A 3px progress bar at the top and a "Step N of 6" subtitle run across all steps. Each step has Back and Next (or equivalent CTAs) in a footer row.

### Step 1 · Welcome

- Serif wordmark "VoxScript" + italic tagline "Local, private voice-to-text for Windows."
- One line: "Let's get you set up in about a minute."
- 4-bullet preview of upcoming steps:
  - 🎙️ Pick your mic and test the level
  - 💾 Download a transcription model (~142 MB)
  - ⌨️ Learn your dictation shortcuts
  - ✨ Record your first clip
- Single CTA: **Get started**. No Back button.

### Step 2 · Mic pick

- Title "Pick your microphone", subtitle "Speak normally — the bar should move into the green."
- Dropdown of enumerated input devices (default selection matches the system default).
- Live gradient level meter below the dropdown.
- "✓ Signal detected" chip appears once average level exceeds the noise floor continuously for ~2s.
- **Next gating:** disabled until signal is detected.
- Edge case: 8s of no signal → adds "Speak into your mic — we need to hear you to continue" hint + "Skip check" link that bypasses the gate while still persisting the selected device.
- Edge case: zero devices enumerated → empty state with Retry button.
- On Next: writes `AppSettings.AudioDeviceId`.

### Step 3 · Model pick

Three sub-states rendered in-place:

**Picker.** Title "Pick a transcription model". Three radio cards:
- **Fast** — `ggml-tiny.en` ~75 MB — "Quickest download, works on any machine. Good for short phrases."
- **Balanced (RECOMMENDED, pre-selected)** — `ggml-base.en` ~142 MB — "Solid accuracy on any hardware. The best default for most users."
- **Accurate** — `ggml-large-v3-turbo` ~1.5 GB — "Highest accuracy. Best with a GPU (Vulkan is bundled)."

Footnote: "Want something different? You can import your own model or pick from the full list later in Settings → Manage models."
CTA: **Download & continue**.

**Downloading.** Title "Downloading {Fast|Balanced|Accurate} model". Progress panel shows model filename, source ("Hugging Face · ~{size}"), MB-of-MB counter, percentage, bar. Silero VAD downloads silently in parallel. Back cancels the in-flight download via `CancellationTokenSource`, deletes the partial file, returns to Picker. Next disabled.

**Done.** Green panel "✓ {ModelName} ready · loaded into whisper.cpp". Lingers ~800 ms, then Next is enabled. User clicks Next to advance (no auto-advance).

**Failed.** Red panel with the exception reason (short, human-readable) + **Retry** (returns to Downloading) and **Pick a different model** (returns to Picker). VAD download failure is logged but does not block the step.

On Done: writes `AppSettings.SelectedModelName`; model file is on disk; VAD file on disk; both are loaded into `WhisperBackend`.

### Step 4 · Hotkeys

- Title "Your dictation shortcuts".
- Subtitle "These work globally — hold, speak, release. You can rebind them in Settings → Keybinds."
- Three-row table of defaults:
  - `Ctrl + Win` — **Hold to dictate** · release to stop and insert text
  - `Ctrl + Win + Space` — **Toggle on/off** · press again to finish, for longer dictations
  - `Esc` — **Cancel** · throw away the current recording
- CTA: **Got it**.

Read-only. No rebinding UI.

### Step 5 · Try it

Four sub-states rendered in-place:

**Idle.** Title "Let's try it". Subtitle "Hold `Ctrl+Win` and say something — like 'hello from VoxScript'. Release to stop." Status line "Waiting for you to press the hotkey…" Next disabled. **Skip for now** escape link (only on this step).

**Recording.** Title "Recording…", subtitle "Release `Ctrl+Win` when you're done." Pulsing red dot + live waveform + elapsed timer. The app's floating recording indicator bar **also** shows (for visual consistency with the post-onboarding experience).

**Transcribing.** Title "Transcribing…", subtitle "Running on your machine — no data leaves your device." Spinner.

**Success.** Title "You're dictating!", subtitle "Here's what we heard. Hold the hotkey again to try another clip, or continue." Transcript rendered in a serif box. **Try again** link resets to Idle. Next enabled.

**Empty transcript case:** loops back to Idle with inline hint "Didn't catch anything — try again."

**Auto-paste is suppressed for this step only** — the transcript renders in the wizard instead of pasting into the modal. **No DB writes** for try-it clips.

### Step 6 · You're set

- Title "You're all set".
- Subtitle "Try dictating anywhere — transcripts get pasted right where your cursor is. A few things you might want to set up next:"
- Three non-interactive feature teaser cards:
  - ✨ **AI Enhancement** — "Clean up filler words, adjust tone, or reformat with an LLM."
  - 🎯 **Context modes** — "Different tones for Slack vs Email vs personal messages."
  - 📖 **Dictionary & Expansions** — "Custom vocabulary, corrections, and text shortcuts."
- Single CTA: **Finish** — closes the wizard and reveals Home.

On Finish: writes `AppSettings.OnboardingCompleted = true` (last), raises the completion event, `MainWindow` swaps its shell content from `OnboardingView` to the normal `NavigationView` shell.

## Navigation state machine

Top-level, in `OnboardingViewModel`:

```
Welcome → MicPick → ModelPick → Hotkeys → TryIt → Final → (completed)
  ←       ←          ←         ←        ←       ←
        (Back)    (Back)     (Back)   (Back)  (Back)
```

Back is enabled on every step except Welcome. Within ModelPick's Downloading sub-state, Back cancels + returns to Picker.

**Next gating per step:**

| Step | Next enabled when |
|------|-------------------|
| Welcome | Always (single CTA) |
| MicPick | Level meter shows signal above noise floor for ~2 s (or "Skip check" used) |
| ModelPick | Inner sub-state is Done |
| Hotkeys | Always (single CTA) |
| TryIt | Inner sub-state is Success (or "Skip for now" used) |
| Final | Always (single CTA) |

## Persisted state

- **`AppSettings.OnboardingCompleted`** (nullable bool, default "unknown" = key absent) — new setting. Persisted `true` on wizard Finish, or `true`/`false` by the launch-time migration for existing installs.
- **`AppSettings.AudioDeviceId`** — written on MicPick Next. Existing setting.
- **`AppSettings.SelectedModelName`** — written on ModelPick Done. Existing setting.
- **Model file** — on disk under `%LOCALAPPDATA%\VoxScript\Models\...`. Existing mechanism.
- **Silero VAD file** — on disk, same dir. Existing mechanism.

TryIt clips are never persisted (no entry in `TranscriptionRepository`).

**Crash / force-quit recovery:** Because `OnboardingCompleted` is only persisted `true` on Finish, any crash mid-wizard leaves the flag unwritten or (if the launch migration has already resolved it for a fresh install) `false`. The user re-enters at Welcome on next launch. Any completed side-effects (mic pick, downloaded model) are preserved, so the re-run clicks through quickly — not a full restart. Note: the launch-time migration's "user has models on disk → treat as existing user" check must run **before** the wizard starts, since the wizard itself will download a model and would otherwise self-graduate users mid-flow.

## Architecture

### Trigger logic

`App.OnLaunched` currently unconditionally calls `EnsureDefaultModelAsync` after the main window activates. Under this design:

1. After DB migration + Power Mode seeding, resolve the onboarding state (see migration below) into a `shouldShowOnboarding` bool.
2. If `false` (already onboarded): show the existing `NavigationView` shell and call `EnsureDefaultModelAsync` as today (covers the VAD re-check path and any user who deletes all models and relaunches).
3. If `true` (fresh install): show `OnboardingView` in the same `MainWindow` content area. The wizard itself owns model downloading for new installs; `EnsureDefaultModelAsync` does not run.

**Migration for existing users.** `AppSettings.OnboardingCompleted` is a new setting and will be absent from existing users' `settings.json`. On read, the getter treats a missing key as "unknown" (not `false`). `App.OnLaunched` then runs a one-time migration:

- If the key is present, use its value.
- If the key is absent **and** `WhisperModelManager.ListDownloaded().Count > 0`: the user has used the app before; persist `OnboardingCompleted = true` and skip the wizard.
- If the key is absent **and** no models are downloaded: treat as a fresh install; persist `OnboardingCompleted = false` and show the wizard.

`MainWindow` gains a single content-presenter property that flips between `OnboardingView` and the normal shell. The global hotkey service, tray manager, indicator window, and all other runtime subsystems initialize as they do today — the wizard relies on `GlobalHotkeyService` being live for the try-it step.

### New code

```
VoxScript/Onboarding/
  OnboardingView.xaml                 — UserControl, hosts the step views
  OnboardingView.xaml.cs              — binds OnboardingViewModel, swaps step views
  OnboardingViewModel.cs              — top-level state machine (current step, gating, completion)
  Steps/
    WelcomeStepView.xaml(.cs)
    MicStepView.xaml(.cs)
    MicStepViewModel.cs               — device enumeration, level-meter consumption, gating
    ModelStepView.xaml(.cs)
    ModelStepViewModel.cs             — picker/downloading/done/failed sub-state machine
    HotkeysStepView.xaml(.cs)
    TryItStepView.xaml(.cs)
    TryItStepViewModel.cs             — idle/recording/transcribing/success sub-state machine
    FinalStepView.xaml(.cs)
  Controls/
    StepHeader.xaml(.cs)              — progress bar + "Step N of 6" label
    LevelMeter.xaml(.cs)              — gradient bar bound to an audio-sample stream
```

### Core additions

- `AppSettings.OnboardingCompleted` — new nullable-bool property (default "unknown" = key absent; the migration in Trigger logic resolves this on first launch after update).
- No new interfaces in Core expected. The wizard VM wires directly to existing services: `WhisperModelManager`, `IAudioCaptureService`, `AppSettings`, `VoxScriptEngine`, `ILocalTranscriptionBackend`.

### Reused subsystems (no changes)

- `IAudioCaptureService` — already exposes a frame-sample callback suitable for feeding the level meter.
- `WhisperModelManager.DownloadAsync` — already accepts `IProgress<double>` and `CancellationToken`; the wizard re-uses this directly.
- `GlobalHotkeyService` — remains always-on during the wizard; its `RecordingStart/Stop/Cancel` events drive the try-it step.
- `VoxScriptEngine` — used as-is for the try-it recording.
- Recording indicator window — stays live during the wizard (visible during try-it Recording state, per design).

### Auto-paste suppression

`VoxScriptEngine.StartRecordingAsync` (or the session it creates) gains an optional `bool suppressAutoPaste` parameter. The transcription pipeline checks this at the paste step and skips when set. The try-it step VM passes `true` on entry; all other recordings default to `false` (preserving today's behavior).

Parameter is preferred over a transient engine flag — cleaner lifecycle, no risk of a stuck flag if the VM is disposed mid-recording.

## Error handling & edge cases

**Mic step.**
- No input devices enumerated → empty-state "No microphone detected. Plug one in and click Retry." with Retry re-enumeration.
- Selected device fails to open → banner "Couldn't open this microphone. Try another device." Meter shows dead; user picks another.
- Very quiet room / no signal after 8 s → hint "Speak into your mic — we need to hear you to continue" + "Skip check" link (applies the selected device and advances).

**Model step.**
- Download failure (network, 404, HTTP error, disk full) → red panel with the exception message + Retry (re-enters Downloading) + "Pick a different model" (returns to Picker).
- Back during Downloading → cancels the `CancellationTokenSource`, deletes partial file, returns to Picker.
- Model file exists but corrupt (detected on `backend.LoadModelAsync`) → same red panel with "Retry download" (deletes + re-downloads).
- VAD download failure → logged via Serilog, does not block the step. Transcription will work without VAD, matching existing `EnsureVadModelAsync` behavior.

**Try-it step.**
- Empty transcript → loops back to Idle with "Didn't catch anything — try again."
- Whisper load/inference failure → red panel with exception + "Try again" button. "Skip for now" link remains.
- Hotkey never pressed → no timeout; "Skip for now" is the only escape.
- Focus switches to another app → global hotkey still works; transcript renders in the wizard on return.

**Wizard-wide.**
- Process crash / force-quit mid-wizard → `OnboardingCompleted` stays `false`; user re-enters at Welcome next launch. Completed side-effects (device pick, downloaded model) persist, making the re-run fast.
- Window narrower than minimum → `MainWindow`'s existing 975 px minimum width already covers the wizard's ~600 px content width.

## Testing strategy

### Unit tests (VoxScript.Tests, xUnit + FluentAssertions + NSubstitute)

- **`OnboardingViewModelTests`**
  - Step navigation Next/Back transitions between all 6 steps, respects gating.
  - Welcome has no Back; Final "Finish" writes `OnboardingCompleted = true` and raises completion event.
  - Completion flag is written last (mid-wizard settings writes do not set it).
- **`MicStepViewModelTests`**
  - Device enumeration populates the dropdown.
  - Signal-detection gating: mocked samples below noise floor → Next disabled; above for 2 s → Next enabled.
  - 8 s-no-signal secondary hint appears; "Skip check" bypasses gate, still writes `AudioDeviceId`.
  - Empty device list shows retry state.
- **`ModelStepViewModelTests`**
  - Picker → Downloading on CTA click.
  - `IProgress<double>` updates drive progress state.
  - Back during download cancels via CT and returns to Picker.
  - Download failure → Failed; Retry → Downloading; "Pick different" → Picker.
  - Success writes `SelectedModelName` and advances to Done.
  - VAD download failure is logged but does not block.
- **`TryItStepViewModelTests`**
  - State transitions Idle → Recording → Transcribing → Success.
  - Empty transcript loops back to Idle with hint.
  - Auto-paste is suppressed (verify `suppressAutoPaste: true` on engine call).
  - "Try again" resets to Idle; "Skip for now" completes the step.
  - No transcription DB writes during try-it (mock repository, verify zero calls).

### Integration tests

- `WhisperModelManager` with a stubbed HTTP client + temp directory: full download, cancel, resume, corrupted-file handling.

### Manual verification

- Fresh profile (no `%LOCALAPPDATA%\VoxScript` directory): full happy path Welcome → Finish, land on Home, relaunch shows no wizard.
- Force-quit at each step, relaunch verifies re-entry at Welcome.
- `OnboardingCompleted` in `settings.json` flipped back to `false` (or removed, with no models on disk) successfully re-triggers the wizard (for dev/QA).
- Existing-install migration: on a profile with models already downloaded and no `OnboardingCompleted` key, launching writes `OnboardingCompleted = true` and the wizard does not appear.

### Not tested

- Real WASAPI hardware — `IAudioCaptureService` is mocked at the boundary.
- Real `whisper.dll` inference in unit tests — `ILocalTranscriptionBackend` is mocked.
- Real network requests in unit tests — HTTP client is stubbed.
