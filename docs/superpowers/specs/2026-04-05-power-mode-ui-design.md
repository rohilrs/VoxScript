# Power Mode — Context-Aware Enhancement Styles

## Overview

Automatically switch AI enhancement style based on the active app/window. Pre-built modes for common categories (Personal Messages, Work Messages, Email) plus user-created custom modes. Integrates into the existing Personalize page and TranscriptionPipeline.

---

## Pre-built Configs

Seeded on first run if `PowerModeConfigs` table is empty. Users can freely edit/delete after seeding.

| Name | Process Filters | URL Pattern | Default Preset |
|------|----------------|-------------|----------------|
| Personal Messages | WhatsApp, Messenger, Discord, Telegram, Signal | — | Casual |
| Work Messages | Slack, Teams | — | Semi-casual |
| Email | Outlook, Thunderbird | `mail\.google\.com` | Formal |

---

## Personalize Page — Context Modes Section

Added below the existing style preset cards.

### Layout

- **Section header**: "CONTEXT MODES" with description "Automatically switch style based on the active app"
- **Mode cards**: one per PowerModeConfig, showing:
  - Name (left)
  - App badges — small rounded chips for each process filter (e.g. "WhatsApp", "Discord")
  - URL badge if URL pattern is set
  - Preset badge (e.g. "Casual") colored by preset type
  - Enable/disable toggle (right)
  - Entire card clickable to open edit dialog
- **"Add custom mode" button** at bottom of section

### Edit Dialog (ContentDialog)

Same programmatic UI pattern as ModelManagementDialog.

| Field | Type | Notes |
|-------|------|-------|
| Name | Text field | Required |
| Style | Dropdown: Formal / Semi-casual / Casual / Custom | Maps to `EnhancementPreset` |
| System prompt | Multi-line text area | Only visible when Style = Custom |
| Apps | Text field | Comma-separated process names, e.g. `WhatsApp, Discord` |
| URL pattern | Text field | Optional regex, e.g. `mail\.google\.com` |
| Delete / Reset | Button | "Delete" for user-created, "Reset to defaults" for built-in |

---

## TranscribePage Badge

When recording starts or a transcription completes, show a context badge below the model name:

- If a Power Mode matched: show `"{Preset} — {MatchedApp}"` (e.g. "Casual — Discord")
- If no mode matched: badge hidden, global preset applies

The badge is a small `TextBlock` with muted styling, updated via a new `ContextModeDisplay` property on the engine or TranscribePage.

---

## Backend Changes

### PowerModeConfig — add preset field

```csharp
// Existing fields: Id, Name, SystemPrompt, ProcessNameFilter, UrlPatternFilter,
//                  WindowTitleFilter, IsEnabled, Priority

// Add:
public EnhancementPreset Preset { get; set; } = EnhancementPreset.SemiCasual;
```

When `Preset != Custom`, the effective system prompt is composed from `EnhancementPrompts.Compose()` using the preset's fixed features. When `Preset == Custom`, `SystemPrompt` is used directly.

### ProcessNameFilter — change from single string to multi-value

Currently `ProcessNameFilter` is a single string with substring matching. For the UI, each mode needs multiple apps. Change to store comma-separated process names (e.g. `"WhatsApp,Discord,Telegram"`). Update `PowerModeManager.Resolve()` to split on comma and match any.

### Persistence — EF Core + SQLite

Add `PowerModeConfigs` table via `AppDbContext`:

```csharp
public DbSet<PowerModeConfigEntity> PowerModeConfigs { get; set; }
```

Entity maps 1:1 with `PowerModeConfig`. Use `EnsureCreated` (existing pattern, no migrations).

Add `IPowerModeRepository` in Core with CRUD methods. Implement in Native or keep in Core since EF Core is already referenced there.

### Seeding

On app startup (in `App.xaml.cs` after DB init), check if `PowerModeConfigs` table is empty. If so, insert the three built-in configs. Mark them with a `IsBuiltIn` bool field so the UI can show "Reset to defaults" instead of "Delete".

### PowerModeManager — load from DB

Currently `PowerModeManager` holds configs in an in-memory list with `Add`/`Remove`. Change to load from DB on startup and refresh after edits. The `Resolve()` matching logic stays the same.

### Pipeline integration

In `TranscriptionPipeline.RunAsync()`, before the AI enhancement step:

1. Call `PowerModeSessionManager.ResolveCurrentAsync(ct)` to check foreground app
2. If a config matches and has a preset/prompt, use that instead of the global `AppSettings.EnhancementSystemPrompt`
3. If no match, fall back to global prompt (existing behavior)

The pipeline already receives `IAIEnhancementService`. Add `PowerModeSessionManager` as a dependency.

### Effective prompt resolution

```
if PowerModeSessionManager.ResolveCurrentAsync() returns a config:
    if config.Preset == Custom:
        use config.SystemPrompt
    else:
        use EnhancementPrompts.Compose(config.Preset, preset features)
else:
    use AppSettings.EnhancementSystemPrompt (global)
```

---

## Files Changed

| File | Change |
|------|--------|
| `VoxScript.Core/PowerMode/PowerModeConfig.cs` | Add `Preset`, `IsBuiltIn` fields; change `ProcessNameFilter` to comma-separated |
| `VoxScript.Core/PowerMode/PowerModeManager.cs` | Update matching for comma-separated process names; load from DB |
| `VoxScript.Core/PowerMode/IPowerModeRepository.cs` | New — CRUD interface |
| `VoxScript.Core/Persistence/PowerModeConfigEntity.cs` | New — EF entity |
| `VoxScript.Core/Persistence/AppDbContext.cs` | Add `PowerModeConfigs` DbSet |
| `VoxScript.Core/Transcription/Core/TranscriptionPipeline.cs` | Add PowerModeSessionManager dependency; resolve mode before enhancement |
| `VoxScript/Views/PersonalizePage.xaml` + `.cs` | Add Context Modes section with cards |
| `VoxScript/Views/PowerModeEditDialog.cs` | New — edit dialog |
| `VoxScript/ViewModels/PersonalizeViewModel.cs` | Add Power Mode list, CRUD commands |
| `VoxScript/Views/TranscribePage.xaml` + `.cs` | Add context mode badge |
| `VoxScript/Infrastructure/AppBootstrapper.cs` | Register repository |
| `VoxScript/App.xaml.cs` | Seed built-in configs on first run |

## Not in scope

- Window title matching in UI (backend supports it but not worth the UI complexity now)
- Per-mode model/provider override (all modes use the global AI provider from Settings)
- Auto-detection of installed apps
