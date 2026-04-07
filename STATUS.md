# VoxScript — Development Status

Last updated: 2026-04-05

## Overview

Windows voice-to-text application built in C# / WinUI 3 / .NET 10, ported from VoiceInk (macOS Swift/SwiftUI). ~120 C# files, 14 XAML files, 57+ passing tests.

**Solution structure:** VoxScript.Core (business logic) → VoxScript.Native (Win32/interop) → VoxScript (WinUI 3 app)

---

## What's Working

### Core Transcription
- [x] Record audio from default microphone (WASAPI via NAudio)
- [x] Local transcription via whisper.cpp (pre-built v1.8.4 DLL, CPU-only)
- [x] Silero VAD integration (strips silence before transcription)
- [x] Auto-download whisper model on first run (default: ggml-base.en)
- [x] Transcript displayed in UI after recording stops
- [x] Copy transcript to clipboard button
- [x] Auto-paste transcription to focused window (Win32 clipboard + keybd_event)
- [x] Transcription pipeline error handling (graceful degradation per step)

### Global Hotkeys
- [x] Low-level keyboard hook (WH_KEYBOARD_LL) with GetModuleHandle
- [x] Ctrl+Win held = push-to-talk (immediate start, release to stop)
- [x] Space during hold = toggle mode (lock recording on, release keys freely)
- [x] Ctrl+Win or Ctrl+Win+Space while toggle-locked = stop
- [x] Deferred stop (200ms) to handle Win+Space OS interception
- [x] Key repeat suppression for Space
- [x] Configurable keybinds via Settings page (recorder-style capture using GetAsyncKeyState)

### UI (WinUI 3 + Mica)
- [x] Custom brand theme (warm palette, Georgia serif headings, Mica backdrop)
- [x] Waveform logo in nav sidebar
- [x] Home/TranscribePage — record button, transcript card, status indicators
- [x] Navigation sidebar: Home, Dictionary, Expansions, History, Personalize, Notes, Settings
- [x] Light theme with visible caption buttons (min/max/close)
- [x] System tray (minimize to tray on close, tray icon with tooltip)
- [x] Minimum window size enforced (900×600)

### Settings Page
- [x] Grouped cards layout (Keybinds, Input, App, Sound, Extras)
- [x] Keybind configuration with inline recorder (GetAsyncKeyState polling for Win key)
- [x] Reset keybinds to defaults
- [x] Microphone selection (auto-detect + enumerated devices)
- [x] Language selection (whisper supported languages)
- [x] Toggle settings: launch at login, minimize to tray, dictation sounds, smart formatting
- [x] Disabled toggles for unimplemented features (recording indicator, pause media, auto-dictionary)
- [x] Auto-save all changes immediately via AppSettings → LocalSettingsStore
- [x] Model management dialog (download/delete/import/use models, accessible from Input card)
- [x] GPU system info logged on model load (Vulkan/AVX2 detection via whisper_print_system_info)

### Expansions Page (formerly Shortcuts)
- [x] Add/edit via centered popup dialog with multi-line "Replace with" field
- [x] Case-sensitive option per expansion
- [x] Sort: newest, oldest, alphabetical
- [x] Edit and delete with confirmation dialogs
- [x] Backed by WordReplacementRepository → SQLite

### Dictionary Page
- [x] Two sections: Vocabulary (custom words) and Corrections (misspelling → fix)
- [x] Add, edit (popup dialog), delete with confirmation
- [x] Sort: newest, oldest, alphabetical (both sections)
- [x] Vocabulary backed by VocabularyRepository, Corrections by CorrectionRepository

### History Page
- [x] Transcriptions grouped by date (Today, Yesterday, or full date)
- [x] Sorted by time within each group (newest first)
- [x] Full text always displayed (no truncation)
- [x] Metadata badges: model name, "✨ Enhanced" for AI-enhanced
- [x] Copy to clipboard with visual feedback (checkmark for 1.5s)
- [x] Delete with confirmation dialog
- [x] Search with 300ms debounce (filters text and enhanced text)
- [x] Pagination: loads past 30 days, "Load more" for older

