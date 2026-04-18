# Smart Formatting Implementation Plan

**Spec:** `docs/superpowers/specs/2026-04-07-smart-formatting-design.md`

**Goal:** Transform raw whisper output into properly formatted text via a chain of deterministic, rule-based transforms. Gated by existing `SmartFormattingEnabled` setting. When off, only basic cleanup runs (paragraph breaks + whitespace normalization + sentence capitalization).

**Architecture:**
- `WhisperBackend` returns `TranscriptionSegment[]` instead of `string` (carries timestamps for paragraph detection)
- `LocalTranscriptionService` joins segments with `\n\n` at gaps ≥ 2.5s, returns `string` (no downstream signature changes)
- New `SmartTextFormatter` replaces `WhisperTextFormatter` in the pipeline
- Each transform is a method inside `SmartTextFormatter` (not separate classes — these are regex passes, not independent components)
- Pipeline step 3 becomes: always run basic cleanup; if `SmartFormattingEnabled`, also run the full transform chain

**Tech:** C# 13 / .NET 10, `GeneratedRegex` for all patterns, xUnit + FluentAssertions for tests.

---

## Task 1: TranscriptionSegment + Backend Changes

**Goal:** Make whisper return segment-level timestamps so we can detect pauses for paragraph breaks.

**Files:**
- Create: `VoxScript.Core/Transcription/Core/TranscriptionSegment.cs`
- Modify: `VoxScript.Core/Transcription/Core/ILocalTranscriptionBackend.cs`
- Modify: `VoxScript.Native/Whisper/IWhisperBackend.cs`
- Modify: `VoxScript.Native/Whisper/WhisperNativeMethods.cs`
- Modify: `VoxScript.Native/Whisper/WhisperBackend.cs`
- Modify: `VoxScript.Native/Parakeet/ParakeetBackend.cs`
- Modify: `VoxScript.Core/Transcription/Batch/LocalTranscriptionService.cs`
- Modify: `VoxScript.Core/Transcription/Batch/ParakeetTranscriptionService.cs`
- Create: `VoxScript.Tests/Transcription/ParagraphBreakTests.cs`

### Steps

- [ ] **1.1: Create TranscriptionSegment record**

```csharp
// VoxScript.Core/Transcription/Core/TranscriptionSegment.cs
namespace VoxScript.Core.Transcription.Core;

/// <summary>A single whisper output segment with timing information.</summary>
public sealed record TranscriptionSegment(string Text, long StartMs, long EndMs);
```

- [ ] **1.2: Update ILocalTranscriptionBackend return type**

Change `TranscribeAsync` return from `Task<string>` to `Task<TranscriptionSegment[]>`:

```csharp
// ILocalTranscriptionBackend.cs
Task<TranscriptionSegment[]> TranscribeAsync(float[] samples, string? language,
    string? initialPrompt, CancellationToken ct);
```

Same change in `IWhisperBackend.cs`.

- [ ] **1.3: Add timestamp P/Invoke methods to WhisperNativeMethods**

```csharp
[DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
internal static extern long whisper_full_get_segment_t0(IntPtr ctx, int i_segment);

[DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
internal static extern long whisper_full_get_segment_t1(IntPtr ctx, int i_segment);
```

Also need to set `NoTimestamps` to **0** (false) in `WhisperBackend.RunInference` — timestamps are currently disabled. Without them, `t0`/`t1` return 0 for all segments.

- [ ] **1.4: Update WhisperBackend.RunInference to return segments**

Change return type to `TranscriptionSegment[]`. In the segment loop:

```csharp
// Remove: Marshal.WriteByte(pParams, ParamOffsets.NoTimestamps, 1);
// (Leave it at default 0 = timestamps enabled)

var segments = new TranscriptionSegment[nSegments];
for (int i = 0; i < nSegments; i++)
{
    IntPtr ptr = WhisperNativeMethods.whisper_full_get_segment_text(_ctx, i);
    var text = Marshal.PtrToStringUTF8(ptr) ?? "";
    long t0 = WhisperNativeMethods.whisper_full_get_segment_t0(_ctx, i) * 10; // centiseconds → ms
    long t1 = WhisperNativeMethods.whisper_full_get_segment_t1(_ctx, i) * 10;
    segments[i] = new TranscriptionSegment(text, t0, t1);
}
return segments;
```

Note: whisper timestamps are in centiseconds (1/100s), multiply by 10 for milliseconds.

- [ ] **1.5: Update ParakeetBackend to return single segment**

