# Structural Formatting Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add spoken structural formatting commands to `SmartTextFormatter` — headings, bullet points, bold/italic, blockquotes, inline code, and horizontal rules — converting spoken phrases like "heading one Introduction" or "bullet buy milk" into the corresponding Markdown syntax.

**Architecture:** A new `ApplyStructuralFormatting` method is added to `SmartTextFormatter` and called early in the smart-formatting path (before `ApplyBasicCleanup`), following the same pattern as all existing transforms. Each construct is handled by a dedicated private method with a `[GeneratedRegex]` source-generated pattern. The protection mechanism already in place for emails/URLs covers any structural tokens that would otherwise be mangled by cleanup.

**Tech Stack:** C# 13 / .NET 10, `System.Text.RegularExpressions` source generators (existing pattern), xUnit + FluentAssertions (existing test project).

---

## File Structure

| File | Action | Responsibility |
|------|--------|---------------|
| `VoxScript.Core/Transcription/Processing/SmartTextFormatter.cs` | Modify | Add `ApplyStructuralFormatting` call in `Format()` and implement heading, bullet, emphasis, blockquote, code, and horizontal-rule private methods |
| `VoxScript.Tests/Transcription/SmartTextFormatterTests.cs` | Modify | Add a new `// ── Structural Formatting` region with theory/fact tests for every new transform |

---

## Task 1: Heading Detection

**Files:**
- Modify: `VoxScript.Core/Transcription/Processing/SmartTextFormatter.cs`
- Test: `VoxScript.Tests/Transcription/SmartTextFormatterTests.cs`

- [ ] **Step 1: Write the failing tests**

Add to `SmartTextFormatterTests.cs` after the Phone Number section:

```csharp
// ── Structural Formatting — Headings ─────────────────────────────

[Theory]
[InlineData("heading one Introduction", "# Introduction")]
[InlineData("heading two Background", "## Background")]
[InlineData("heading three Details", "### Details")]
[InlineData("h1 Summary", "# Summary")]
[InlineData("h2 Overview", "## Overview")]
[InlineData("h3 Notes", "### Notes")]
public void Headings_are_formatted(string input, string expected)
{
    _sut.Format(input, smartFormattingEnabled: true).Should().Be(expected);
}

[Fact]
public void Heading_in_middle_of_text_is_on_its_own_line()
{
    _sut.Format("some text heading two Section here some more text", smartFormattingEnabled: true)
        .Should().Be("Some text\n## Section here some more text");
}

[Fact]
public void Headings_disabled_when_smart_formatting_off()
{
    _sut.Format("heading one Introduction", smartFormattingEnabled: false)
        .Should().Be("Heading one Introduction");
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test VoxScript.Tests --filter "FullyQualifiedName~SmartTextFormatterTests.Headings"`
Expected: FAIL — `# Introduction` not produced yet

- [ ] **Step 3: Implement heading detection**

Add the following inside `SmartTextFormatter.cs`, in a new `// ── Structural Formatting ──` region, just before the `// ── Basic Cleanup` region.

First add the call in `Format()` inside the `if (smartFormattingEnabled)` block after `FormatPhoneNumbers`:

```csharp
text = ApplyStructuralFormatting(text);
```

Then add the region:

```csharp
// ── Structural Formatting ─────────────────────────────────────────

/// <summary>
/// Converts spoken structural commands (headings, bullets, emphasis,
/// blockquotes, code, horizontal rules) to Markdown syntax.
/// Must run before basic cleanup so that newlines are respected.
/// </summary>
private static string ApplyStructuralFormatting(string text)
{
    text = FormatHeadings(text);
    return text;
}

/// <summary>
/// Converts "heading one/two/three X" and "h1/h2/h3 X" to Markdown headings.
/// When the heading appears mid-sentence, the preceding text is separated
/// onto its own line with a newline.
/// </summary>
private static string FormatHeadings(string text)
{
    return HeadingRegex().Replace(text, m =>
    {
        string level = m.Groups[1].Value.ToLowerInvariant();
        string content = m.Groups[2].Value.Trim();
        string prefix = m.Groups[3].Value; // text before the heading command

        int hashes = level switch
        {
            "one" or "1" => 1,
            "two" or "2" => 2,
            "three" or "3" => 3,
            _ => 1
        };

        string hdr = new string('#', hashes) + " " + content;

        if (!string.IsNullOrWhiteSpace(prefix))
            return prefix.TrimEnd() + "\n" + hdr;

        return hdr;
    });
}

// "heading one/two/three <content>" or "h1/h2/h3 <content>"
// Captures: group 1 = level word/digit, group 2 = rest of line until next heading or end,
//           group 3 = any text that precedes this command on the same segment
[GeneratedRegex(
    @"(.*?)\b(?:heading\s+(one|two|three)|h([123]))\s+(.+?)(?=\b(?:heading\s+(?:one|two|three)|h[123])\b|$)",
    RegexOptions.IgnoreCase | RegexOptions.Singleline)]
private static partial Regex HeadingRegex();
```

