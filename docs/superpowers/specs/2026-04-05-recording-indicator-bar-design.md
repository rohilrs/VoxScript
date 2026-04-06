# Recording Indicator Bar — Design Spec

## Overview

A floating always-on-top dark translucent pill that provides visual feedback during the recording → transcription → paste lifecycle. Appears at the bottom center of the screen, adapts its controls based on recording mode (hold vs toggle), and auto-dismisses after paste completes.

## Settings

The existing disabled "Recording indicator" toggle in Settings > App card becomes a dropdown with two options:

- **Always visible** — pill is shown at all times while the app is running; shows an idle/ready state when not recording, active states during recording lifecycle
- **Only during recording** — pill appears on recording start, dismisses after paste

Stored in `AppSettings.RecordingIndicatorMode` as an enum: `Off` (default), `AlwaysVisible`, `DuringRecording`.

The existing `RecordingIndicatorEnabled` bool property is replaced by this enum.

## Visual Design

### Pill Container

- Background: `rgba(30, 30, 30, 0.92)` with backdrop blur
- Border radius: 24px
- Border: 1px solid, color varies by state (see States table)
- Shadow: `0 4px 20px rgba(0,0,0,0.4)`
- Padding: 10px 24px (wider on toggle mode to accommodate Finish button)
- Horizontal layout: elements separated by 14px gap

### Position

- Bottom center of primary monitor
- ~40px margin from screen bottom edge
- Horizontally centered

### States & Lifecycle

| State | Border Color | Left Content | Center | Right Content |
|-------|-------------|-------------|--------|---------------|
| **Idle (always-visible only)** | `rgba(255,255,255,0.1)` neutral | VoxScript icon or mic icon (dimmed) | "Ready" text | None |
| **Recording (hold)** | `rgba(204,68,68,0.4)` red | Pulsing red dot (10px) | Waveform bars + elapsed timer | Cancel (X) button |
| **Recording (toggle)** | `rgba(204,68,68,0.4)` red | Pulsing red dot (10px) | Waveform bars + elapsed timer | Finish button + Cancel (X) button |
| **Transcribing** | `rgba(125,132,178,0.4)` purple | Spinning loader (18px) | "Transcribing..." text | None |
| **Pasted** | `rgba(51,153,102,0.4)` green | Green checkmark | "Pasted" text | None |

### Waveform

- 5-7 vertical bars, 3px wide, 2px gap, rounded ends
- Color: `#CC4444` (BrandRecording)
- Heights driven by real-time audio amplitude (RMS from WASAPI capture, normalized 0-1)
- Smooth interpolation between amplitude samples
- Falls to flat line (~4px height) on silence

### Pulsing Dot

- 10px diameter circle, `#CC4444` fill
- `box-shadow: 0 0 8px #CC4444` glow
- Opacity pulses between 1.0 and 0.4 on a 1.5s cycle

### Timer

- Font: Segoe UI, 13px, weight 500, color `#e0e0e0`
- Format: `M:SS` (no leading zero on minutes)
- Updates every second

### Buttons

**Cancel (X):** 28px square, 8px border radius, `rgba(255,255,255,0.08)` background, `#aaa` X icon. Hover: background lightens to `rgba(255,255,255,0.15)`.

**Finish (toggle mode only):** Pill-shaped (14px border radius), `rgba(51,153,102,0.25)` background with `rgba(51,153,102,0.5)` border, `#5bbd8a` text, Segoe UI 12px weight 600. Hover: background brightens.

Separator: 1px wide, 20px tall, `rgba(255,255,255,0.15)`, between timer and buttons.

### Dismiss Animation

After "Pasted" state: 1 second linger → 300ms opacity fade to 0 → window hidden.

## Window Implementation

### WinUI 3 Secondary Window

A new `RecordingIndicatorWindow` class — a second `Window` instance (not ContentDialog, which is modal).

### Win32 Properties (via P/Invoke)

- **Always on top:** `SetWindowPos` with `HWND_TOPMOST`
- **No taskbar entry:** `WS_EX_TOOLWINDOW` extended window style
- **Borderless:** `ExtendsContentIntoTitleBar = true`, no title bar chrome
- **Transparent background:** Transparent/no-brush background with Win32 layered window attributes for the transparent pill effect
- **Click-through on transparent regions:** `WS_EX_TRANSPARENT` on non-interactive areas, or handle hit-testing so clicks on the transparent margin pass through to the app behind

### Positioning

- Query primary monitor work area via `MonitorFromWindow` + `GetMonitorInfo`
- Center horizontally: `x = (workArea.Width - pillWidth) / 2`
- Bottom with margin: `y = workArea.Bottom - pillHeight - 40`
- Reposition on display change events

## Data Flow

### Audio Amplitude

`VoxScriptEngine` exposes a new observable property:

```csharp
[ObservableProperty]
private float _audioLevel; // 0.0 to 1.0, updated during recording
```

Sourced from WASAPI capture: compute RMS of each audio buffer, normalize, and push to the property. The indicator window subscribes and maps the value to waveform bar heights.

### State Transitions

1. **Recording starts** → `VoxScriptEngine.State` changes to `Recording` → indicator window appears (if setting enabled), starts waveform + timer
2. **Hold mode: keys released** → `StopAndTranscribeAsync` called → state becomes `Transcribing` → pill swaps to spinner
3. **Toggle mode: Finish clicked** → same as above
4. **Cancel clicked** → `CancelRecordingAsync` called → pill dismisses immediately (no "Pasted" state)
5. **Transcription + paste complete** → state becomes `Idle` + paste event fires → pill shows "Pasted" → 1s linger → 300ms fade → hidden

### Hold vs Toggle Detection

The indicator window needs to know whether the current recording session is hold or toggle mode to decide whether to show the Finish button. `VoxScriptEngine` or `GlobalHotkeyService` exposes the current recording mode.

## Integration Points

### Files to Create

- `VoxScript/Shell/RecordingIndicatorWindow.xaml` + `.xaml.cs` — the window and its XAML layout
- `VoxScript/ViewModels/RecordingIndicatorViewModel.cs` — binds to engine state, audio level, timer, mode

### Files to Modify

- `VoxScript.Core/Transcription/Core/VoiceInkEngine.cs` — add `AudioLevel` property, expose recording mode (hold/toggle)
- `VoxScript.Core/Settings/AppSettings.cs` — replace `RecordingIndicatorEnabled` bool with `RecordingIndicatorMode` enum
- `VoxScript/ViewModels/SettingsViewModel.cs` — update binding for new enum
- `VoxScript/Views/SettingsPage.xaml` — enable toggle, change to dropdown for mode selection
- `VoxScript/App.xaml.cs` — create and manage `RecordingIndicatorWindow` lifecycle
- `VoxScript/Infrastructure/AppBootstrapper.cs` — register new ViewModel if needed
- `VoxScript/Styles/AppColors.xaml` — no changes needed (BrandRecording, BrandPrimary, BrandSuccess already exist)

### Dependencies

- Win32 interop for `SetWindowPos`, `SetWindowLongPtr` (window styles) — add to `VoxScript.Native` or inline in the window class since it's UI-layer Win32
- No new NuGet packages required
- WinUIEx already available for window extensions