Parakeet has no timestamp info. Wrap result in a single segment spanning 0..0:

```csharp
// In ParakeetBackend.TranscribeAsync:
var text = /* existing decode logic */;
return [new TranscriptionSegment(text, 0, 0)];
```

- [ ] **1.6: Update LocalTranscriptionService — join segments with paragraph breaks**

```csharp
public async Task<string> TranscribeAsync(string audioPath, ITranscriptionModel model,
    string? language, CancellationToken ct)
{
    var samples = await Task.Run(() => ReadWavAsFloat(audioPath), ct);

    string? initialPrompt = null;
    try
    {
        var words = await _vocabulary.GetWordsAsync(ct);
        if (words.Count > 0)
            initialPrompt = string.Join(", ", words);
    }
    catch { }

    var segments = await _backend.TranscribeAsync(samples, language, initialPrompt, ct);
    return JoinWithParagraphBreaks(segments);
}

private const int ParagraphGapMs = 2500;

private static string JoinWithParagraphBreaks(TranscriptionSegment[] segments)
{
    if (segments.Length == 0) return string.Empty;

    var sb = new StringBuilder();
    sb.Append(segments[0].Text);

    for (int i = 1; i < segments.Length; i++)
    {
        long gap = segments[i].StartMs - segments[i - 1].EndMs;
        sb.Append(gap >= ParagraphGapMs ? "\n\n" : " ");
        sb.Append(segments[i].Text);
    }

    return sb.ToString().Trim();
}
```

- [ ] **1.7: Update ParakeetTranscriptionService similarly**

```csharp
var segments = await _backend.TranscribeAsync(samples, language: null, initialPrompt: null, ct);
return segments.Length > 0 ? segments[0].Text.Trim() : string.Empty;
```

(Single segment, no paragraph logic needed.)

- [ ] **1.8: Write paragraph break tests**

```csharp
// VoxScript.Tests/Transcription/ParagraphBreakTests.cs
public class ParagraphBreakTests
{
    [Fact]
    public void Segments_with_large_gap_get_paragraph_break() { ... }

    [Fact]
    public void Segments_with_small_gap_joined_with_space() { ... }

    [Fact]
    public void Single_segment_returns_trimmed_text() { ... }

    [Fact]
    public void Empty_segments_returns_empty() { ... }
}
```

Note: `JoinWithParagraphBreaks` should be `internal static` so tests can call it directly.

- [ ] **1.9: Build + run full test suite**

Existing tests that mock `ILocalTranscriptionBackend` will need their mock return types updated from `string` to `TranscriptionSegment[]`. Fix any compilation errors.

- [ ] **1.10: Commit**

---

## Task 2: SmartTextFormatter — Spoken Punctuation + Basic Cleanup

**Goal:** Create the formatter that replaces `WhisperTextFormatter`. Start with spoken punctuation (the most impactful transform) and basic cleanup (the always-on foundation).

**Files:**
- Create: `VoxScript.Core/Transcription/Processing/SmartTextFormatter.cs`
- Create: `VoxScript.Tests/Transcription/SmartTextFormatterTests.cs`

### Steps

- [ ] **2.1: Write tests for spoken punctuation**

```csharp
[Theory]
[InlineData("hello comma how are you", "Hello, how are you")]
[InlineData("stop period next sentence", "Stop. Next sentence")]
[InlineData("is it done question mark", "Is it done?")]
[InlineData("wow exclamation point that is great", "Wow! That is great")]
[InlineData("items colon eggs and milk", "Items: eggs and milk")]
[InlineData("first semicolon second", "First; second")]
[InlineData("line one new line line two", "Line one\nLine two")]
[InlineData("paragraph one new paragraph paragraph two", "Paragraph one\n\nParagraph two")]
[InlineData("hello COMMA world", "Hello, world")]  // case insensitive
public void SpokenPunctuation_is_replaced(string input, string expected)
```

- [ ] **2.2: Write tests for basic cleanup**

```csharp
[Theory]
[InlineData("  hello   world  ", "Hello world")]  // collapse spaces, capitalize, trim
[InlineData("hello. world", "Hello. World")]  // sentence boundary capitalization
[InlineData("done! next", "Done! Next")]
[InlineData("what? yes", "What? Yes")]
[InlineData("line one\n\nline two", "Line one\n\nLine two")]  // preserves paragraph breaks
[InlineData("hello ,world", "Hello, world")]  // space before punct removed, space after added
public void BasicCleanup_formats_correctly(string input, string expected)
```