Note: The regex has four capture groups. Refactor the match handler accordingly:

```csharp
private static string FormatHeadings(string text)
{
    return HeadingRegex().Replace(text, m =>
    {
        string before = m.Groups[1].Value;  // text preceding this heading command
        string wordLevel = m.Groups[2].Value;  // "one"/"two"/"three" (from "heading X")
        string digitLevel = m.Groups[3].Value; // "1"/"2"/"3" (from "hN")
        string content = m.Groups[4].Value.Trim();

        string levelKey = !string.IsNullOrEmpty(wordLevel)
            ? wordLevel.ToLowerInvariant()
            : digitLevel;

        int hashes = levelKey switch
        {
            "one" or "1" => 1,
            "two" or "2" => 2,
            "three" or "3" => 3,
            _ => 1
        };

        string hdr = new string('#', hashes) + " " + content;

        if (!string.IsNullOrWhiteSpace(before))
            return before.TrimEnd() + "\n" + hdr;

        return hdr;
    });
}
```

- [ ] **Step 4: Run tests to verify they pass**

Run: `dotnet test VoxScript.Tests --filter "FullyQualifiedName~SmartTextFormatterTests.Heading"`
Expected: all PASS

- [ ] **Step 5: Commit**

```bash
git add VoxScript.Core/Transcription/Processing/SmartTextFormatter.cs \
        VoxScript.Tests/Transcription/SmartTextFormatterTests.cs
git commit -m "feat: add heading detection to SmartTextFormatter"
```

---

## Task 2: Bullet Point Detection

**Files:**
- Modify: `VoxScript.Core/Transcription/Processing/SmartTextFormatter.cs`
- Test: `VoxScript.Tests/Transcription/SmartTextFormatterTests.cs`

- [ ] **Step 1: Write the failing tests**

Append to the Structural Formatting test region:

```csharp
// ── Structural Formatting — Bullet Points ────────────────────────

[Theory]
[InlineData("bullet buy milk", "- Buy milk")]
[InlineData("dash pick up dry cleaning", "- Pick up dry cleaning")]
[InlineData("new bullet call dentist", "- Call dentist")]
public void Single_bullet_is_formatted(string input, string expected)
{
    _sut.Format(input, smartFormattingEnabled: true).Should().Be(expected);
}

[Fact]
public void Multiple_bullets_produce_list()
{
    _sut.Format("bullet eggs bullet milk bullet bread", smartFormattingEnabled: true)
        .Should().Be("- Eggs\n- Milk\n- Bread");
}

[Fact]
public void Bullets_after_intro_text_are_on_new_lines()
{
    _sut.Format("my shopping list bullet eggs bullet milk", smartFormattingEnabled: true)
        .Should().Be("My shopping list\n- Eggs\n- Milk");
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test VoxScript.Tests --filter "FullyQualifiedName~SmartTextFormatterTests" --filter "FullyQualifiedName~bullet|Bullet"`
Expected: FAIL

- [ ] **Step 3: Implement bullet detection**

Add `FormatBullets(text)` call to `ApplyStructuralFormatting` after `FormatHeadings`:

```csharp
text = FormatBullets(text);
```

Add the method and regex:

```csharp
/// <summary>
/// Converts "bullet X" and "dash X" and "new bullet X" to "- X" Markdown bullets,
/// inserting newlines between consecutive bullets and between preceding prose and the first bullet.
/// </summary>
private static string FormatBullets(string text)
{
    // Replace each spoken bullet command with a newline-prefixed "- content" marker.
    // We use a sentinel approach: first pass replaces "bullet X" → "\n- X",
    // then we clean up any leading newline if the bullet is at the start.
    text = BulletCommandRegex().Replace(text, m =>
    {
        string before = m.Groups[1].Value;
        string content = m.Groups[2].Value.Trim();

        // Capitalize the item text
        if (content.Length > 0 && char.IsLower(content[0]))
            content = char.ToUpper(content[0]) + content[1..];

        string item = "- " + content;

        if (!string.IsNullOrWhiteSpace(before))
            return before.TrimEnd() + "\n" + item;

        return item;
    });

    return text;
}

// Matches "bullet X", "new bullet X", "dash X" where X is content up to the next bullet or end.
// Group 1: text before the bullet command; Group 2: bullet item content.
[GeneratedRegex(
    @"(.*?)\b(?:new\s+)?(?:bullet|dash)\s+(.+?)(?=\b(?:(?:new\s+)?(?:bullet|dash))\b|$)",
    RegexOptions.IgnoreCase | RegexOptions.Singleline)]
private static partial Regex BulletCommandRegex();
```

- [ ] **Step 4: Run tests to verify they pass**

Run: `dotnet test VoxScript.Tests --filter "FullyQualifiedName~SmartTextFormatterTests"`
Expected: all PASS (including previous tasks)

- [ ] **Step 5: Commit**

```bash
git add VoxScript.Core/Transcription/Processing/SmartTextFormatter.cs \
        VoxScript.Tests/Transcription/SmartTextFormatterTests.cs
git commit -m "feat: add bullet point detection to SmartTextFormatter"
```

---

## Task 3: Bold and Italic Emphasis

**Files:**
- Modify: `VoxScript.Core/Transcription/Processing/SmartTextFormatter.cs`
- Test: `VoxScript.Tests/Transcription/SmartTextFormatterTests.cs`

- [ ] **Step 1: Write the failing tests**

Append to the Structural Formatting test region:

```csharp
// ── Structural Formatting — Emphasis ─────────────────────────────

[Theory]
[InlineData("this is bold important end bold", "This is **important**")]
[InlineData("press italic escape italic to cancel", "Press _escape_ to cancel")]
[InlineData("bold hello end bold world", "**Hello** world")]
public void Emphasis_is_applied(string input, string expected)
{
    _sut.Format(input, smartFormattingEnabled: true).Should().Be(expected);
}

[Fact]
public void Bold_without_end_bold_is_not_transformed()
{
    // Without a closing delimiter, do not partially transform
    _sut.Format("this is bold important", smartFormattingEnabled: true)
        .Should().Be("This is bold important");
}

[Fact]
public void Italic_without_end_italic_is_not_transformed()
{
    _sut.Format("press italic escape to cancel", smartFormattingEnabled: true)
        .Should().Be("Press italic escape to cancel");
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test VoxScript.Tests --filter "FullyQualifiedName~SmartTextFormatterTests.Emphasis"`
Expected: FAIL

- [ ] **Step 3: Implement emphasis formatting**

Add `FormatEmphasis(text)` call in `ApplyStructuralFormatting` after `FormatBullets`:

```csharp
text = FormatEmphasis(text);
```

Add the method and regexes:

```csharp
/// <summary>
/// Converts "bold X end bold" → "**X**" and "italic X end italic" → "_X_".
/// Both delimiters must be present — unpaired spoken markers are left as-is.
/// </summary>
private static string FormatEmphasis(string text)
{
    text = BoldRegex().Replace(text, m =>
    {
        string content = m.Groups[1].Value.Trim();
        if (content.Length > 0 && char.IsUpper(content[0]))
            content = char.ToLower(content[0]) + content[1..];
        return $"**{content}**";
    });

    text = ItalicRegex().Replace(text, m =>
    {
        string content = m.Groups[1].Value.Trim();
        if (content.Length > 0 && char.IsUpper(content[0]))
            content = char.ToLower(content[0]) + content[1..];
        return $"_{content}_";
    });

    return text;
}

// "bold <content> end bold" — content is anything between the paired delimiters
[GeneratedRegex(@"\bbold\s+(.+?)\s+end\s+bold\b", RegexOptions.IgnoreCase)]
private static partial Regex BoldRegex();

// "italic <content> end italic"
[GeneratedRegex(@"\bitalic\s+(.+?)\s+end\s+italic\b", RegexOptions.IgnoreCase)]
private static partial Regex ItalicRegex();
```

