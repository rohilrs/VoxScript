# Notes Page Design

## Overview

A Notes feature with two surfaces: a list view embedded in the main window's Notes tab, and a separate editor window with a master-detail layout. The list view acts as a launcher/overview; clicking a note or creating a new one opens the editor window where all editing happens.

## Surface 1: Notes Tab (Main Window)

The Notes tab in the sidebar navigation shows a full-width list of all notes.

**Header row:**
- "Notes" title (Georgia serif, consistent with other pages)
- "+ New" button (brand primary) — creates a new note and opens the editor window with it selected

**Search bar:**
- Text input with search icon placeholder
- 300ms debounce (same pattern as History)
- Searches note titles and plain-text content

**Sort controls:**
- Pill-style toggle buttons: Newest (default), Oldest, A-Z
- Sorts by `ModifiedAt` for Newest/Oldest, by `Title` for A-Z

**Note cards:**
- Background: `BrandCardBrush`, corner radius 12
- Content:
  - Title (semibold, single line truncated)
  - Body preview (muted, 2-line clamp)
  - Footer row: timestamp + type badge + copy button
- Type badges:
  - User-created note: muted text/icon badge
  - Starred transcription: gold star "Saved" badge
- Copy button: small icon button (copy glyph), copies note's plain text to clipboard with checkmark feedback (1.5s, same pattern as History)
- **Click card** → opens the editor window with that note selected

**Empty state:**
- Centered message: "No notes yet" + subtitle "Create a note or star a transcription from History"

## Surface 2: Editor Window (Separate Window)

A standalone window with master-detail layout. Opened when clicking a note card or "+ New" from the main window's Notes tab. Can navigate between notes independently without returning to the main window.

### Window Properties
- Separate `Window` instance (similar pattern to `RecordingIndicatorWindow`)
- Title: "Notes — VoxScript"
- Min size: ~800×500
- Standard window chrome (min/max/close)
- Mica backdrop (consistent with main window)
- Singleton: if already open, bring to front and navigate to the requested note

### Left Panel — Note List (~300px fixed width)

Same data as the main window list but in a compact sidebar format:

- **Search bar** (debounced)
- **Sort controls** (Newest / Oldest / A-Z)
- **"+ New" button**
- **Note cards** (compact):
  - Title (11px, semibold, single line truncated)
  - Body preview (9px, muted, 2-line clamp)
  - Footer: timestamp + type badge + copy button
  - Selected state: highlighted background + left accent border (brand primary)

### Right Panel — Editor

**Title bar:**
- Editable `TextBox` (Georgia serif, 20px, transparent background)
- Delete button (trash icon) on the right — shows confirmation dialog

**Metadata row:**
- "Created: {date}" and "Modified: {date}" in muted text
- Format: "Today, 2:30 PM" / "Yesterday, 4:15 PM" / "Apr 3, 11:00 AM"

**Formatting toolbar:**
- Row of icon buttons (32x32 hit targets, 16px FontIcon glyphs from Segoe Fluent Icons)
- Buttons: Bold | Italic | Underline | separator | Bullet list | Numbered list | Checklist
- Active formatting state shown with highlighted background on the button
- Toolbar sits between two 1px borders (top and bottom)

**Editor area:**
- WinUI 3 `RichEditBox` filling remaining space
- Content stored as RTF via `Document.GetText(TextGetOptions.FormatRtf)` / `Document.SetText(TextSetOptions.FormatRtf)`
- Auto-save: debounced save (1s after last edit) to SQLite
- Auto-save indicator in footer: "Saved" with checkmark

**Empty editor state (no note selected):**
- Centered muted text: "Select a note or create a new one"

## Starring from History

**History page change:**
- Add a star icon button to each History card (next to copy/delete buttons)
- Clicking star creates a new Note from the transcription:
  - Title: first ~50 characters of transcription text (or "Saved transcription" if too short)
  - Body: full transcription text (as plain text in RichEditBox)
  - `SourceTranscriptionId`: links back to the original transcription record
  - `IsStarred = true` (marks it as a saved transcription vs user-created note)