- [ ] **2.3: Implement SmartTextFormatter**

```csharp
public sealed partial class SmartTextFormatter
{
    public string Format(string text, bool smartFormattingEnabled)
    {
        if (string.IsNullOrWhiteSpace(text)) return string.Empty;

        if (smartFormattingEnabled)
        {
            text = ApplySpokenPunctuation(text);
            // Future transforms inserted here (Tasks 3-6)
        }

        text = ApplyBasicCleanup(text);
        return text;
    }
}
```

**Spoken punctuation rules:**
- Match whole words, case-insensitive
- "new paragraph" → `\n\n`, "new line" → `\n` (multi-word, process first)
- "comma" → `,` / "period" / "full stop" → `.` / "question mark" → `?` / "exclamation point" / "exclamation mark" → `!` / "colon" → `:` / "semicolon" → `;`
- Remove surrounding whitespace, insert punctuation attached to previous word + space after
- Capitalize next word after sentence-ending punctuation (`. ? !`)

**Basic cleanup** (always runs, replaces WhisperTextFormatter):
- Collapse multiple spaces to single (but NOT `\n` — use `[^\S\n]+` instead of `\s+`)
- Remove space before punctuation (`, . ? ! : ;`)
- Ensure space after punctuation when followed by a letter
- Capitalize first char of each sentence (after `. ? ! \n`)
- Trim each line + overall string

- [ ] **2.4: Run tests**

- [ ] **2.5: Commit**

---

## Task 3: Number Conversion

**Goal:** Convert spoken number words to digits. Cardinals, ordinals, hundreds, thousands, millions.

**Files:**
- Modify: `VoxScript.Core/Transcription/Processing/SmartTextFormatter.cs`
- Modify: `VoxScript.Tests/Transcription/SmartTextFormatterTests.cs`

### Steps

- [ ] **3.1: Write tests**

```csharp
// Cardinals
[InlineData("I have zero apples", "I have 0 apples")]
[InlineData("twenty three people", "23 people")]
[InlineData("one hundred and fifty", "150")]
[InlineData("two thousand twenty six", "2026")]
[InlineData("a hundred dollars", "100 dollars")]  // "a" = 1
[InlineData("three million", "3000000")]

// Ordinals
[InlineData("the first time", "the 1st time")]
[InlineData("second place", "2nd place")]
[InlineData("third row", "3rd row")]
[InlineData("twenty first birthday", "21st birthday")]

// No false positives
[InlineData("anyone can do it", "anyone can do it")]
[InlineData("I am the one", "I am the one")]  // standalone "one" in non-numeric context
[InlineData("one of the best", "one of the best")]  // exclusion phrase
```

- [ ] **3.2: Implement number conversion**

Processing order matters — greedy consumption of longest number word sequence:

1. Compound ordinals first ("twenty first" → "21st")
2. Simple ordinals ("first" → "1st")
3. Large number sequences ("one hundred and fifty" → "150", "two thousand twenty six" → "2026")
4. Compound cardinals ("thirty five" → "35")
5. Standalone tens ("twenty" → "20")
6. Standalone units/teens ("three" → "3")

For large numbers, use a greedy token scanner:
- Consume tokens: units/teens, tens, "hundred", "thousand", "million", "billion", "and"
- Accumulate: `current + (multiplier)` pattern
- Example: "two thousand twenty six" → 2×1000 + 20 + 6 = 2026

Exclusion list for standalone small numbers: "one of", "one thing", "for one", "a one", "the one". When "one" appears in these patterns, leave it as a word.

Insert this step in `Format()` after spoken punctuation, before basic cleanup.

- [ ] **3.3: Run tests**

- [ ] **3.4: Commit**

---

## Task 4: List Detection

**Goal:** Detect numbered item sequences and format as newline-separated lists.

**Files:**
- Modify: `VoxScript.Core/Transcription/Processing/SmartTextFormatter.cs`
- Modify: `VoxScript.Tests/Transcription/SmartTextFormatterTests.cs`

### Steps

- [ ] **4.1: Write tests**

```csharp
[Fact]
public void NumberedList_is_formatted()
{
    // After number conversion, input has digits
    _sut.Format("1 eggs 2 milk 3 oranges", true)
        .Should().Be("1. Eggs\n2. Milk\n3. Oranges");
}

[Fact]
public void List_requires_at_least_three_items()
{
    // "2 people" should NOT become a list
    _sut.Format("I saw 2 people", true)
        .Should().Be("I saw 2 people");
}

[Fact]
public void List_must_start_from_one()
{
    _sut.Format("items 3 first 4 second 5 third", true)
        .Should().NotContain("\n");  // not sequential from 1
}
```