### Infrastructure
- [x] EF Core + SQLite database (auto-created on startup)
- [x] Settings persistence (JSON file at %LOCALAPPDATA%\VoxScript\settings.json)
- [x] Serilog logging (rolling daily to %LOCALAPPDATA%\VoxScript\Logs\)
- [x] DI container (all services wired in AppBootstrapper)
- [x] 57 unit tests passing (settings, hotkey serialization, transcription filters, word replacements, etc.)

### AI Enhancement + Context Modes
- [x] AI Enhancement service (OpenAI, Anthropic, Ollama backends) with UI config
- [x] Provider selection and API key management in Settings
- [x] Tabbed Personalize page — one tab per context mode (Personal Messages, Work Messages, Email, + custom)
- [x] Each mode: style preset cards, editable app chips (add/remove), URL chips (add/remove), custom prompt
- [x] Enhancement only fires when a context mode matches the active app (no default fallback)
- [x] Auto-detects active app via process name + browser URL matching
- [x] Context badge on TranscribePage showing matched mode + app
- [x] SQLite persistence with first-run seeding of 3 built-in modes
- [x] User can add/delete custom modes, edit all built-in modes

### Backend Services (code exists, not yet exposed in UI)
- [x] Cloud transcription (OpenAI Whisper API, OpenAI-compatible endpoints)
- [x] WebSocket streaming providers (Deepgram, ElevenLabs)
- [x] Transcription output filter (hallucination stripping)
- [x] Text formatter + filler word removal
- [x] Power Mode manager (process name, URL, window title matching)
- [x] Active window detection + browser URL extraction
- [x] Parakeet ONNX Runtime backend (mel spectrogram, CTC decoder)
- [x] Windows Credential Manager for API keys
- [x] Launch at startup (registry-based)
- [x] Model download manager (HuggingFace URLs, progress reporting)

---

## What's Not Working / Outstanding

### High Priority — Core Experience

3. **GPU acceleration (Vulkan)** — DONE
   - Vulkan-enabled whisper.cpp DLLs built and deployed to NativeBinaries/x64/
   - ggml-vulkan.dll auto-discovered at runtime by whisper.cpp backend system
   - `whisper_print_system_info` logged after model load (check Serilog for "Whisper system info: ... VULKAN = 1")
   - Files: `native/whisper/build.ps1`, `WhisperNativeMethods.cs`, `WhisperBackend.cs`

### Medium Priority — Feature Parity with VoiceInk

9. **AI Enhancement + Context Modes** — DONE
   - Settings page: AI Enhancement card (enable toggle, provider dropdown, model name, API keys, Ollama endpoint)
   - Personalize page: tabbed context modes (Personal Messages, Work Messages, Email, + custom)
   - Each mode tab: style preset cards, editable app chips, URL chips, custom prompt (Custom preset only)
   - Enhancement only fires when a context mode matches the active app (no default fallback)
   - AIService refactored to explicit provider routing with configurable model/endpoint
   - TranscriptionPipeline resolves Power Mode before enhancement, context badge on TranscribePage
   - SQLite persistence with first-run seeding of 3 built-in modes

10. ~~**Streaming transcription UI**~~ — DROPPED
    - Local Whisper + Vulkan GPU is fast enough; streaming adds cloud cost/dependency for marginal UX gain
    - Post-processing pipeline (hallucination filter, word replacement, AI enhancement) requires complete transcript
    - Deepgram/ElevenLabs provider code remains if ever needed

13. **Custom sounds** — DONE
    - Four woodblock/click WAVs: start (560Hz), toggle (450Hz), stop (350Hz double-tap), cancel (280Hz short tap)
    - `SoundEffectsService` pre-loads WAVs, plays on state transitions in VoxScriptEngine
    - Toggle sound fires via `ToggleLockActivated` event from GlobalHotkeyService
    - Respects `SoundEffectsEnabled` setting toggle
    - Files: `scripts/generate_sounds.py`, `Assets/Sounds/`, `SoundEffectsService.cs`, `ISoundEffectsService.cs`

