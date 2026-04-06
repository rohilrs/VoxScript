# GPU Info Logging + Model Management Dialog

## Part 1: GPU/System Info Logging

Add `whisper_print_system_info` P/Invoke to `WhisperNativeMethods.cs`. This whisper.cpp API returns a `const char*` describing compiled-in features (AVX2, Vulkan, NEON, etc.).

Call it once in `WhisperBackend.LoadModelAsync` after successful `whisper_init_from_file`, log the result via Serilog at Information level. Example output: `"Whisper system info: AVX2 = 1 | VULKAN = 1 | COREML = 0 | ..."`.

### Files changed
- `VoxScript.Native/Whisper/WhisperNativeMethods.cs` — add DllImport
- `VoxScript.Native/Whisper/WhisperBackend.cs` — log after model load

---

## Part 2: Model Management

### Settings Card
Add a row to the Input card (or a small standalone card) in `SettingsPage.xaml` showing the currently active model name and a "Manage models" button that opens the dialog.

### Model Management Dialog

A `ContentDialog` with the project's existing visual style (brand theme, card brushes). Contents:

**Model list** — each predefined model from `PredefinedModels.All` shown as a row:
- Display name + file size (from `TranscriptionModel.EstimatedSizeBytes`)
- Status: "Active" badge (currently loaded), "Downloaded" (on disk but not active), or download button with progress bar
- "Use" button to set as active (downloads first if needed)
- "Delete" button (confirmation required, disabled for active model)

**Custom model section** at the bottom:
- "Import local file" button — opens a file picker for `.bin` files, copies into models directory
- "Download from URL" — text input + download button, downloads with progress into models directory
- Custom models appear in the list alongside predefined ones

### Selecting a model
Clicking "Use" on any model (predefined or custom):
1. Saves name to `AppSettings.SelectedModelName`
2. Hot-loads into `WhisperBackend` via `LoadModelAsync`
3. Updates the "Active" badge in the dialog and the display on the Settings card

### Backend changes

**`WhisperModelManager`** — add:
- `DeleteModel(string name)` — deletes the `.bin` file
- `DownloadFromUrlAsync(string url, string name, IProgress<double>?, CancellationToken)` — downloads arbitrary URL into models dir
- `ImportModel(string sourcePath)` — copies a local `.bin` into models dir, returns the model name

**`PredefinedModels`** — no changes; custom models are identified by being in the models dir but not matching any predefined name.

### New files
- `VoxScript/ViewModels/ModelManagementViewModel.cs` — ObservableCollection<ModelDisplayItem>, download/delete/import/use commands
- `VoxScript/Views/ModelManagementDialog.xaml` + `.cs` — the ContentDialog UI

### UI pattern
Follows the existing popup dialog pattern used by Expansions and Dictionary pages. Programmatic row building in code-behind (consistent with the rest of the app — avoids x:Bind/x:DataType compilation issues noted in CLAUDE.md).