- [ ] **4.2: Implement list detection**

Runs after number conversion (so numbers are already digits). Pattern: find sequences of `N <text> N+1 <text> N+2 <text>` starting from 1, where N is at a word boundary.

Regex approach: scan for `\b1\b` then check if `\b2\b` and `\b3\b` follow at reasonable intervals with non-numeric text between them. If 3+ consecutive found, reformat.

Insert after number conversion in `Format()`.

- [ ] **4.3: Run tests**

- [ ] **4.4: Commit**

---

## Task 5: Currency, Percentages, Dates, Times

**Goal:** Four related transforms that operate on already-converted digit strings.

**Files:**
- Modify: `VoxScript.Core/Transcription/Processing/SmartTextFormatter.cs`
- Modify: `VoxScript.Tests/Transcription/SmartTextFormatterTests.cs`

### Steps

- [ ] **5.1: Write tests**

```csharp
// Currency
[InlineData("23 dollars", "$23")]
[InlineData("50 cents", "$0.50")]
[InlineData("10 dollars and 50 cents", "$10.50")]
[InlineData("23 bucks", "$23")]

// Percentages
[InlineData("50 percent", "50%")]
[InlineData("100 percent", "100%")]

// Dates
[InlineData("March 5", "March 5th")]
[InlineData("March 5 2026", "March 5th, 2026")]
[InlineData("January 1 2000", "January 1st, 2000")]
[InlineData("December 22", "December 22nd")]

// Times
[InlineData("3 30 PM", "3:30 PM")]
[InlineData("3 PM", "3:00 PM")]
[InlineData("3 o'clock", "3:00")]
[InlineData("noon", "12:00 PM")]
[InlineData("midnight", "12:00 AM")]
```

- [ ] **5.2: Implement currency**

Pattern: `(\d+) dollars` → `$\1`, `(\d+) cents` → `$0.\1`, `(\d+) dollars and (\d+) cents` → `$\1.\2`. Also handle "bucks". Runs after number conversion.

- [ ] **5.3: Implement percentages**

Pattern: `(\d+) percent` → `\1%`

- [ ] **5.4: Implement dates**

- Recognize month names ("January"–"December")
- Pattern: `(Month) (\d{1,2})(?:\s+(\d{4}))?`
- Add ordinal suffix to day (1st, 2nd, 3rd, 4th–20th, 21st, etc.)
- Insert comma before year if present

- [ ] **5.5: Implement times**

- `(\d{1,2})\s+(\d{2})\s*(AM|PM)` → `\1:\2 \3`
- `(\d{1,2})\s*(AM|PM)` → `\1:00 \2`
- `(\d{1,2})\s*o'clock` → `\1:00`
- "noon" → "12:00 PM", "midnight" → "12:00 AM"

- [ ] **5.6: Insert all four in Format() after list detection, before basic cleanup**

Order: currency → percentages → dates → times (currency must run before percentages to avoid "$50 percent" confusion — unlikely but safe)

- [ ] **5.7: Run tests**

- [ ] **5.8: Commit**

---

## Task 6: Email, URL, Phone Assembly

**Goal:** Reassemble spoken emails, URLs, and phone numbers.

**Files:**
- Modify: `VoxScript.Core/Transcription/Processing/SmartTextFormatter.cs`
- Modify: `VoxScript.Tests/Transcription/SmartTextFormatterTests.cs`

### Steps

- [ ] **6.1: Write tests**

```csharp
// Email
[InlineData("rohils74 at gmail dot com", "rohils74@gmail.com")]
[InlineData("user at company dot co dot uk", "user@company.co.uk")]

// URL
[InlineData("w w w dot example dot com", "www.example.com")]
[InlineData("https colon slash slash example dot com", "https://example.com")]
[InlineData("go to example dot com slash about", "go to example.com/about")]

// Phone
[InlineData("call me at 5 5 5 1 2 3 4 5 6 7", "call me at (555) 123-4567")]
[InlineData("my number is 5 5 5 1 2 3 4", "my number is 555-1234")]
```

- [ ] **6.2: Implement email assembly**

