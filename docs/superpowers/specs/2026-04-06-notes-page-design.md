# Notes Page Design

## Overview

A master-detail Notes page that serves as both a scratchpad for user-created notes and a collection point for starred transcriptions from History. Left panel shows a searchable, sortable list of all notes; right panel provides a rich text editor.

## Layout

Master-detail split view:

- **Left panel** (~300px fixed width): note list with controls
- **Right panel** (flex fill): rich text editor
- Divider: 1px border between panels

### Left Panel — Note List

**Header row:**
- "Notes" title (Georgia serif, consistent with other pages)
- "+ New" button (brand primary) — creates a new empty note and selects it

**Search bar:**
- Text input with search icon placeholder
- 300ms debounce (same pattern as History)
- Searches note titles and plain-text content (strip RTF for matching)

**Sort controls:**
- Pill-style toggle buttons: Newest (default), Oldest, A-Z
- Sorts by `ModifiedAt` for Newest/Oldest, by `Title` for A-Z

**Note cards:**
- Background: `BrandCardBrush`, corner radius 12
- Selected state: highlighted background + left accent border (brand primary)
- Content:
  - Title (11px, semibold, single line truncated)
  - Body preview (9px, muted, 2-line clamp)
  - Footer row: timestamp + type badge + copy button
- Type badges:
  - User-created note: muted text/icon badge
  - Starred transcription: gold star "Saved" badge
- Copy button: small icon button (copy glyph), copies note's plain text to clipboard with checkmark feedback (1.5s, same pattern as History)

**Empty state:**
- Centered message when no notes exist: "No notes yet" + subtitle "Create a note or star a transcription from History"

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

- **Core:** `INoteRepository` interface, `Note` entity
- **Core/Persistence:** `Note` entity added to `AppDbContext`
- **Native or Core:** `NoteRepository` (EF Core, same layer as other repositories)
- **App/ViewModels:** `NotesViewModel` (CommunityToolkit.Mvvm) — manages list state, search, sort, selected note, editor content, auto-save timer
- **App/Views:** `NotesPage.xaml` (master-detail XAML layout) + `NotesPage.xaml.cs` (toolbar wiring, RichEditBox interaction)
- **App/Views:** `HistoryPage` modified to add star button

## Checklist Implementation

RichEditBox does not natively support checklists. Implementation approach:

- Use bullet list formatting as the base
- Checklist items rendered as bullet points with a checkbox character prefix or custom inline UI
- Simplest approach: toggle checklist inserts lines prefixed with `[ ]` or `[x]` in the text, with toolbar button toggling between states
- Alternative: use `RichEditBox` paragraph formatting with a custom bullet character (checkbox unicode). This keeps it within RTF without needing custom rendering.

Decision: use unicode checkbox characters (☐ / ☑) as bullet prefixes within standard RTF. Simple, searchable, no custom rendering needed.

## Interactions

- **Click note card** → selects it, loads content into editor
- **"+ New" button** → creates note with "Untitled" title, empty body, selects it, focuses title field
- **Edit title** → auto-saves on change (debounced)
- **Edit body** → auto-saves on change (debounced 1s)
- **Toolbar buttons** → apply/remove formatting to selection in RichEditBox
- **Copy button on card** → copies plain text to clipboard, checkmark feedback
- **Delete button** → confirmation dialog, removes note, selects adjacent note or shows empty state
- **Star on History card** → creates Note, brief visual feedback on the star icon
- **Search** → filters list in real-time (300ms debounce)
- **Sort pills** → re-orders list immediately

## What's NOT in Scope

- Folders or categories for notes
- Note sharing or export
- Markdown support
- Image or file attachments
- Drag-and-drop reordering
- Undo/redo beyond RichEditBox built-in