- [ ] **Step 4: Run tests to verify they pass**

Run: `dotnet test VoxScript.Tests --filter "FullyQualifiedName~SmartTextFormatterTests"`
Expected: all PASS

- [ ] **Step 5: Commit**

```bash
git add VoxScript.Core/Transcription/Processing/SmartTextFormatter.cs \
        VoxScript.Tests/Transcription/SmartTextFormatterTests.cs
git commit -m "feat: add bold and italic emphasis formatting to SmartTextFormatter"
```

---

## Task 4: Blockquotes

**Files:**
- Modify: `VoxScript.Core/Transcription/Processing/SmartTextFormatter.cs`
- Test: `VoxScript.Tests/Transcription/SmartTextFormatterTests.cs`

- [ ] **Step 1: Write the failing tests**

Append to the Structural Formatting test region:

```csharp
// ── Structural Formatting — Blockquotes ──────────────────────────

[Theory]
[InlineData("quote to be or not to be end quote", "> To be or not to be")]
[InlineData("blockquote ask not what your country can do end quote", "> Ask not what your country can do")]
public void Blockquotes_are_formatted(string input, string expected)
{
    _sut.Format(input, smartFormattingEnabled: true).Should().Be(expected);
}

[Fact]
public void Blockquote_without_end_quote_is_not_transformed()
{
    _sut.Format("quote to be or not to be", smartFormattingEnabled: true)
        .Should().Be("Quote to be or not to be");
}

[Fact]
public void Blockquote_preceded_by_text_gets_newline()
{
    _sut.Format("he said quote hello world end quote", smartFormattingEnabled: true)
        .Should().Be("He said\n> Hello world");
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test VoxScript.Tests --filter "FullyQualifiedName~SmartTextFormatterTests.Blockquote"`
Expected: FAIL

- [ ] **Step 3: Implement blockquote formatting**

Add `FormatBlockquotes(text)` call in `ApplyStructuralFormatting` after `FormatEmphasis`:

```csharp
text = FormatBlockquotes(text);
```

Add the method and regexes:

```csharp
/// <summary>
/// Converts "quote X end quote" and "blockquote X end quote" to "> X".
/// When preceded by prose, inserts a newline before the blockquote.
/// Unpaired "quote" markers are left as-is.
/// </summary>
private static string FormatBlockquotes(string text)
{
    return BlockquoteRegex().Replace(text, m =>
    {
        string before = m.Groups[1].Value;
        string content = m.Groups[2].Value.Trim();

        // Capitalize content
        if (content.Length > 0 && char.IsLower(content[0]))
            content = char.ToUpper(content[0]) + content[1..];

        string bq = "> " + content;

        if (!string.IsNullOrWhiteSpace(before))
            return before.TrimEnd() + "\n" + bq;

        return bq;
    });
}

// "(prose) quote/blockquote <content> end quote"
// Group 1: optional preceding text, Group 2: quoted content
[GeneratedRegex(
    @"(.*?)\b(?:blockquote|quote)\s+(.+?)\s+end\s+quote\b",
    RegexOptions.IgnoreCase | RegexOptions.Singleline)]
private static partial Regex BlockquoteRegex();
```

- [ ] **Step 4: Run tests to verify they pass**

Run: `dotnet test VoxScript.Tests --filter "FullyQualifiedName~SmartTextFormatterTests"`
Expected: all PASS

- [ ] **Step 5: Commit**

```bash
git add VoxScript.Core/Transcription/Processing/SmartTextFormatter.cs \
        VoxScript.Tests/Transcription/SmartTextFormatterTests.cs
git commit -m "feat: add blockquote formatting to SmartTextFormatter"
```