### Low Priority — Polish

14. **Models management page** — DONE (dialog-based)
    - Model management dialog accessible from Settings > Input > "Manage models" button
    - Lists predefined models (download/use/delete), shows active model badge
    - Import local .bin files via file picker, download from arbitrary URL
    - Hot-swaps model into WhisperBackend without app restart
    - Files: `ModelManagementDialog.cs`, `ModelManagementViewModel.cs`, `SettingsPage.xaml`, `SettingsViewModel.cs`

15. **Parakeet models** — backend exists but untested
    - ONNX Runtime + DirectML, mel spectrogram, tokenizer stub
    - Need to verify model export from NeMo and end-to-end inference

16. **Recording indicator bar** — DONE
    - Floating dark pill at bottom center of screen, fully transparent background (no window chrome)
    - Settings dropdown: Off / Always visible / Only during recording (default)
    - Recording state: pulsing red dot, animated waveform (noise-gated, sqrt loudness curve), elapsed timer
    - Hold mode: Cancel (X) button; Toggle mode: adds Finish button
    - Transcribing state: purple spinner; Pasted state: green checkmark → 1s linger → fade out
    - Win32: WS_POPUP + DwmExtendFrameIntoClientArea + TransparentTintBackdrop for true transparency
    - Files: `RecordingIndicatorWindow.xaml/.cs`, `RecordingIndicatorViewModel.cs`, `RecordingIndicatorMode.cs`

17. **Pause media while dictating** — DONE
    - Sends WM_APPCOMMAND(APPCOMMAND_MEDIA_PLAY_PAUSE) to shell window on recording start/stop
    - Routes through system media transport pipeline (same as physical media keys); works with Spotify, browser, VLC
    - `MediaControlService` tracks paused state to avoid double-toggle
    - Respects `PauseMediaWhileDictating` setting; toggle enabled in Settings
    - Files: `IMediaControlService.cs`, `MediaControlService.cs`, `VoiceInkEngine.cs`

18. **Auto-add to dictionary** — DONE
    - After each transcription, extracts uncommon words and adds them to vocabulary
    - Filters against ~10K common English word list (HashSet, case-insensitive)
    - Skips single characters, pure numbers, and words already in vocabulary
    - New pipeline step after word replacement, gated by `AutoAddToDictionary` setting
    - Files: `ICommonWordList.cs`, `CommonWordList.cs`, `IAutoVocabularyService.cs`, `AutoVocabularyService.cs`, `common-words.txt`

19. **Paste last transcript** — DONE
    - `VoxScriptEngine.LastTranscription` stores post-pipeline result
    - `GlobalHotkeyService.PasteLastRequested` fires on Alt+Shift+Z (configurable)
    - Wired in App.xaml.cs to call `IPasteService.PasteAtCursorAsync`
    - Works regardless of auto-paste setting; silently no-ops if no transcription yet
    - Files: `VoiceInkEngine.cs`, `GlobalHotkeyService.cs`, `App.xaml.cs`

20. **Notes page** — DONE
    - Two surfaces: list view in main window Notes tab + separate editor window (NoteEditorWindow)
    - Master-detail layout in editor: sidebar note list + RichEditBox with formatting toolbar (B/I/U/bullet/number/checklist)
    - Auto-save with 1s debounce, search with 300ms debounce, sort (newest/oldest/A-Z)
    - Star button on History cards saves transcriptions to Notes as "Saved" items
    - Singleton editor window pattern (reused on subsequent opens)
    - NoteRecord entity in SQLite via EF Core, INoteRepository with full CRUD
    - Files: `NoteRecord.cs`, `INoteRepository.cs`, `NoteRepository.cs`, `NotesViewModel.cs`, `NotesPage.xaml/.cs`, `NoteEditorWindow.xaml/.cs`

21. **Onboarding flow** — no first-run wizard

22. **Import/export** — dictionary, expansions, corrections, and history

