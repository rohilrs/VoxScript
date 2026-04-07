# Import/Export — Design Spec

**Date:** 2026-04-06
**Scope:** Vocabulary, Corrections, Expansions

## Overview

Single JSON file export/import for user-curated dictionary data. Triggered from Settings > Extras card. Import skips duplicates (add-only).

## JSON Format

```json
{
  "version": 1,
  "exportedAt": "2026-04-06T12:00:00Z",
  "vocabulary": ["Rohil", "VoxScript", "whisper"],
  "corrections": [
    { "wrong": "teh", "correct": "the" }
  ],
  "expansions": [
    { "original": "brb", "replacement": "be right back", "caseSensitive": false }
  ]
}
```

- `version`: integer, currently `1`. For future schema changes.
- `exportedAt`: ISO 8601 UTC timestamp.
- `vocabulary`: string array of words (entity only has `Word` field).
- `corrections`: array of `{ wrong, correct }` objects.
- `expansions`: array of `{ original, replacement, caseSensitive }` objects.
- Empty sections are valid (empty arrays).

## UI

New row in **Settings > Extras card** with two buttons: **Export** and **Import**.

### Export Flow

1. User clicks Export.
2. Windows Save File dialog opens (filter: `*.json`).
3. All three collections are read from repositories and serialized.
4. File is written.
5. Success InfoBar: "Exported 12 vocabulary words, 5 corrections, 8 expansions."

### Import Flow

1. User clicks Import.
2. Windows Open File dialog opens (filter: `*.json`).
3. File is deserialized and validated (`version` field must be present and `1`).
4. Each item is checked against existing data; duplicates are skipped.
5. New items are added to their respective repositories.
6. Success InfoBar: "Imported 3 new vocabulary words, 1 correction, 0 expansions (9 skipped)."

### Duplicate Detection

- **Vocabulary:** same word, case-insensitive.
- **Corrections:** same `Wrong` value, case-insensitive.
- **Expansions:** same `Original` value, case-insensitive.

## Architecture

### Service Layer (VoxScript.Core)

**`IDataPortService`** interface:

```csharp
Task<ExportResult> ExportAsync(Stream output, CancellationToken ct);
Task<ImportResult> ImportAsync(Stream input, CancellationToken ct);
```

**`DataPortService`** implementation:

- Depends on `IVocabularyRepository`, `ICorrectionRepository`, `IWordReplacementRepository`.
- Uses `System.Text.Json` with camelCase property naming.
- `ExportResult`: counts per data type.
- `ImportResult`: added counts and skipped counts per data type.

### ViewModel (VoxScript)

**`SettingsViewModel`** gets two new `IAsyncRelayCommand`s: `ExportDataCommand`, `ImportDataCommand`.

- Commands call `IDataPortService` with a `Stream` provided by the view.
- Return result objects that the view uses to display InfoBars.

### View (SettingsPage.xaml.cs)

- File picker logic lives in code-behind (needs window handle for `FileSavePicker`/`FileOpenPicker`).
- Opens picker, gets file stream, passes to ViewModel command.
- Displays success/error InfoBar based on result.

### DI Registration

- `IDataPortService` → `DataPortService` registered in `AppBootstrapper.cs`.

## Error Handling

- Malformed JSON or missing/wrong `version`: error InfoBar "Invalid file format."
- File I/O errors: error InfoBar with message.
- Partial data (some sections missing): import what's present, skip missing sections.

## Out of Scope

- History, Notes, PowerMode config export.
- CSV or other formats.
- Merge/overwrite strategies (import is add-only).