---

## Task 5: Inline Code

**Files:**
- Modify: `VoxScript.Core/Transcription/Processing/SmartTextFormatter.cs`
- Test: `VoxScript.Tests/Transcription/SmartTextFormatterTests.cs`

- [ ] **Step 1: Write the failing tests**

Append to the Structural Formatting test region:

```csharp
// ── Structural Formatting — Inline Code ──────────────────────────

[Theory]
[InlineData("press code escape end code to cancel", "Press `escape` to cancel")]
[InlineData("the variable code my var end code holds the count", "The variable `my var` holds the count")]
[InlineData("backtick null backtick means nothing", "`null` means nothing")]
public void Inline_code_is_formatted(string input, string expected)
{
    _sut.Format(input, smartFormattingEnabled: true).Should().Be(expected);
}

[Fact]
public void Code_without_end_code_is_not_transformed()
{
    _sut.Format("press code escape to cancel", smartFormattingEnabled: true)
        .Should().Be("Press code escape to cancel");
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test VoxScript.Tests --filter "FullyQualifiedName~SmartTextFormatterTests.Inline_code"`
Expected: FAIL

- [ ] **Step 3: Implement inline code formatting**

Add `FormatInlineCode(text)` call in `ApplyStructuralFormatting` after `FormatBlockquotes`:

```csharp
text = FormatInlineCode(text);
```

Add the method and regexes. Note: inline code content must be excluded from basic cleanup (capitalization and space-after-dot mangling). The existing protection mechanism only covers emails/URLs, so we extend `ProtectedTokenRegex` to also match backtick spans, OR we run `FormatInlineCode` in the already-protected pass. The cleanest approach is to run structural formatting before protection is applied, and let the code tokens get picked up by the existing `ProtectedTokenRegex` since backtick-wrapped tokens contain no spaces that need protecting. Backtick content is safe — basic cleanup won't mangle `\`null\`` because there are no dots or caps rules inside backticks.

```csharp
/// <summary>
/// Converts "code X end code" → "`X`" and "backtick X backtick" → "`X`".
/// Unpaired markers are left as-is.
/// </summary>
private static string FormatInlineCode(string text)
{
    // "code X end code"
    text = InlineCodeRegex().Replace(text, m => "`" + m.Groups[1].Value.Trim() + "`");

    // "backtick X backtick" (single-word or short snippet shorthand)
    text = BacktickPairRegex().Replace(text, m => "`" + m.Groups[1].Value.Trim() + "`");

    return text;
}

// "code <content> end code"
[GeneratedRegex(@"\bcode\s+(.+?)\s+end\s+code\b", RegexOptions.IgnoreCase)]
private static partial Regex InlineCodeRegex();

// "backtick <content> backtick"
[GeneratedRegex(@"\bbacktick\s+(.+?)\s+backtick\b", RegexOptions.IgnoreCase)]
private static partial Regex BacktickPairRegex();
```

- [ ] **Step 4: Run tests to verify they pass**

Run: `dotnet test VoxScript.Tests --filter "FullyQualifiedName~SmartTextFormatterTests"`
Expected: all PASS

- [ ] **Step 5: Commit**

```bash
git add VoxScript.Core/Transcription/Processing/SmartTextFormatter.cs \
        VoxScript.Tests/Transcription/SmartTextFormatterTests.cs
git commit -m "feat: add inline code formatting to SmartTextFormatter"
```

---

## Task 6: Horizontal Rule

**Files:**
- Modify: `VoxScript.Core/Transcription/Processing/SmartTextFormatter.cs`
- Test: `VoxScript.Tests/Transcription/SmartTextFormatterTests.cs`

- [ ] **Step 1: Write the failing tests**

Append to the Structural Formatting test region:

```csharp
// ── Structural Formatting — Horizontal Rule ───────────────────────

[Theory]
[InlineData("horizontal rule", "---")]
[InlineData("divider", "---")]
[InlineData("section break", "---")]
public void Horizontal_rule_is_inserted(string input, string expected)
{
    _sut.Format(input, smartFormattingEnabled: true).Should().Be(expected);
}

[Fact]
public void Horizontal_rule_mid_text_has_surrounding_newlines()
{
    _sut.Format("paragraph one horizontal rule paragraph two", smartFormattingEnabled: true)
        .Should().Be("Paragraph one\n---\nParagraph two");
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test VoxScript.Tests --filter "FullyQualifiedName~SmartTextFormatterTests.Horizontal_rule"`
Expected: FAIL

- [ ] **Step 3: Implement horizontal rule insertion**

Add `FormatHorizontalRules(text)` call in `ApplyStructuralFormatting` after `FormatInlineCode`:

```csharp
text = FormatHorizontalRules(text);
```

Add the method and regexes:

```csharp
/// <summary>
/// Converts "horizontal rule", "divider", and "section break" to Markdown "---".
/// When the command appears mid-sentence, inserts surrounding newlines so the rule
/// appears on its own line.
/// </summary>
private static string FormatHorizontalRules(string text)
{
    return HorizontalRuleRegex().Replace(text, m =>
    {
        string before = m.Groups[1].Value;
        string after = m.Groups[2].Value;

        bool hasBefore = !string.IsNullOrWhiteSpace(before);
        bool hasAfter = !string.IsNullOrWhiteSpace(after);

        string rule = "---";

        return (hasBefore, hasAfter) switch
        {
            (true, true)  => before.TrimEnd() + "\n" + rule + "\n" + after.TrimStart(),
            (true, false) => before.TrimEnd() + "\n" + rule,
            (false, true) => rule + "\n" + after.TrimStart(),
            _             => rule
        };
    });
}

// Matches optional leading text, the rule command, and optional trailing text.
// Group 1: text before; Group 2: text after.
[GeneratedRegex(
    @"(.*?)\b(?:horizontal\s+rule|divider|section\s+break)\b(.*)",
    RegexOptions.IgnoreCase | RegexOptions.Singleline)]
private static partial Regex HorizontalRuleRegex();
```

- [ ] **Step 4: Run tests to verify they pass**

Run: `dotnet test VoxScript.Tests --filter "FullyQualifiedName~SmartTextFormatterTests"`
Expected: all PASS

- [ ] **Step 5: Commit**

```bash
git add VoxScript.Core/Transcription/Processing/SmartTextFormatter.cs \
        VoxScript.Tests/Transcription/SmartTextFormatterTests.cs
git commit -m "feat: add horizontal rule formatting to SmartTextFormatter"
```

---

## Task 7: Wire ApplyStructuralFormatting into Format() and Run Full Suite

**Files:**
- Modify: `VoxScript.Core/Transcription/Processing/SmartTextFormatter.cs`

- [ ] **Step 1: Verify the call site is correct**

Confirm the `Format()` method's `if (smartFormattingEnabled)` block calls transforms in this order:

```csharp
text = ApplySpokenPunctuation(text);
text = ConvertNumbers(text);
text = DetectLists(text);
text = FormatCurrency(text);
text = FormatPercentages(text);
text = FormatDates(text);
text = FormatTimes(text);
text = AssembleEmails(text);
text = AssembleUrls(text);
text = FormatPhoneNumbers(text);
text = ApplyStructuralFormatting(text);  // <-- new, after all inline transforms
```

Structural formatting runs last among the smart transforms so that number conversion and spoken punctuation have already been applied before structural commands are parsed. This prevents, for example, "heading one" being mis-recognized before number conversion turns surrounding words to digits.

- [ ] **Step 2: Extend ProtectedTokenRegex to cover backtick code spans**

Update the existing `ProtectedTokenRegex` to also match backtick-wrapped tokens so basic cleanup doesn't capitalize inside them:

