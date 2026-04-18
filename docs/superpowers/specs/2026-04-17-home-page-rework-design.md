# Home page rework — design

**Date:** 2026-04-17
**Status:** Approved, ready for implementation planning

## Context

The current `TranscribePage` is built around a big record button, a transcript card that updates live, and a hero tagline. Since real-time transcription was dropped (local Whisper + Vulkan GPU is fast enough; streaming adds cost and doesn't play well with the post-processing pipeline), most of those elements no longer earn their screen space. Recording is hotkey-only in practice — users dictate into other apps and rarely look at the home window.

The rework turns the page into a single non-scrolling dashboard: component readiness + usage stats + latest transcript, optimized for glance checks.

## Goals

- Replace `TranscribePage` with a dashboard-style `HomePage`.
- Show at-a-glance readiness for every component in the transcription pipeline (model, AI enhancement, LLM formatting) plus a rolled-up overall state.
- Show lifetime usage stats (total words, avg WPM) and a 12-hour activity graph.
- Show the latest transcript (truncated) with copy + link to history.
- Fit in a single viewport at the app's minimum window size (900×600) — no scrollbars.
- Keep recording entirely hotkey-driven; no record button on this page.

## Non-goals

- Live streaming/real-time transcript display.
- Record/stop controls on this page.
- Editing transcripts inline (history page owns that).
- Per-session or per-day detail views (this is a dashboard, not a report).

## Layout

Two-column grid, fixed to window content area, no scrolling.

```
┌────────────────────────────────┬──────────────┐
│  Latest transcript · 2m ago    │  Status      │
│  [Copy]  [View history ›]      │  ● Ready     │
│                                │              │
│  Lorem ipsum dolor sit amet... │  Components  │
│  (up to 6 lines, ellipsized)   │  ● Model     │
│                                │  ● AI Enh.   │
├────────────────────────────────┤  ● LLM Fmt.  │
│  Last 12 hours                 │              │
│  ▂▃▅▇▆▄▃▂▁▂▃▄                  │  Total words │
│                                │  42,318      │
│                                │              │
│                                │  Avg WPM     │
│                                │  147         │
└────────────────────────────────┴──────────────┘
```

Left column (`*`): transcript card (`flex:1`, dominant height) + 12-hour graph card at the bottom.
Right rail (`240px` fixed): overall status tile → components tile → total words tile → avg WPM tile.

The right rail balances the left navigation pane so the content feels centered. Recording state never alters this page's appearance — the separate recording indicator window already owns live UI.

## File & class structure

### Rename
- `VoxScript/Views/TranscribePage.xaml[.cs]` → `HomePage.xaml[.cs]`.
- Class `TranscribePage` → `HomePage`. Namespace unchanged.
- Update nav registration wherever it currently routes to `TranscribePage` (likely `MainWindow.xaml[.cs]`).

### New types

**`VoxScript.Core` additions:**

```csharp
namespace VoxScript.Core.Home;

public enum StatusLevel { Ready, Warming, Unavailable, Off }

public sealed record StatusResult(StatusLevel Level, string Label);

public interface IHomeStatusService
{
    Task<StatusResult> GetModelStatusAsync(CancellationToken ct);
    Task<StatusResult> GetAiEnhancementStatusAsync(CancellationToken ct);
    Task<StatusResult> GetLlmFormattingStatusAsync(CancellationToken ct);
    StatusResult Rollup(params StatusResult[] components);
}

public interface IHomeStatsService
{
    Task<int> GetTotalWordsAsync(CancellationToken ct);
    Task<double> GetAverageWpmAsync(CancellationToken ct);
    Task<IReadOnlyList<int>> GetHourlyWordBucketsAsync(int hours, CancellationToken ct);
    void InvalidateCache();
}

public static class TextUtil
{
    public static int CountWords(string? text); // whitespace-split, handles nulls/empties
}
```

**`VoxScript.Core` implementations:**
- `HomeStatusService` — depends on `AppSettings`, `HttpClient` (for Ollama pings), `IModelManager` (for `.bin` existence checks), optionally `IStructuralFormattingService` (to reuse its readiness signal if one exists).
- `HomeStatsService` — depends on `ITranscriptionRepository`. In-memory cache for `TotalWords` and `AvgWpm`, invalidated via event subscription or explicit `InvalidateCache()`.

**`VoxScript` (app layer) additions:**
- `HomeViewModel` — CommunityToolkit.Mvvm `ObservableObject`. Observable properties: `OverallStatus`, `ModelStatus`, `AiEnhanceStatus`, `LlmFormatStatus` (all `StatusResult`), `TotalWords` (int), `AvgWpm` (double), `HourlyBuckets` (`IReadOnlyList<int>`), `LatestTranscriptText` (string), `LatestTranscriptTimestamp` (DateTimeOffset?), `HasLatestTranscript` (bool).
- `HomePage.xaml[.cs]` — thin, binds to `HomeViewModel`, subscribes to `VoxScriptEngine.TranscriptionCompleted`, wires Copy button, wires "View history ›" navigation.

### DI wiring
In `AppBootstrapper.cs` — register `IHomeStatusService → HomeStatusService` (singleton), `IHomeStatsService → HomeStatsService` (singleton), `HomeViewModel` (transient).

## Data flow & refresh

**Refresh triggers:**
- `OnNavigatedTo` → full refresh (all statuses + stats + graph + latest transcript). Four status checks run in parallel via `Task.WhenAll`.
- `VoxScriptEngine.TranscriptionCompleted` → incremental refresh: `IHomeStatsService.InvalidateCache()`, re-fetch stats, re-fetch graph, re-fetch latest transcript. Statuses unchanged.
- No timer-based polling. Staleness across long idle periods is acceptable; `OnNavigatedTo` re-checks on every navigation.

**Status signal rules:**

| Component | Ready (green) | Warming (amber) | Unavailable (red) | Off (grey) |
|---|---|---|---|---|
| Model | `.bin` file exists, size > 0 | `ModelDownloadManager` reports active download | File missing, no download running | never (model is mandatory) |
| AI Enhancement | Ollama ping 200 or cloud key set | — | Ollama timeout/4xx or cloud key empty | `settings.AiEnhancementEnabled == false` |
| LLM Formatting | Same as AI Enhancement against its own config | Warmup in flight (first 10s after app start per existing behavior) | Provider unreachable | `settings.LlmFormattingEnabled == false` |

**Rollup logic (`Rollup`):**
- Disabled components (`Off`) are skipped.
- Priority: `Unavailable` > `Warming` > `Ready`.
- If all considered components are `Ready`, overall is `Ready`.
- If every component is `Off` (only possible hypothetically — model is never `Off`), overall is `Ready` by default.

**Ollama ping spec:** `GET {endpoint}/api/tags` with 3s `CancellationTokenSource` timeout. Existing pattern at current `TranscribePage.xaml.cs:140-151`. Reuse verbatim.

## Persistence change: `WordCount` column via EF migrations

The DB currently uses `EnsureCreated` with no migrations. Schema changes aren't applied to existing DBs. For this feature:

1. Add `public int WordCount { get; set; }` to `TranscriptionRecord` (non-nullable, default 0).
2. Populate in `TranscriptionPipeline` right before `repository.AddAsync`, using `TextUtil.CountWords` on the final text (post-replacement, post-enhancement — whichever text is stored).
3. Switch `AppBootstrapper` from `db.Database.EnsureCreated()` to `db.Database.Migrate()`.
4. Run `dotnet ef migrations add InitialCreate` to capture the full current schema plus `WordCount` as one migration.
5. **One-time local cleanup**: delete `%LOCALAPPDATA%\VoxScript\voxscript.db` before the first run on the new code. User is sole consumer and has accepted this wipe scope. Prior to deleting, they can export vocab/corrections/expansions via existing `IDataPortService` and re-import after.

Notes and context modes aren't covered by the export/import service today and will be wiped. If preservation matters, extend `IDataPortService` before shipping this migration.

## Stats computation

- **`TotalWords` and `AvgWpm`:** both derived from one aggregate query. `HomeStatsService` calls `repository.GetAggregateStatsAsync(ct)` → `(int TotalWords, double TotalSeconds)`, caches both values in memory. `AvgWpm = totalSeconds > 0 ? (totalWords / totalSeconds) * 60 : 0`. `HomeViewModel` calls `InvalidateCache()` in its `TranscriptionCompleted` handler before re-reading the values.
- **`HourlyBuckets(12)`:** `repository.GetRangeAsync(now - 12h, now)`, bucket by `CreatedAt.Hour` (local time), return array of 12 ints aligned with hour slots (index 0 = oldest hour, index 11 = current hour). Always re-queried — window is small, caching not worth it.

New repo method on `ITranscriptionRepository`:

```csharp
Task<(int TotalWords, double TotalSeconds)> GetAggregateStatsAsync(CancellationToken ct);
```

Implemented as a single EF `GroupBy`/`Sum` query in `TranscriptionRepository`.

## Word count computation

`TextUtil.CountWords(text)`:
- Null/empty → 0.
- Split on whitespace (`text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries).Length`).
- Punctuation does not split. `"hello, world"` → 2.

This rule is used in both the insert path (`TranscriptionPipeline`) and in tests. Consistent rule ensures cached stats stay in sync with what users see on the latest transcript.

## Edge cases & empty states

- **No transcriptions yet:** latest transcript card shows `"No transcriptions yet — press Ctrl+Win to start."` Stats show `0` / `—`. Graph shows 12 zero-height bars.
- **Model file missing:** overall `Unavailable`. Label includes model name so the user knows what to download.
- **AI Enhancement / LLM Formatting disabled:** row shows `Off` in grey. Doesn't factor into overall.
- **Recording in progress:** home page stays static. Recording indicator window owns live UI. Overall status does not flip during recording.
- **Ollama warming:** during the existing first-10s fire-and-forget warmup, LLM Formatting row = `Warming` (amber). Turns `Ready` on first successful ping, `Unavailable` if warmup fails.
- **Relative timestamp staleness:** `"N min ago"` is computed on refresh only. Acceptable staleness; re-computes on navigation.
- **Window width at 900px (minimum):** left column compresses; transcript `MaxLines=6` stays. Right rail stays 240px. No scrollbars, no overflow.
- **Long transcripts:** displayed truncated via `TextTrimming="CharacterEllipsis"` + `MaxLines="6"`. Copy button copies the full stored text, not the displayed truncation.

## Testing

Unit tests only; no UI automation.

- **`HomeStatusServiceTests`** — cover each component × each level. Mock `AppSettings`, `HttpClient`, `IModelManager`. Verify `Rollup` with full enum matrix including disabled-component skipping.
- **`HomeStatsServiceTests`** — seed in-memory `ITranscriptionRepository` with known records (fixed `WordCount`, `DurationSeconds`, `CreatedAt`). Verify `GetTotalWordsAsync`, `GetAverageWpmAsync`, `GetHourlyWordBucketsAsync(12)` bucket boundaries. Cache invalidation: first call → add record → `InvalidateCache()` → second call reflects the new record.
- **`HomeViewModelTests`** — fake `IHomeStatusService`/`IHomeStatsService`. Simulate `TranscriptionCompleted` → VM's `LatestTranscriptText` and stats update, statuses unchanged. `OnNavigatedTo` path asserts all four statuses are fetched (counter on the fake or `Task.WhenAll` spy).
- **`TextUtilTests`** — `CountWords` cases: null, empty, single word, multiple spaces between words, leading/trailing whitespace, punctuation, newlines.

## Open items for implementation plan

- Exact nav registration file to edit (likely `MainWindow.xaml[.cs]`; confirm during planning).
- Whether `IStructuralFormattingService` exposes a readiness signal we can call, or we duplicate the Ollama ping pattern for LLM Formatting.
- DataPort extension for notes/context modes (optional, only if user wants to preserve them through the DB wipe).
