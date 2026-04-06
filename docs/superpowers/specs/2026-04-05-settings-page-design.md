# Settings Page Design

## Overview

Settings page for VoxScript using grouped cards layout (Win11 style). Single scrollable page with 5 cards, max-width 700px centered. Auto-saves all changes immediately via `AppSettings` → `LocalSettingsStore`.

## Layout

Grouped cards on a scrollable page. Each card has an icon header, contains related settings with one-line descriptions beneath each label. Unimplemented features shown at 50% opacity with non-interactive toggles.

## Cards

### 1. Keybinds (⌨️)

| Binding | Description | Default |
|---------|-------------|---------|
| Hold to talk | Hold keys to record, release to stop | Ctrl+Win |
| Toggle talk | Press to start, press again to stop | Ctrl+Win+Space |
| Paste last transcript | Paste your most recent dictation | Alt+Shift+Z |
| Cancel recording | Stop without transcribing | Esc |

**Inline recorder interaction:**
- Click a key badge → badge turns BrandPrimary with white "Press keys..." text
- Other keybind rows dim to 40% opacity
- Hint bar appears at bottom of card: "Press desired key combination · Esc to cancel · Backspace to clear"
- Pressing a valid combo saves immediately and exits recording mode
- Esc reverts to the previous binding
- Backspace clears the binding (sets to "Not set")
- The keybind recorder temporarily unregisters the global hotkey hook to avoid triggering recording while capturing

**Storage:** Each keybind stored as a string in AppSettings (e.g., "Ctrl+Win+Space"). Parsed on load, serialized on save.

### 2. Input (🎙️)

| Setting | Control | Description |
|---------|---------|-------------|
| Microphone | ComboBox | Audio input device for recording |
| Language | ComboBox | Transcription language |

- Microphone dropdown: first item "System default (auto-detect)", then enumerated devices from `WasapiCaptureService`
- Language dropdown: whisper supported languages. Default: English.

### 3. App (⚙️)

| Setting | Default | Status | Description |
|---------|---------|--------|-------------|
| Launch at login | On | Implemented | Start VoxScript when you sign in |
| Recording indicator | Off | **Not implemented** | Show floating bar when dictating |
| Minimize to tray | On | Implemented | Keep running in system tray when closed |

### 4. Sound (🔊)

| Setting | Default | Status | Description |
|---------|---------|--------|-------------|
| Dictation sounds | On | Implemented (toggle exists, no audio files yet) | Play audio cues when recording starts and stops |
| Pause media while dictating | Off | **Not implemented** | Mute other audio during recording |

### 5. Extras (✨)

| Setting | Default | Status | Description |
|---------|---------|--------|-------------|
| Auto-add to dictionary | Off | **Not implemented** | Learn frequently used words automatically |
| Smart formatting | On | Implemented | Auto-format punctuation and capitalization |

## New AppSettings Properties

```
LaunchAtLogin           bool   default true
MinimizeToTray          bool   default true
SmartFormattingEnabled  bool   default true
RecordingIndicatorEnabled bool default false  (disabled in UI)
PauseMediaWhileDictating bool  default false  (disabled in UI)
AutoAddToDictionary     bool   default false  (disabled in UI)
HoldHotkey              string default "Ctrl+Win"
ToggleHotkey            string default "Ctrl+Win+Space"
PasteLastHotkey         string default "Alt+Shift+Z"
CancelHotkey            string default "Esc"
```

## Architecture

- **SettingsPage.xaml** — XAML layout with 5 card Borders, ToggleSwitches, ComboBoxes, keybind buttons
- **SettingsViewModel.cs** — new ViewModel (CommunityToolkit.Mvvm ObservableObject), binds to AppSettings, handles keybind recording state, audio device enumeration
- **AppSettings.cs** — add new properties listed above
- **GlobalHotkeyService** — `Unregister()`/`Register()` called during keybind recording to prevent hotkey conflicts. After keybind change, re-register with new combos.
- **HotkeySerializer** — small static utility to parse "Ctrl+Win+Space" ↔ `HotkeyCombo` (ModifierKeys + trigger key)

## Keybind Recording Flow

1. User clicks key badge → ViewModel sets `IsRecordingKeybind = true`, `RecordingSlot = "ToggleHotkey"`
2. ViewModel calls `GlobalHotkeyService.Unregister()` to prevent hook interference
3. Page installs a temporary `KeyDown` handler on the Page itself (WinUI routed event)
4. On KeyDown: build combo from pressed modifiers + trigger key, display live in badge
5. On KeyUp (if all modifiers released): save combo, exit recording mode, call `GlobalHotkeyService.Register()`
6. On Escape: revert, exit recording mode, re-register hook

## Disabled Features

Settings that don't have backend implementations are rendered at 50% opacity. Their ToggleSwitches have `IsEnabled="False"`. No tooltip or "coming soon" text — the dimmed appearance is sufficient.

## What This Spec Does NOT Cover

- Implementation of recording indicator bar, media pause, auto-dictionary, or paste-last-transcript backends
- The Shortcuts page (separate spec — will display the same keybinds in a read-only reference view)
- AI Enhancement settings (separate Personalize page)