Pattern: `(\S+)\s+at\s+(\S+)\s+dot\s+(\S+)` — but handle multiple `dot` segments in domain (e.g., `co dot uk`). Only trigger when result has exactly one `@` and domain has at least one `.`.

- [ ] **6.3: Implement URL assembly**

- "w w w dot" / "www dot" → "www."
- "https colon slash slash" / "http colon slash slash" → "https://" / "http://"
- "dot com/org/net/io/edu/gov" → ".com" etc.
- "slash" → "/" in URL context (after a domain has been detected)

- [ ] **6.4: Implement phone number formatting**

After number conversion, look for sequences of 7 or 10 consecutive single digits (possibly space-separated in the original). Format as US phone: `(XXX) XXX-XXXX` or `XXX-XXXX`.

Regex: scan for `(\d\s+){6}\d` (7 digits) or `(\d\s+){9}\d` (10 digits). Strip spaces, format.

- [ ] **6.5: Insert in Format() after times, before basic cleanup**

Order: email → URL → phone (email needs "at" before URL converts "dot" patterns)

- [ ] **6.6: Run tests**

- [ ] **6.7: Commit**

---

## Task 7: Pipeline Integration + DI Wiring

**Goal:** Replace `WhisperTextFormatter` with `SmartTextFormatter` in the pipeline and DI container.

**Files:**
- Modify: `VoxScript.Core/Transcription/Core/TranscriptionPipeline.cs`
- Modify: `VoxScript/Infrastructure/AppBootstrapper.cs`
- Delete: `VoxScript.Core/Transcription/Processing/WhisperTextFormatter.cs`
- Create: `VoxScript.Tests/Transcription/PipelineSmartFormattingTests.cs`

### Steps

- [ ] **7.1: Replace WhisperTextFormatter with SmartTextFormatter in pipeline**

In `TranscriptionPipeline`:
- Replace `WhisperTextFormatter _formatter` field with `SmartTextFormatter _formatter`
- Update constructor parameter type
- Change step 3 to pass the setting:

```csharp
// 3. Format
string formatted;
try
{
    formatted = _formatter.Format(filtered, _settings.SmartFormattingEnabled);
}
catch (Exception ex)
{
    Log.Warning(ex, "Text formatter failed, using filtered text");
    formatted = filtered;
}
```

- [ ] **7.2: Update AppBootstrapper**

Replace:
```csharp
services.AddSingleton<WhisperTextFormatter>();
```
With:
```csharp
services.AddSingleton<SmartTextFormatter>();
```

- [ ] **7.3: Delete WhisperTextFormatter.cs**

It's fully replaced by SmartTextFormatter's basic cleanup.

- [ ] **7.4: Write pipeline-level integration tests**

```csharp
[Fact]
public async Task SmartFormatting_enabled_converts_numbers()
{
    // Pipeline with SmartFormattingEnabled = true
    // Input: "I need twenty three items"
    // Expected output: "I need 23 items"
}

[Fact]
public async Task SmartFormatting_disabled_only_runs_basic_cleanup()
{
    // Pipeline with SmartFormattingEnabled = false
    // Input: "  hello   world  twenty three"
    // Expected: "Hello world twenty three" (cleanup only, no number conversion)
}
```

- [ ] **7.5: Fix any existing tests that reference WhisperTextFormatter**

Update constructor calls and mocks throughout the test suite.

- [ ] **7.6: Build + run full test suite**

All tests must pass. Expect 104+ existing + new tests.

- [ ] **7.7: Commit**

---

## Transform Execution Order (final reference)

Inside `SmartTextFormatter.Format(string text, bool smartFormattingEnabled)`:

```
if (smartFormattingEnabled):
    1. Spoken punctuation    ("comma" → ",")
    2. Number conversion     ("twenty three" → "23")
    3. List detection        ("1 eggs 2 milk 3 oranges" → formatted list)
    4. Currency              ("23 dollars" → "$23")
    5. Percentages           ("50 percent" → "50%")
    6. Dates                 ("March 5 2026" → "March 5th, 2026")
    7. Times                 ("3 30 PM" → "3:30 PM")
    8. Email assembly        ("at" → "@", "dot" → ".")
    9. URL assembly          ("w w w dot" → "www.")
    10. Phone numbers        (digit sequences → formatted)

always:
    11. Basic cleanup        (collapse spaces, punctuation spacing, sentence caps, trim)
```

Paragraph breaks are handled upstream in `LocalTranscriptionService` (segment gaps ≥ 2.5s → `\n\n`) before the text reaches the pipeline.
