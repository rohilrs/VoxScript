# Smart Formatting — Design Spec

**Date:** 2026-04-07

## Overview

Transform raw whisper transcription output into properly formatted text. Whisper outputs flat text with numbers as words, no list structure, and no awareness of emails, URLs, or special formats. Smart formatting applies a chain of deterministic, rule-based transforms to produce human-readable output.

Gated by the existing `SmartFormattingEnabled` setting (default: true). When off, only basic cleanup runs. When on, the full transform chain runs.

## Data Flow Change

### Problem

`WhisperBackend.RunInference` currently concatenates all whisper segments into a flat string, discarding timestamp information. Paragraph detection requires knowing the time gap between segments.

### Solution

Change the transcription return type to carry segment-level data:

```csharp
public sealed record TranscriptionSegment(string Text, long StartMs, long EndMs);
```

**Ripple path:**
- `WhisperBackend.RunInference` → returns `TranscriptionSegment[]` instead of `string`
- `ILocalTranscriptionBackend.TranscribeAsync` → returns `TranscriptionSegment[]`
- `IWhisperBackend.TranscribeAsync` → returns `TranscriptionSegment[]`
- `LocalTranscriptionService.TranscribeAsync` → joins segments via formatter
- `ParakeetTranscriptionService.TranscribeAsync` → returns single segment (Parakeet doesn't expose timestamps)
- `ITranscriptionService.TranscribeAsync` → still returns `string` (segment joining happens inside the service)

The segment-to-string conversion happens inside `LocalTranscriptionService`, which calls the formatter. This keeps `ITranscriptionService` and everything downstream (pipeline, hallucination filter, word replacement) working on plain strings.

## Transform Chain

Each transform is a pure function applied in sequence. The paragraph break transform is special (takes segments); all others are `string → string`.

### 1. Paragraph Breaks (segment-aware)

**Input:** `TranscriptionSegment[]`
**Output:** `string` with `\n\n` inserted at gaps

- If the gap between one segment's `EndMs` and the next segment's `StartMs` is ≥ 2500ms, insert a double newline between them.
- Otherwise, join with a single space.
- This transform always runs (not gated by SmartFormatting) since it's producing the base string from segments.

### 2. Spoken Punctuation

Replace spoken punctuation words with their symbols. Must run before number conversion to avoid conflicts (e.g., "period" shouldn't be misinterpreted).

| Spoken | Output |
|--------|--------|
| "comma" | `, ` |
| "period" / "full stop" | `. ` |
| "question mark" | `? ` |
| "exclamation point" / "exclamation mark" | `! ` |
| "colon" | `: ` |
| "semicolon" | `; ` |
| "new line" | `\n` |
| "new paragraph" | `\n\n` |

**Rules:**
- Match whole words only (word boundary regex).
- Case-insensitive matching.
- Remove extra spaces around inserted punctuation (e.g., "hello comma world" → "hello, world" not "hello , world").
- Capitalize the next word after sentence-ending punctuation (`. ? !`).

### 3. Number Conversion

Convert spoken number words to digits.

**Cardinals:** "zero" through "nineteen", "twenty", "thirty", ..., "ninety", "hundred", "thousand", "million", "billion".
- "twenty three" → "23"
- "one hundred and fifty" → "150"
- "two thousand twenty six" → "2026"
- "a hundred" → "100"

**Ordinals:** "first" → "1st", "second" → "2nd", "third" → "3rd", "twenty third" → "23rd".

**Rules:**
- Process greedily — consume the longest sequence of number words possible.
- "and" between number words is optional and consumed (e.g., "one hundred and fifty" and "one hundred fifty" both → "150").
- Do NOT convert number words that are part of common phrases where the word form is conventional: "one of", "one thing", "once", "for one". Use a short exclusion list of these patterns.
- Single standalone "one" / "two" etc. in clearly non-numeric context are harder to detect. Start conservative: convert when adjacent to other number words or when starting a list sequence; leave standalone single-digit words as-is unless in a numeric context (currency, date, time, phone).

### 4. List Detection

Detect sequences of numbered items and format as a newline-separated list.

**Pattern:** A sequence of 3+ items where each starts with a consecutive number followed by non-numeric text.

**Input:** `"1 eggs 2 milk 3 oranges"`
**Output:**
```
1. eggs
2. milk
3. oranges
```

**Rules:**
- Requires at least 3 consecutive numbered items to trigger (avoids false positives on "2 people" or "3 times").
- Numbers must be sequential starting from 1.
- Each item's text extends until the next number in the sequence.
- Insert `\n` between items and `. ` after each number.

### 5. Currency

**Patterns:**
- "[number] dollars" → "$[number]" (e.g., "twenty three dollars" → "$23")
- "[number] cents" → "$0.[number]" (e.g., "fifty cents" → "$0.50")
- "[number] dollars and [number] cents" → "$[number].[cents]" (e.g., "ten dollars and fifty cents" → "$10.50")
- "[number] bucks" → "$[number]" (informal)

**Rules:**
- Runs after number conversion, so inputs are already digits.
- Only handles USD ($) — the most common case. Other currencies can be added later.

### 6. Percentages

- "[number] percent" → "[number]%" (e.g., "50 percent" → "50%")
- Runs after number conversion.

### 7. Dates

**Patterns:**
- "[Month] [ordinal/number]" → "[Month] [ordinal]" (e.g., "March 5" → "March 5th")
- "[Month] [ordinal/number] [year]" → "[Month] [ordinal], [year]" (e.g., "March 5 2026" → "March 5th, 2026")
- "[Month] [ordinal/number] [year]" where year is spoken as "twenty twenty six" should already be "2026" from number conversion.

**Rules:**
- Recognize full month names ("January" through "December").
- Day must be 1-31.
- Add ordinal suffix if missing (1st, 2nd, 3rd, 4th-20th, 21st, 22nd, 23rd, 24th-30th, 31st).
- Insert comma before year.

### 8. Times

**Patterns:**
- "[hour] [minutes] AM/PM" → "[hour]:[minutes] [AM/PM]" (e.g., "3 30 PM" → "3:30 PM")
- "[hour] AM/PM" → "[hour]:00 [AM/PM]" (e.g., "3 PM" → "3:00 PM")
- "[hour] o'clock" → "[hour]:00" (e.g., "3 o'clock" → "3:00")
- "noon" → "12:00 PM"
- "midnight" → "12:00 AM"

**Rules:**
- Hour: 1-12 for AM/PM format.
- Minutes: 00-59, zero-padded.
- Runs after number conversion.

### 9. Email Assembly

**Pattern:** `[word] at [word] dot [word]` → `[word]@[word].[word]`

**Rules:**
- "at" must be between two non-whitespace tokens.
- "dot" between tokens in the domain part → `.`
- Multiple "dot" sequences supported (e.g., "co dot uk" → "co.uk").
- Only trigger when the result looks like a valid email pattern (has exactly one `@`, domain has at least one `.`).

### 10. URL Assembly

**Patterns:**
- "w w w dot" / "www dot" → "www."
- "http colon slash slash" → "http://"
- "https colon slash slash" → "https://"
- "dot com" / "dot org" / "dot net" / "dot io" / etc. → ".com" / ".org" / etc.
- "slash" within URL context → "/"

**Rules:**
- Trigger URL mode when a URL prefix is detected ("www", "http", "https").
- Within URL mode, convert "dot" → `.` and "slash" → `/` until a clear word boundary (next sentence, punctuation).

### 11. Phone Numbers

**Pattern:** Sequences of 7 or 10 digits that appear in phone-like groupings.

**Rules:**
- 10 digits → "(XXX) XXX-XXXX" (US format)
- 7 digits → "XXX-XXXX"
- Only trigger on sequences that are clearly phone numbers (10 consecutive single digits spoken in a row, or grouped as "five five five one two three four five six seven").
- Runs after number conversion.

### 12. Basic Cleanup (always runs)

- Collapse multiple spaces to single space.
- Capitalize first character of each sentence (after `. ? ! \n`).
- Trim leading/trailing whitespace.
- Trim whitespace from each line.

## Interface

```csharp
// VoxScript.Core/Transcription/Processing/ITextTransform.cs
public interface ITextTransform
{
    string Apply(string text);
}
```

Paragraph break transform is separate (takes segments, produces string):

```csharp
// VoxScript.Core/Transcription/Processing/ParagraphBreakTransform.cs
public static class ParagraphBreakTransform
{
    public static string Apply(IReadOnlyList<TranscriptionSegment> segments, int gapThresholdMs = 2500);
}
```

## Formatter Orchestration

```csharp
// VoxScript.Core/Transcription/Processing/SmartTextFormatter.cs
public sealed class SmartTextFormatter
{
    private readonly ITextTransform[] _smartTransforms;
    private readonly BasicCleanupTransform _cleanup;

    public string Format(IReadOnlyList<TranscriptionSegment> segments, bool smartFormattingEnabled)
    {
        // Step 1: Always join segments with paragraph breaks
        var text = ParagraphBreakTransform.Apply(segments);

        // Step 2: Smart transforms (only when enabled)
        if (smartFormattingEnabled)
        {
            foreach (var transform in _smartTransforms)
                text = transform.Apply(text);
        }

        // Step 3: Basic cleanup always runs
        text = _cleanup.Apply(text);

        return text;
    }
}
```

Replaces the current `WhisperTextFormatter`. The old class is deleted.

## Integration Points

### LocalTranscriptionService

Currently calls `_backend.TranscribeAsync()` and returns the string. After this change:

```csharp
var segments = await _backend.TranscribeAsync(samples, language, initialPrompt, ct);
return _formatter.Format(segments, _settings.SmartFormattingEnabled);
```

### TranscriptionPipeline

Currently calls `_formatter.Format(filtered)` as step 3. After this change, the formatter call moves into `LocalTranscriptionService` (it needs segments). The pipeline receives an already-formatted string from the transcription service. Remove the `_formatter` field and step 3 from the pipeline.

### ParakeetTranscriptionService

Parakeet doesn't expose segment timestamps. Return a single segment spanning the full audio, so the paragraph break transform is effectively a no-op. Smart text transforms still apply.

## File Layout

```
VoxScript.Core/Transcription/Processing/
    ITextTransform.cs                    (new)
    SmartTextFormatter.cs                (new, replaces WhisperTextFormatter)
    ParagraphBreakTransform.cs           (new)
    SpokenPunctuationTransform.cs        (new)
    NumberConversionTransform.cs         (new)
    ListDetectionTransform.cs            (new)
    CurrencyTransform.cs                 (new)
    PercentageTransform.cs               (new)
    DateTransform.cs                     (new)
    TimeTransform.cs                     (new)
    EmailAssemblyTransform.cs            (new)
    UrlAssemblyTransform.cs              (new)
    PhoneNumberTransform.cs              (new)
    BasicCleanupTransform.cs             (new)
    WhisperTextFormatter.cs              (deleted)

VoxScript.Core/Transcription/Core/
    TranscriptionSegment.cs              (new)
    ILocalTranscriptionBackend.cs        (modified — return type)
    TranscriptionPipeline.cs             (modified — remove formatter step)

VoxScript.Native/Whisper/
    WhisperBackend.cs                    (modified — return segments)
    IWhisperBackend.cs                   (modified — return type)

VoxScript.Core/Transcription/Batch/
    LocalTranscriptionService.cs         (modified — call formatter)
    ParakeetTranscriptionService.cs      (modified — wrap in single segment)
```

## Testing Strategy

Each transform gets its own test class with cases for:
- Happy path (the examples in this spec)
- Edge cases (empty input, no matches, partial matches)
- No false positives (text that looks similar but shouldn't transform)

Key test cases to verify:
- "one eggs two milk three oranges" → "1. eggs\n2. milk\n3. oranges"
- "rohils74 at gmail dot com" → "rohils74@gmail.com"
- "twenty three dollars" → "$23"
- "march fifth twenty twenty six" → "March 5th, 2026"
- "3 30 PM" → "3:30 PM"
- "hello comma how are you question mark" → "Hello, how are you?"
- "fifty percent" → "50%"
- Segments with 3s gap → paragraph break between them
- Segments with 1s gap → no paragraph break
- SmartFormatting disabled → only paragraph breaks and basic cleanup

## Settings

No new settings. Uses existing `SmartFormattingEnabled` (bool, default true).

The paragraph gap threshold (2500ms) is hardcoded. Can be made configurable later if needed.