```csharp
// Matches email addresses, URLs, and backtick code spans that should be
// protected from cleanup (capitalization, space-after-dot mangling).
[GeneratedRegex(@"`[^`]+`|\S+@\S+\.\S+|https?://\S+|www\.\S+|\S+\.\S+/\S+", RegexOptions.IgnoreCase)]
private static partial Regex ProtectedTokenRegex();
```

- [ ] **Step 3: Run the full test suite**

Run: `dotnet test VoxScript.Tests`
Expected: all tests PASS (103 existing + new structural formatting tests)

- [ ] **Step 4: Commit**

```bash
git add VoxScript.Core/Transcription/Processing/SmartTextFormatter.cs
git commit -m "feat: wire structural formatting into Format() pipeline and protect code spans"
```

---

## Task 8: Edge Case and Integration Tests

**Files:**
- Test: `VoxScript.Tests/Transcription/SmartTextFormatterTests.cs`

- [ ] **Step 1: Write integration / edge case tests**

Append to `SmartTextFormatterTests.cs`:

```csharp
// ── Structural Formatting — Integration / Edge Cases ─────────────

[Fact]
public void Heading_followed_by_bullets_formats_correctly()
{
    _sut.Format(
        "heading two Shopping List bullet eggs bullet milk bullet bread",
        smartFormattingEnabled: true)
        .Should().Be("## Shopping List\n- Eggs\n- Milk\n- Bread");
}

[Fact]
public void Emphasis_inside_bullet_formats_correctly()
{
    _sut.Format("bullet bold important end bold task", smartFormattingEnabled: true)
        .Should().Be("- **Important** task");
}

[Fact]
public void Horizontal_rule_between_headings_formats_correctly()
{
    _sut.Format(
        "heading one Part One horizontal rule heading one Part Two",
        smartFormattingEnabled: true)
        .Should().Be("# Part One\n---\n# Part Two");
}

[Fact]
public void Structural_commands_disabled_when_smart_formatting_off()
{
    const string input = "heading one Title bullet item horizontal rule";
    _sut.Format(input, smartFormattingEnabled: false)
        .Should().Be("Heading one Title bullet item horizontal rule");
}

[Fact]
public void Inline_code_inside_sentence_preserves_case()
{
    // "null" inside backtick should not be capitalized by cleanup
    _sut.Format("returns code null end code when empty", smartFormattingEnabled: true)
        .Should().Be("Returns `null` when empty");
}
```

- [ ] **Step 2: Run all tests**

Run: `dotnet test VoxScript.Tests`
Expected: all PASS

- [ ] **Step 3: Commit**

```bash
git add VoxScript.Tests/Transcription/SmartTextFormatterTests.cs
git commit -m "test: add structural formatting integration and edge case tests"
```

---

## Implementation Notes

### Transform Ordering Rationale

`ApplyStructuralFormatting` is called after all other smart transforms because:
- `ConvertNumbers` needs to fire first so that "heading one" does not appear after numbers like "heading 1" — both forms are handled by the heading regex, but spoken punctuation and number conversion should not interfere with structural command boundaries.
- `AssembleEmails` / `AssembleUrls` run before structural formatting so that "dot" patterns inside URLs are not confused with structural keywords.
- `ApplyBasicCleanup` runs after structural formatting and the protection step so that Markdown output (`##`, `- `, `---`, `**`, `` ` ``) is not mangled by the space-normalization and capitalization passes.

### Regex Design Decisions

- All structural regexes use `RegexOptions.IgnoreCase` since voice input does not reliably produce case.
- Structural regexes use `RegexOptions.Singleline` where the content can span across what were originally multiple sentences (e.g., a bullet item containing spoken punctuation already converted to `.`).
- Emphasis (`bold`/`italic`) and inline code (`code`/`backtick`) require paired delimiters — unpaired markers pass through unchanged to avoid false positives on the word "bold" or "code" used in normal speech.
- `HorizontalRuleRegex` consumes the entire remaining line via group 2 to enable correct newline insertion for mid-sentence dividers.

### Protection Boundary

Backtick code spans are added to `ProtectedTokenRegex` so basic cleanup does not:
1. Capitalize the first letter of code content (e.g., `` `null` `` → `` `Null` ``).
2. Insert spaces after dots inside code (e.g., `` `foo.bar` `` → `` `foo. bar` ``).

Markdown heading markers (`## `), bullet markers (`- `), blockquote markers (`> `), and horizontal rules (`---`) do not need protection because basic cleanup only capitalizes after sentence-ending punctuation and newlines — which is the correct behavior for these elements.