- Star icon fills/highlights after saving (visual feedback)
- Original transcription remains in History unchanged

## Data Model

New `Note` entity in `AppDbContext`:

```
Note
├── Id: int (PK, auto-increment)
├── Title: string (max 200)
├── ContentRtf: string (RTF content from RichEditBox)
├── ContentPlainText: string (plain text mirror for search)
├── IsStarred: bool (true = saved from History, false = user-created)
├── SourceTranscriptionId: int? (nullable FK to transcription, for starred notes)
├── CreatedAt: DateTime (UTC)
└── ModifiedAt: DateTime (UTC)
```

**Repository:** `INoteRepository` in Core, `NoteRepository` implementation using EF Core.

Methods:
- `GetAllAsync()` — returns all notes ordered by ModifiedAt desc
- `GetByIdAsync(int id)`
- `SearchAsync(string query)` — searches Title and ContentPlainText
- `CreateAsync(Note note)`
- `UpdateAsync(Note note)` — updates content + ModifiedAt
- `DeleteAsync(int id)`

## Architecture

Follows existing patterns:

- **Core:** `INoteRepository` interface, `NoteRecord` entity in `VoxScript.Core/Notes/`
- **Core/Persistence:** `NoteRecord` added to `AppDbContext`
- **Core:** `NoteRepository` (EF Core, same layer as other repositories)
- **App/ViewModels:** `NotesViewModel` (CommunityToolkit.Mvvm) — shared between both surfaces, manages list state, search, sort, selected note, editor content, auto-save timer
- **App/Views:** `NotesPage.xaml` / `.cs` — main window list view (replaces placeholder)
- **App/Shell:** `NoteEditorWindow.xaml` / `.cs` — separate editor window with master-detail layout
- **App/Views:** `HistoryPage` modified to add star button
- **App/Infrastructure:** `AppBootstrapper` registers repository; window creation wired in `NotesPage` or `App.xaml.cs`

### Window Lifecycle
- `NoteEditorWindow` is created on first open, reused after that (singleton pattern)
- If the window is already open and user clicks a different note, bring window to front and navigate to that note
- Closing the editor window hides it (not destroyed), similar to how system tray works
- Both surfaces share the same `NotesViewModel` instance so list state stays in sync

## Checklist Implementation

RichEditBox does not natively support checklists. Implementation approach:

- Use unicode checkbox characters (☐ / ☑) as bullet prefixes within standard RTF
- Toolbar checklist button inserts lines prefixed with ☐
- Toggling a checklist item swaps ☐ ↔ ☑
- Simple, searchable, no custom rendering needed

## Interactions

### Main Window Notes Tab
- **Click note card** → opens editor window with that note selected
- **"+ New" button** → creates note, opens editor window with it selected and title focused
- **Copy button on card** → copies plain text to clipboard, checkmark feedback
- **Search** → filters list in real-time (300ms debounce)
- **Sort pills** → re-orders list immediately

### Editor Window
- **Click note in sidebar** → loads it in the editor
- **"+ New" button** → creates note, selects it, focuses title
- **Edit title** → auto-saves on change (debounced)
- **Edit body** → auto-saves on change (debounced 1s)
- **Toolbar buttons** → apply/remove formatting to selection in RichEditBox
- **Copy button on sidebar card** → copies plain text to clipboard, checkmark feedback
- **Delete button** → confirmation dialog, removes note, selects adjacent note or shows empty state
- **Close window** → hides window (note already auto-saved)

### History Page
- **Star on History card** → creates Note, brief visual feedback on the star icon

## Synchronization

Both the main window Notes tab and the editor window sidebar show the same list. They share one `NotesViewModel` instance so:
- Creating a note in either surface updates both lists
- Deleting a note in the editor window removes it from the main window list
- Editing a note updates the preview text in both lists
- The main window list does NOT show a selected state (selection only matters in the editor window sidebar)

## What's NOT in Scope

- Folders or categories for notes
- Note sharing or export
- Markdown support
- Image or file attachments
- Drag-and-drop reordering
- Undo/redo beyond RichEditBox built-in