23. **Screen/clipboard context capture** for AI enhancement

---

## File Layout

```
VoxScript.slnx
├── VoxScript.Core/          (business logic, no platform refs)
│   ├── Audio/               IAudioCaptureService, AudioFormat, AudioDeviceInfo
│   ├── Transcription/
│   │   ├── Core/            VoxScriptEngine, TranscriptionPipeline, sessions, registry
│   │   ├── Models/          ITranscriptionModel, PredefinedModels, ModelProvider
│   │   ├── Batch/           LocalTranscriptionService, CloudTranscriptionService
│   │   ├── Streaming/       Deepgram, ElevenLabs providers, StreamingTranscriptionService
│   │   └── Processing/      OutputFilter, TextFormatter, WordReplacement, FillerWords
│   ├── AI/                  AIEnhancementService, AIService, PromptDetection
│   ├── PowerMode/           PowerModeConfig, Manager, SessionManager
│   ├── Settings/            ISettingsStore, AppSettings, ApiKeyManager
│   ��── Dictionary/          Vocabulary, WordReplacement, Correction repositories
│   ├─�� History/             ITranscriptionRepository, TranscriptionRepository
│   ├── Persistence/         EF Core entities, AppDbContext
│   ├── Platform/            IPasteService
��   └── Common/              Result<T>
├── VoxScript.Native/        (Win32 interop, P/Invoke)
│   ├── Whisper/             WhisperBackend, NativeMethods, ModelManager, SileroVAD
│   ├── Parakeet/            ParakeetBackend, MelSpectrogram, WordAgreementEngine
│   ├── Audio/               WasapiCaptureService, AudioFormatConverter
│   ├── Platform/            GlobalHotkeyService, CursorPasterService, ActiveWindowService
│   └── Storage/             LocalSettingsStore
├��─ VoxScript/               (WinUI 3 app)
│   ├��─ Views/               TranscribePage, SettingsPage, ExpansionsPage, DictionaryPage, HistoryPage + placeholders
│   ├── ViewModels/          SettingsViewModel, ExpansionsViewModel, DictionaryViewModel, HistoryViewModel
│   ├── Helpers/             HotkeySerializer, DialogHelper
│   ├── Styles/              AppColors.xaml (brand theme)
│   ├── Shell/               MainWindow, SystemTrayManager
│   ├── Infrastructure/      AppBootstrapper, ServiceLocator, AppLogger, StartupRegistration
│   └── Converters/          NullToVisibility, InvertedBoolToVisibility
├── VoxScript.Tests/         57 tests (xUnit + FluentAssertions + NSubstitute)
└── native/whisper/          build.ps1 + whisper.cpp source
```

## How to Run

```powershell
# From the solution root (E:\Documents\VoiceInk-Windows):
dotnet run --project VoxScript
```

First launch downloads ggml-base.en (~142MB) automatically. whisper.dll (pre-built v1.8.4) is included in NativeBinaries/x64/.

## Key Technical Decisions

- **whisper.cpp via pointer-based API** — uses `whisper_full_default_params_by_ref` to avoid struct marshaling ABI issues
- **Silero VAD** — ONNX Runtime inference strips silence before whisper, reducing hallucinations
- **JSON settings** — `LocalSettingsStore` uses a JSON file instead of `ApplicationData` (app runs unpackaged)
- **ILocalTranscriptionBackend** — Core-level interface that WhisperBackend implements, avoiding Core→Native dependency
- **Default model: ggml-base.en** — fast enough for CPU; switch to large-v3-turbo once Vulkan build is enabled
- **Win32 clipboard + keybd_event** — WinRT Clipboard requires foreground; keybd_event avoids UIPI blocks that SendInput hits
- **GetAsyncKeyState for keybind recording** — WinUI KeyDown doesn't see Win key (OS intercepts Win+Space for input switching)
- **Deferred hold-stop (200ms)** — Windows sends Win keyup before Space keydown due to Win+Space interception; timer allows Space to still convert hold to toggle
