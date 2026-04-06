# Cursor Pasting + Pipeline Error Handling

## Item #1: Wire Cursor Pasting

### Problem
`CursorPasterService` exists with working SendInput logic but is never called. After transcription completes, text is only displayed in the UI — not pasted to the user's active window.

### Design

**Interface in Core** (`VoxScript.Core/Platform/IPasteService.cs`):
```csharp
public interface IPasteService
{
    Task PasteAtCursorAsync(string text, CancellationToken ct);
}
```

**CursorPasterService** implements `IPasteService` (no behavioral changes, just adds the interface).

**AppSettings** gets `AutoPasteEnabled` (default `true`).

**VoxScriptEngine.StopAndTranscribeAsync()** — after pipeline returns text, before firing `TranscriptionCompleted`:
```
if (text != null && _settings.AutoPasteEnabled)
    try paste
    catch → log warning, continue
fire TranscriptionCompleted
```

Paste failure must never prevent the UI from showing the transcript.

**AppBootstrapper** registers `IPasteService → CursorPasterService`.

## Item #4: Pipeline Error Handling

### Problem
If `WordReplacementService` or `ITranscriptionRepository.AddAsync()` throws, the entire pipeline crashes and the user loses their transcript.

### Design

**Degradation strategy in `TranscriptionPipeline.RunAsync()`:**

| Step | On failure | Fallback |
|------|-----------|----------|
| Transcribe | Let throw | Engine catches, fires `TranscriptionFailed` |
| Filter | Catch, log | Use raw text |
| Format | Catch, log | Use previous step's output |
| Word replacement | Catch, log | Use formatted text |
| AI enhancement | Already handled | Returns null on failure |
| Persist | Catch, log | Return text anyway |

Each post-transcription step is wrapped individually so a failure in one doesn't skip subsequent steps. The pipeline always returns the best text it was able to produce.

Logging uses Serilog `Log.Warning()` for skipped steps so failures are observable.
