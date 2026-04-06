# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Project Overview

VoxScript is a Windows voice-to-text application (WinUI 3 / .NET 10 / C#), ported from VoiceInk (macOS Swift/SwiftUI). It uses whisper.cpp for local transcription with Silero VAD for silence stripping.

## Commands

```powershell
# Build the solution
dotnet build VoxScript.slnx

# Run the app (first launch downloads ggml-base.en model ~142MB)
dotnet run --project VoxScript

# Run all tests (xUnit + FluentAssertions + NSubstitute)
dotnet test VoxScript.Tests

# Run a single test by name
dotnet test VoxScript.Tests --filter "FullyQualifiedName~TestClassName.TestMethodName"

# Build whisper.dll with Vulkan GPU support (PowerShell, requires VS Build Tools + Vulkan SDK)
.\native\whisper\build.ps1 -Vulkan
```

## Architecture

**Solution has four projects with a strict dependency flow:**

```
VoxScript.Core  (business logic, no platform refs, net10.0)
       ↑
VoxScript.Native  (Win32/P-Invoke, NAudio, ONNX Runtime, net10.0-windows)
       ↑
VoxScript  (WinUI 3 app, net10.0-windows)

VoxScript.Tests  (references Core + Native)
```

**Core cannot reference Native.** The boundary is maintained via interfaces defined in Core, implemented in Native (e.g., `ILocalTranscriptionBackend` → `WhisperBackend`, `IPasteService` → `CursorPasterService`). All wiring happens in `VoxScript/Infrastructure/AppBootstrapper.cs` (DI container).

### Transcription Pipeline

The main data flow is in `VoxScript.Core/Transcription/Core/TranscriptionPipeline.cs`:

`Record audio (WASAPI) → Silero VAD → whisper.cpp transcribe → filter hallucinations → format → word replacement → AI enhancement (optional) → save to DB → auto-paste to focused window`

Each post-transcription step is individually wrapped in try-catch for graceful degradation.

### Key Subsystems

- **Whisper interop** (`VoxScript.Native/Whisper/`): Uses pointer-based API (`whisper_full_default_params_by_ref`) to avoid struct marshaling ABI issues. Pre-built DLL in `VoxScript/NativeBinaries/x64/`.
- **Global hotkeys** (`VoxScript.Native/Platform/GlobalHotkeyService.cs`): WH_KEYBOARD_LL hook with deferred stop (200ms) to handle Win+Space OS interception. Ctrl+Win = push-to-talk, Space converts to toggle mode.
- **Paste** (`VoxScript.Native/Platform/CursorPasterService.cs`): Win32 clipboard API + `keybd_event` (not `SendInput`, which is blocked by UIPI).
- **Settings**: JSON file at `%LOCALAPPDATA%\VoxScript\settings.json` (app runs unpackaged). `AppSettings` wraps `ISettingsStore` with typed properties.
- **Database**: EF Core + SQLite at `%LOCALAPPDATA%\VoxScript\voxscript.db`, auto-created via `EnsureCreated` (no migrations).
- **Logging**: Serilog rolling daily to `%LOCALAPPDATA%\VoxScript\Logs/`.
- **API keys**: Windows Credential Manager via `WindowsCredentialService`.

### UI Layer

WinUI 3 with custom brand theme (`VoxScript/Styles/AppColors.xaml`), Mica backdrop. MVVM pattern: ViewModels in `VoxScript/ViewModels/` use CommunityToolkit.Mvvm. Pages build UI rows programmatically (code-behind) rather than XAML DataTemplates to avoid x:Bind/x:DataType compilation issues.

**Navigation sidebar:** Home, Dictionary, Expansions, History, Personalize, Notes, Settings.

**Implemented pages:** TranscribePage, SettingsPage (5 grouped cards), ExpansionsPage (word replacements with popup add/edit), DictionaryPage (vocabulary + corrections), HistoryPage (date-grouped transcriptions with search).

### Win32 Gotchas

- **Win key interception**: Windows consumes Win+Space for input-language switching. The keyboard hook sees Win keyup BEFORE Space keydown. Mitigated with deferred stops and `GetAsyncKeyState` polling.
- **UIPI**: `SendInput` returns 0 for unpackaged apps injecting into other processes. Use `keybd_event` instead.
- **WinRT Clipboard**: Requires foreground window. Use Win32 `OpenClipboard`/`SetClipboardData` with `GlobalAlloc(GMEM_MOVEABLE)` for background clipboard access.

## Development Status

See `STATUS.md` for detailed tracking. Key remaining gaps: GPU acceleration (Vulkan build script ready, needs VS Build Tools), AI Enhancement UI, streaming transcription UI, Power Mode UI, recording indicator bar.
