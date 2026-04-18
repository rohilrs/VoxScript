using System.Text;
using System.Text.RegularExpressions;

namespace VoxScript.Core.Transcription.Processing;

/// <summary>
/// Formats transcription output with optional smart transforms (spoken punctuation, etc.)
/// and always-on basic cleanup (whitespace normalization, sentence capitalization, punctuation spacing).
/// Replaces the former WhisperTextFormatter with smart formatting capabilities.
/// </summary>
public sealed partial class SmartTextFormatter
{
    public string Format(string text, bool smartFormattingEnabled)
    {
        if (string.IsNullOrWhiteSpace(text)) return string.Empty;

        List<string>? protectedTokens = null;

        if (smartFormattingEnabled)
        {
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

            // Protect assembled emails and URLs from basic cleanup mangling
            // (cleanup inserts spaces after dots and capitalizes, breaking emails/URLs)
            protectedTokens = [];
            text = ProtectedTokenRegex().Replace(text, m =>
            {
                int index = protectedTokens.Count;
                protectedTokens.Add(m.Value);
                return $"\x00PH{index}\x00";
            });
        }

        text = ApplyBasicCleanup(text);

        // Restore protected tokens
        if (protectedTokens is { Count: > 0 })
        {
            text = PlaceholderRegex().Replace(text, m =>
            {
                int index = int.Parse(m.Groups[1].Value);
                return protectedTokens[index];
            });
        }

        return text;
    }

    // Matches email addresses and URLs that should be protected from cleanup
    [GeneratedRegex(@"\S+@\S+\.\S+|https?://\S+|www\.\S+|\S+\.\S+/\S+", RegexOptions.IgnoreCase)]
    private static partial Regex ProtectedTokenRegex();

    // Matches placeholders inserted for protection
    [GeneratedRegex(@"\x00PH(\d+)\x00")]
    private static partial Regex PlaceholderRegex();

    // ── Spoken Punctuation ─────────────────────────────────────────────

    private string ApplySpokenPunctuation(string text)
    {
        // Multi-word phrases first (order matters — "new paragraph" before "new line")
        text = NewParagraphRegex().Replace(text, "\n\n");
        text = NewLineRegex().Replace(text, "\n");
        text = FullStopRegex().Replace(text, ". ");
        text = QuestionMarkRegex().Replace(text, "? ");
        text = ExclamationPointRegex().Replace(text, "! ");
        text = ExclamationMarkRegex().Replace(text, "! ");

        // Single words
        text = CommaRegex().Replace(text, ", ");
        text = PeriodRegex().Replace(text, ". ");
        text = ColonRegex().Replace(text, ": ");
        text = SemicolonRegex().Replace(text, "; ");

        // Clean up whitespace around inserted punctuation: collapse spaces before punctuation
        text = SpaceBeforeInsertedPunctRegex().Replace(text, "$1");

        // Capitalize after sentence-ending punctuation
        text = CapitalizeAfterSentenceEndRegex().Replace(text, m =>
            m.Groups[1].Value + char.ToUpper(m.Groups[2].Value[0]));

        // Capitalize after newlines
        text = CapitalizeAfterNewlineRegex().Replace(text, m =>
            m.Groups[1].Value + char.ToUpper(m.Groups[2].Value[0]));

        // Trim trailing space from punctuation at end of string
        text = text.TrimEnd();

        return text;
    }

    // Multi-word phrase patterns (match whole words, case-insensitive)
    // Surrounding whitespace is consumed so punctuation attaches to previous word
    [GeneratedRegex(@"\s*\bnew\s+paragraph\b\s*", RegexOptions.IgnoreCase)]
    private static partial Regex NewParagraphRegex();

    [GeneratedRegex(@"\s*\bnew\s+line\b\s*", RegexOptions.IgnoreCase)]
    private static partial Regex NewLineRegex();

    [GeneratedRegex(@"\s*\bfull\s+stop\b\s*", RegexOptions.IgnoreCase)]
    private static partial Regex FullStopRegex();

    [GeneratedRegex(@"\s*\bquestion\s+mark\b\s*", RegexOptions.IgnoreCase)]
    private static partial Regex QuestionMarkRegex();

    [GeneratedRegex(@"\s*\bexclamation\s+point\b\s*", RegexOptions.IgnoreCase)]
    private static partial Regex ExclamationPointRegex();

    [GeneratedRegex(@"\s*\bexclamation\s+mark\b\s*", RegexOptions.IgnoreCase)]
    private static partial Regex ExclamationMarkRegex();

    // Single-word patterns
    [GeneratedRegex(@"\s*\bcomma\b\s*", RegexOptions.IgnoreCase)]
    private static partial Regex CommaRegex();

    [GeneratedRegex(@"\s*\bperiod\b\s*", RegexOptions.IgnoreCase)]
    private static partial Regex PeriodRegex();

    [GeneratedRegex(@"\s*\bcolon\b\s*", RegexOptions.IgnoreCase)]
    private static partial Regex ColonRegex();

    [GeneratedRegex(@"\s*\bsemicolon\b\s*", RegexOptions.IgnoreCase)]
    private static partial Regex SemicolonRegex();

    // Collapse spaces that ended up before punctuation (e.g., "hello , world" → "hello, world")
    [GeneratedRegex(@"\s+([,.\?!:;])")]
    private static partial Regex SpaceBeforeInsertedPunctRegex();

    // Capitalize after sentence-ending punctuation followed by space(s)
    [GeneratedRegex(@"([.!?]\s+)([a-z])")]
    private static partial Regex CapitalizeAfterSentenceEndRegex();

    // Capitalize after newlines
    [GeneratedRegex(@"(\n+)([a-z])")]
    private static partial Regex CapitalizeAfterNewlineRegex();

    // ── Number Conversion ──────────────────────────────────────────────

    // Cardinal number word → value mappings
    private static readonly Dictionary<string, int> Units = new(StringComparer.OrdinalIgnoreCase)
    {
        ["zero"] = 0, ["one"] = 1, ["two"] = 2, ["three"] = 3, ["four"] = 4,
        ["five"] = 5, ["six"] = 6, ["seven"] = 7, ["eight"] = 8, ["nine"] = 9,
        ["ten"] = 10, ["eleven"] = 11, ["twelve"] = 12, ["thirteen"] = 13,
        ["fourteen"] = 14, ["fifteen"] = 15, ["sixteen"] = 16, ["seventeen"] = 17,
        ["eighteen"] = 18, ["nineteen"] = 19
    };

    private static readonly Dictionary<string, int> Tens = new(StringComparer.OrdinalIgnoreCase)
    {
        ["twenty"] = 20, ["thirty"] = 30, ["forty"] = 40, ["fifty"] = 50,
        ["sixty"] = 60, ["seventy"] = 70, ["eighty"] = 80, ["ninety"] = 90
    };

    private static readonly Dictionary<string, long> Scales = new(StringComparer.OrdinalIgnoreCase)
    {
        ["hundred"] = 100, ["thousand"] = 1000, ["million"] = 1_000_000, ["billion"] = 1_000_000_000
    };

    // Ordinal word → (value, suffix) mappings
    private static readonly Dictionary<string, (int Value, string Suffix)> OrdinalUnits = new(StringComparer.OrdinalIgnoreCase)
    {
        ["first"] = (1, "st"), ["second"] = (2, "nd"), ["third"] = (3, "rd"),
        ["fourth"] = (4, "th"), ["fifth"] = (5, "th"), ["sixth"] = (6, "th"),
        ["seventh"] = (7, "th"), ["eighth"] = (8, "th"), ["ninth"] = (9, "th"),
        ["tenth"] = (10, "th"), ["eleventh"] = (11, "th"), ["twelfth"] = (12, "th"),
        ["thirteenth"] = (13, "th"), ["fourteenth"] = (14, "th"), ["fifteenth"] = (15, "th"),
        ["sixteenth"] = (16, "th"), ["seventeenth"] = (17, "th"), ["eighteenth"] = (18, "th"),
        ["nineteenth"] = (19, "th")
    };

    private static readonly Dictionary<string, (int Value, string Suffix)> OrdinalTens = new(StringComparer.OrdinalIgnoreCase)
    {
        ["twentieth"] = (20, "th"), ["thirtieth"] = (30, "th"), ["fortieth"] = (40, "th"),
        ["fiftieth"] = (50, "th"), ["sixtieth"] = (60, "th"), ["seventieth"] = (70, "th"),
        ["eightieth"] = (80, "th"), ["ninetieth"] = (90, "th")
    };

    /// <summary>
    /// Returns the ordinal suffix for a number (1st, 2nd, 3rd, 4th, 11th, 12th, 13th, 21st, etc.)
    /// </summary>
    private static string GetOrdinalSuffix(int n)
    {
        int lastTwo = n % 100;
        if (lastTwo is >= 11 and <= 13) return "th";
        return (n % 10) switch
        {
            1 => "st",
            2 => "nd",
            3 => "rd",
            _ => "th"
        };
    }

    /// <summary>
    /// Checks whether a "one" at the given token index is in an exclusion context
    /// and should not be converted to a digit.
    /// </summary>
    private static bool IsExcludedOne(string[] tokens, int index)
    {
        string word = tokens[index];
        if (!word.Equals("one", StringComparison.OrdinalIgnoreCase)) return false;

        // Check next word: "one of", "one thing", "one by"
        if (index + 1 < tokens.Length)
        {
            string next = tokens[index + 1];
            if (next.Equals("of", StringComparison.OrdinalIgnoreCase) ||
                next.Equals("thing", StringComparison.OrdinalIgnoreCase) ||
                next.Equals("by", StringComparison.OrdinalIgnoreCase))
                return true;
        }

        // Check previous word: "for one", "the one", "no one"
        if (index > 0)
        {
            string prev = tokens[index - 1];
            if (prev.Equals("for", StringComparison.OrdinalIgnoreCase) ||
                prev.Equals("the", StringComparison.OrdinalIgnoreCase) ||
                prev.Equals("no", StringComparison.OrdinalIgnoreCase))
                return true;
        }

        return false;
    }

    /// <summary>
    /// Tries to parse a number word at the given token index.
    /// Returns the numeric value and how many tokens were consumed, or false if not a number word.
    /// </summary>
    private static bool TryParseNumberWord(string token, out int value)
    {
        if (Units.TryGetValue(token, out value)) return true;
        if (Tens.TryGetValue(token, out value)) return true;
        value = 0;
        return false;
    }

    /// <summary>
    /// Greedy token scanner that converts number word sequences to digits.
    /// Handles cardinals (including hundreds/thousands/millions) and ordinals.
    /// </summary>
    private static string ConvertNumbers(string text)
    {
        // Split on whitespace, preserving structure for reconstruction.
        // We need to handle hyphens in compound numbers like "twenty-three".
        // Strategy: tokenize by splitting on spaces, then handle hyphens within tokens.
        var parts = TokenizeForNumbers(text);
        var result = new List<string>();
        int i = 0;

        while (i < parts.Count)
        {
            // Try to consume a number sequence starting at i
            int consumed = TryConsumeNumber(parts, i, out string? replacement);
            if (consumed > 0 && replacement != null)
            {
                // Re-attach trailing punctuation from the last consumed token
                var lastToken = parts[i + consumed - 1];
                result.Add(replacement + lastToken.TrailingPunct);
                i += consumed;
            }
            else
            {
                result.Add(parts[i].Original);
                i++;
            }
        }

        return string.Join(" ", result);
    }

    private sealed record NumberToken(string Original, string Normalized, string TrailingPunct = "", string? HyphenSecond = null);

    private static List<NumberToken> TokenizeForNumbers(string text)
    {
        var tokens = new List<NumberToken>();
        // Split on whitespace
        var rawTokens = text.Split(' ', StringSplitOptions.RemoveEmptyEntries);

        foreach (var raw in rawTokens)
        {
            // Strip trailing punctuation so "one." normalizes to "one" with TrailingPunct="."
            string core = raw;
            string trailing = "";
            while (core.Length > 0 && char.IsPunctuation(core[^1]) && core[^1] != '-')
            {
                trailing = core[^1] + trailing;
                core = core[..^1];
            }

            // Check for hyphenated compound like "twenty-three"
            int hyphen = core.IndexOf('-');
            if (hyphen > 0 && hyphen < core.Length - 1)
            {
                string left = core[..hyphen];
                string right = core[(hyphen + 1)..];
                tokens.Add(new NumberToken(raw, left, trailing, right));
            }
            else
            {
                tokens.Add(new NumberToken(raw, core, trailing));
            }
        }

        return tokens;
    }

    /// <summary>
    /// Attempts to consume a number sequence starting at index i.
    /// Returns the number of tokens consumed and the replacement string.
    /// </summary>
    private static int TryConsumeNumber(List<NumberToken> tokens, int startIndex, out string? replacement)
    {
        replacement = null;
        int i = startIndex;
        var token = tokens[i];

        // First, check for "a hundred" / "a thousand" etc.
        bool startsWithA = token.Normalized.Equals("a", StringComparison.OrdinalIgnoreCase)
                           && i + 1 < tokens.Count
                           && Scales.ContainsKey(tokens[i + 1].Normalized);

        // Try to start a number sequence
        // The first token must be a number word, "a" before a scale, or a tens with hyphenated unit
        bool isNumberStart;
        if (startsWithA)
        {
            isNumberStart = true;
        }
        else if (TryParseNumberWord(token.Normalized, out _))
        {
            // Check exclusion for "one"
            if (IsExcludedOne(tokens.Select(t => t.Normalized).ToArray(), i))
                return 0;
            isNumberStart = true;
        }
        else if (Tens.ContainsKey(token.Normalized) || (token.HyphenSecond != null && Tens.ContainsKey(token.Normalized)))
        {
            isNumberStart = true;
        }
        else
        {
            // Check ordinals standalone
            int ordConsumed = TryConsumeOrdinal(tokens, startIndex, out replacement);
            return ordConsumed;
        }

        if (!isNumberStart) return 0;

        // Greedy scan: accumulate number value
        // Pattern: total = sum of groups, where each group is current * scale
        // e.g., "two thousand twenty six" = (2 * 1000) + (20 + 6) = 2026
        long total = 0;
        long current = 0;
        int consumed = 0;
        bool anyConsumed = false;

        while (i < tokens.Count)
        {
            var tok = tokens[i];
            string norm = tok.Normalized;

            // "a" as 1 before a scale word
            if (norm.Equals("a", StringComparison.OrdinalIgnoreCase))
            {
                if (i + 1 < tokens.Count && Scales.ContainsKey(tokens[i + 1].Normalized))
                {
                    current = 1;
                    i++;
                    consumed++;
                    anyConsumed = true;
                    continue;
                }
                else
                {
                    break;
                }
            }

            // "and" — skip if between number words
            if (norm.Equals("and", StringComparison.OrdinalIgnoreCase))
            {
                if (anyConsumed && i + 1 < tokens.Count && IsNumberContinuation(tokens[i + 1]))
                {
                    i++;
                    consumed++;
                    continue;
                }
                else
                {
                    break;
                }
            }

            // Check for ordinal at this position (compound ordinal like "twenty first")
            // Only if we already have accumulated some tens value
            if (anyConsumed)
            {
                int ordConsumed = TryConsumeOrdinalUnit(tokens, i, out int ordValue, out string? ordSuffix);
                if (ordConsumed > 0 && ordSuffix != null)
                {
                    current += ordValue;
                    total += current;
                    replacement = $"{total}{ordSuffix}";
                    return consumed + ordConsumed;
                }
            }

            // Hyphenated compound: "twenty-three" or "twenty-first"
            if (tok.HyphenSecond != null && Tens.TryGetValue(norm, out int tensVal))
            {
                string second = tok.HyphenSecond;
                // Check if second part is an ordinal
                if (OrdinalUnits.TryGetValue(second, out var ordInfo) && ordInfo.Value < 10)
                {
                    current += tensVal + ordInfo.Value;
                    total += current;
                    replacement = $"{total}{ordInfo.Suffix}";
                    return consumed + 1;
                }
                // Check if second part is a cardinal unit
                if (Units.TryGetValue(second, out int unitVal) && unitVal < 10)
                {
                    current += tensVal + unitVal;
                    i++;
                    consumed++;
                    anyConsumed = true;
                    continue;
                }
                // Just the tens part
            }

            // Scale words: hundred, thousand, million, billion
            if (Scales.TryGetValue(norm, out long scaleVal))
            {
                if (scaleVal == 100)
                {
                    // "hundred" multiplies current group
                    current = (current == 0 ? 1 : current) * 100;
                }
                else
                {
                    // thousand/million/billion: flush current group to total
                    current = (current == 0 ? 1 : current) * scaleVal;
                    total += current;
                    current = 0;
                }
                i++;
                consumed++;
                anyConsumed = true;
                continue;
            }

            // Units and teens
            if (Units.TryGetValue(norm, out int uVal))
            {
                // Check exclusion for "one"
                if (norm.Equals("one", StringComparison.OrdinalIgnoreCase) && !anyConsumed)
                {
                    if (IsExcludedOne(tokens.Select(t => t.Normalized).ToArray(), i))
                        break;
                }
                current += uVal;
                i++;
                consumed++;
                anyConsumed = true;
                continue;
            }

            // Tens
            if (Tens.TryGetValue(norm, out int tVal))
            {
                current += tVal;
                i++;
                consumed++;
                anyConsumed = true;

                // Look ahead for unit (non-hyphenated compound: "twenty three")
                if (i < tokens.Count)
                {
                    var nextTok = tokens[i];
                    // Check for ordinal unit next ("twenty first")
                    if (OrdinalUnits.TryGetValue(nextTok.Normalized, out var nextOrd) && nextOrd.Value < 10)
                    {
                        current += nextOrd.Value;
                        total += current;
                        replacement = $"{total}{nextOrd.Suffix}";
                        return consumed + 1;
                    }
                    if (Units.TryGetValue(nextTok.Normalized, out int nextUnit) && nextUnit > 0 && nextUnit < 10)
                    {
                        current += nextUnit;
                        i++;
                        consumed++;
                    }
                }
                continue;
            }

            // Not a number word — stop
            break;
        }

        if (!anyConsumed) return 0;

        total += current;
        replacement = total.ToString();
        return consumed;
    }

    /// <summary>
    /// Checks if a token could continue a number sequence (is a number word, scale, or "and").
    /// </summary>
    private static bool IsNumberContinuation(NumberToken token)
    {
        string norm = token.Normalized;
        return Units.ContainsKey(norm) || Tens.ContainsKey(norm) || Scales.ContainsKey(norm);
    }

    /// <summary>
    /// Try to consume a standalone ordinal at the given position (e.g., "first", "twentieth").
    /// </summary>
    private static int TryConsumeOrdinal(List<NumberToken> tokens, int startIndex, out string? replacement)
    {
        replacement = null;
        var tok = tokens[startIndex];

        // Hyphenated ordinal: "twenty-first"
        if (tok.HyphenSecond != null && Tens.TryGetValue(tok.Normalized, out int tVal))
        {
            if (OrdinalUnits.TryGetValue(tok.HyphenSecond, out var ordInfo) && ordInfo.Value < 10)
            {
                replacement = $"{tVal + ordInfo.Value}{ordInfo.Suffix}";
                return 1;
            }
        }

        // "twenty first" (two separate tokens)
        if (Tens.TryGetValue(tok.Normalized, out int tensVal) && startIndex + 1 < tokens.Count)
        {
            var next = tokens[startIndex + 1];
            if (OrdinalUnits.TryGetValue(next.Normalized, out var ordUnit) && ordUnit.Value < 10)
            {
                replacement = $"{tensVal + ordUnit.Value}{ordUnit.Suffix}";
                return 2;
            }
        }

        // Simple ordinal unit: "first", "second", etc.
        if (OrdinalUnits.TryGetValue(tok.Normalized, out var ordVal))
        {
            replacement = $"{ordVal.Value}{ordVal.Suffix}";
            return 1;
        }

        // Ordinal tens: "twentieth", "thirtieth", etc.
        if (OrdinalTens.TryGetValue(tok.Normalized, out var ordTens))
        {
            replacement = $"{ordTens.Value}{ordTens.Suffix}";
            return 1;
        }

        return 0;
    }

    /// <summary>
    /// Try to consume an ordinal unit at the given position (for compound numbers like "one hundred and first").
    /// Only matches unit-level ordinals (first-ninth).
    /// </summary>
    private static int TryConsumeOrdinalUnit(List<NumberToken> tokens, int index, out int value, out string? suffix)
    {
        value = 0;
        suffix = null;
        if (index >= tokens.Count) return 0;

        var tok = tokens[index];
        if (OrdinalUnits.TryGetValue(tok.Normalized, out var ordInfo))
        {
            value = ordInfo.Value;
            suffix = ordInfo.Suffix;
            return 1;
        }
        if (OrdinalTens.TryGetValue(tok.Normalized, out var ordTens))
        {
            value = ordTens.Value;
            suffix = ordTens.Suffix;
            return 1;
        }
        return 0;
    }

    // ── List Detection ────────────────────────────────────────────────

    /// <summary>
    /// Detects numbered item sequences (1, 2, 3, ...) in text and formats them
    /// as newline-separated lists. Requires at least 3 consecutive items starting
    /// from 1 to trigger. Runs after number conversion so numbers are already digits.
    /// </summary>
    private static string DetectLists(string text)
    {
        // Find the position of "1" at a word boundary
        var match = ListStartRegex().Match(text);
        if (!match.Success) return text;

        // Try to find consecutive numbers 1, 2, 3, ... with text between them
        var items = new List<(int Start, int End, string ItemText)>();
        int searchFrom = 0;

        // We may need to try multiple "1" positions if the first doesn't yield a list
        while (true)
        {
            match = ListStartRegex().Match(text, searchFrom);
            if (!match.Success) break;

            // Require the starting "1" to sit at a list-like anchor so narrative prose
            // ("I had 1 coffee, met 2 friends, walked 3 miles") doesn't get fragmented.
            // Valid anchors: start of string, after a newline, or after a colon.
            if (!IsAnchoredListStart(text, match.Index))
            {
                searchFrom = match.Index + 1;
                continue;
            }

            items.Clear();
            int currentNumber = 1;
            int numberPos = match.Index;

            // Find all consecutive numbers starting from this "1"
            while (true)
            {
                // Find where this number's text starts (after the number itself)
                string numberStr = currentNumber.ToString();
                int textStart = numberPos + numberStr.Length;

                // Look for the next number in sequence
                int nextNumber = currentNumber + 1;
                var nextPattern = BuildNumberBoundaryPattern(nextNumber);
                var nextMatch = nextPattern.Match(text, textStart);

                if (nextMatch.Success)
                {
                    // Extract text between current number and next number
                    string itemText = text[textStart..nextMatch.Index].Trim();
                    if (string.IsNullOrEmpty(itemText))
                    {
                        // No text between numbers — not a valid list item
                        break;
                    }
                    items.Add((numberPos, nextMatch.Index, itemText));
                    numberPos = nextMatch.Index;
                    currentNumber = nextNumber;
                }
                else
                {
                    // No next number found — this is the last item
                    // Extract text from current number to end of string
                    string itemText = text[textStart..].Trim();
                    if (string.IsNullOrEmpty(itemText))
                    {
                        break;
                    }
                    items.Add((numberPos, text.Length, itemText));
                    break;
                }
            }

            // Need at least 3 items to consider it a list
            if (items.Count >= 3)
            {
                return FormatDetectedList(text, items);
            }

            // Try the next "1" occurrence
            searchFrom = match.Index + 1;
        }

        return text;
    }

    /// <summary>
    /// Builds the formatted output for a detected list.
    /// </summary>
    private static string FormatDetectedList(string text, List<(int Start, int End, string ItemText)> items)
    {
        var sb = new StringBuilder();

        // Any text before the first item is a prefix
        string prefix = text[..items[0].Start].Trim();
        if (!string.IsNullOrEmpty(prefix))
        {
            sb.Append(prefix);
            sb.Append('\n');
        }

        for (int i = 0; i < items.Count; i++)
        {
            int number = i + 1;
            string itemText = items[i].ItemText;

            // Capitalize the first letter of the item text
            if (itemText.Length > 0 && char.IsLower(itemText[0]))
            {
                itemText = char.ToUpper(itemText[0]) + itemText[1..];
            }

            sb.Append($"{number}. {itemText}");
            if (i < items.Count - 1)
            {
                sb.Append('\n');
            }
        }

        return sb.ToString();
    }

    /// <summary>
    /// Creates a regex that matches a specific number at a word boundary.
    /// </summary>
    private static Regex BuildNumberBoundaryPattern(int number)
    {
        return new Regex($@"\b{number}\b");
    }

    // Matches "1" at a word boundary (the start of a potential list)
    [GeneratedRegex(@"\b1\b")]
    private static partial Regex ListStartRegex();

    /// <summary>
    /// True when the character immediately before <paramref name="index"/> (skipping
    /// horizontal whitespace) is the start of the string, a newline, or a colon —
    /// positions where a "1" is plausibly the head of a list rather than a count
    /// embedded in narrative prose.
    /// </summary>
    private static bool IsAnchoredListStart(string text, int index)
    {
        int i = index - 1;
        while (i >= 0 && (text[i] == ' ' || text[i] == '\t'))
            i--;

        if (i < 0) return true;
        return text[i] == '\n' || text[i] == ':';
    }

    // ── Currency ──────────────────────────────────────────────────────

    /// <summary>
    /// Converts spoken currency patterns to symbols. Processes most specific pattern first.
    /// </summary>
    private static string FormatCurrency(string text)
    {
        // Most specific first: "X dollars and Y cents"
        text = DollarsAndCentsRegex().Replace(text, m =>
        {
            string dollars = m.Groups[1].Value;
            string cents = m.Groups[2].Value.PadLeft(2, '0');
            return $"${dollars}.{cents}";
        });

        // "X dollars"
        text = DollarsRegex().Replace(text, "$$$1");

        // "X cents"
        text = CentsRegex().Replace(text, m =>
        {
            string cents = m.Groups[1].Value.PadLeft(2, '0');
            return $"$0.{cents}";
        });

        // "X bucks"
        text = BucksRegex().Replace(text, "$$$1");

        return text;
    }

    [GeneratedRegex(@"\b(\d+)\s+dollars\s+and\s+(\d+)\s+cents\b", RegexOptions.IgnoreCase)]
    private static partial Regex DollarsAndCentsRegex();

    [GeneratedRegex(@"\b(\d+)\s+dollars\b", RegexOptions.IgnoreCase)]
    private static partial Regex DollarsRegex();

    [GeneratedRegex(@"\b(\d+)\s+cents\b", RegexOptions.IgnoreCase)]
    private static partial Regex CentsRegex();

    [GeneratedRegex(@"\b(\d+)\s+bucks\b", RegexOptions.IgnoreCase)]
    private static partial Regex BucksRegex();

    // ── Percentages ───────────────────────────────────────────────────

    /// <summary>
    /// Converts "N percent" to "N%".
    /// </summary>
    private static string FormatPercentages(string text)
    {
        return PercentRegex().Replace(text, "$1%");
    }

    [GeneratedRegex(@"\b(\d+)\s+percent\b", RegexOptions.IgnoreCase)]
    private static partial Regex PercentRegex();

    // ── Dates ─────────────────────────────────────────────────────────

    private static readonly string[] MonthNames =
    [
        "January", "February", "March", "April", "May", "June",
        "July", "August", "September", "October", "November", "December"
    ];

    /// <summary>
    /// Formats dates: "Month Day Year" → "Month DayOrd, Year" and "Month Day" → "Month DayOrd".
    /// </summary>
    private static string FormatDates(string text)
    {
        // With year: "March 5 2026" → "March 5th, 2026"
        text = DateWithYearRegex().Replace(text, m =>
        {
            string month = m.Groups[1].Value;
            int day = int.Parse(m.Groups[2].Value);
            string year = m.Groups[3].Value;
            if (day < 1 || day > 31) return m.Value;
            return $"{month} {day}{GetOrdinalSuffix(day)}, {year}";
        });

        // Without year: "March 5" → "March 5th"
        text = DateWithoutYearRegex().Replace(text, m =>
        {
            string month = m.Groups[1].Value;
            string dayStr = m.Groups[2].Value;
            // Don't double-add ordinal if already has suffix
            if (m.Groups[3].Success && !string.IsNullOrEmpty(m.Groups[3].Value))
                return m.Value;
            int day = int.Parse(dayStr);
            if (day < 1 || day > 31) return m.Value;
            return $"{month} {day}{GetOrdinalSuffix(day)}";
        });

        return text;
    }

    // Match "Month Day Year" where Day is 1-2 digits and Year is 4 digits
    [GeneratedRegex(@"\b(January|February|March|April|May|June|July|August|September|October|November|December)\s+(\d{1,2})\s+(\d{4})\b", RegexOptions.None)]
    private static partial Regex DateWithYearRegex();

    // Match "Month Day" where Day is 1-2 digits, optionally already followed by ordinal suffix
    [GeneratedRegex(@"\b(January|February|March|April|May|June|July|August|September|October|November|December)\s+(\d{1,2})(st|nd|rd|th)?\b", RegexOptions.None)]
    private static partial Regex DateWithoutYearRegex();

    // ── Times ─────────────────────────────────────────────────────────

    /// <summary>
    /// Formats spoken time expressions into standard clock notation.
    /// </summary>
    private static string FormatTimes(string text)
    {
        // "3 30 PM" → "3:30 PM" (hour + minutes + AM/PM)
        text = TimeWithMinutesAndAmPmRegex().Replace(text, m =>
        {
            string hour = m.Groups[1].Value;
            string minutes = m.Groups[2].Value;
            string ampm = NormalizeAmPm(m.Groups[3].Value);
            return $"{hour}:{minutes} {ampm}";
        });

        // "3 PM" → "3:00 PM" (hour + AM/PM, no minutes)
        // Uses negative lookbehind to avoid matching digits after a colon (already formatted times)
        text = TimeWithAmPmRegex().Replace(text, m =>
        {
            string hour = m.Groups[1].Value;
            string ampm = NormalizeAmPm(m.Groups[2].Value);
            return $"{hour}:00 {ampm}";
        });

        // "3 o'clock" → "3:00"
        text = TimeOClockRegex().Replace(text, "$1:00");

        // "noon" → "12:00 PM"
        text = NoonRegex().Replace(text, "12:00 PM");

        // "midnight" → "12:00 AM"
        text = MidnightRegex().Replace(text, "12:00 AM");

        return text;
    }

    /// <summary>
    /// Normalizes AM/PM variants (am, pm, a.m., p.m.) to uppercase AM/PM.
    /// </summary>
    private static string NormalizeAmPm(string value)
    {
        string lower = value.ToLowerInvariant().Replace(".", "");
        return lower == "am" ? "AM" : "PM";
    }

    // "3 30 PM" or "3 30 am" or "3 30 a.m."
    [GeneratedRegex(@"\b(\d{1,2})\s+(\d{2})\s*(AM|PM|am|pm|a\.m\.|p\.m\.)\b", RegexOptions.None)]
    private static partial Regex TimeWithMinutesAndAmPmRegex();

    // "3 PM" or "3 am" — negative lookbehind prevents matching after colon (already formatted)
    [GeneratedRegex(@"(?<![:]\d*)(\d{1,2})\s*(AM|PM|am|pm|a\.m\.|p\.m\.)\b", RegexOptions.None)]
    private static partial Regex TimeWithAmPmRegex();

    // "3 o'clock"
    [GeneratedRegex(@"\b(\d{1,2})\s*o'clock\b", RegexOptions.IgnoreCase)]
    private static partial Regex TimeOClockRegex();

    // "noon" as whole word
    [GeneratedRegex(@"\bnoon\b", RegexOptions.IgnoreCase)]
    private static partial Regex NoonRegex();

    // "midnight" as whole word
    [GeneratedRegex(@"\bmidnight\b", RegexOptions.IgnoreCase)]
    private static partial Regex MidnightRegex();

    // ── Email Assembly ────────────────────────────────────────────────

    /// <summary>
    /// Assembles spoken email addresses: "user at domain dot com" → "user@domain.com".
    /// Must run before URL assembly since "at" could be confused with URL patterns.
    /// </summary>
    private static string AssembleEmails(string text)
    {
        return EmailRegex().Replace(text, m =>
        {
            string user = m.Groups[1].Value;
            string domainPart = m.Groups[2].Value;

            // Replace "dot" with "." in domain, collapse spaces around it
            string domain = DotWordRegex().Replace(domainPart, ".");

            // Remove any remaining spaces (between domain segments)
            domain = domain.Replace(" ", "");

            string email = $"{user}@{domain}";

            // Validate: exactly one "@" and domain has at least one "."
            if (email.Count(c => c == '@') == 1 && domain.Contains('.'))
                return email;

            return m.Value; // Don't transform if invalid
        });
    }

    // Matches "word at word dot word" with optional additional "dot word" segments
    [GeneratedRegex(@"(\S+)\s+at\s+(\S+(?:\s+dot\s+\S+)+)", RegexOptions.IgnoreCase)]
    private static partial Regex EmailRegex();

    // Matches " dot " as a word separator in domain parts
    [GeneratedRegex(@"\s+dot\s+", RegexOptions.IgnoreCase)]
    private static partial Regex DotWordRegex();

    // ── URL Assembly ──────────────────────────────────────────────────

    /// <summary>
    /// Assembles spoken URLs: protocol prefixes, www, TLDs, and path slashes.
    /// Note: spoken punctuation may have already converted "colon" to ":" before this runs,
    /// so we match both the spoken and already-converted forms.
    /// </summary>
    private static string AssembleUrls(string text)
    {
        // 1. Replace URL prefixes
        text = WwwSpacedDotRegex().Replace(text, "www.");
        text = WwwDotRegex().Replace(text, "www.");
        text = HttpsColonSlashRegex().Replace(text, "https://");
        text = HttpColonSlashRegex().Replace(text, "http://");

        // 2. Replace common TLD patterns: "dot com", "dot org", etc.
        text = DotTldRegex().Replace(text, m => "." + m.Groups[1].Value.ToLowerInvariant());

        // 3. Replace "slash" with "/" when it follows a domain-like pattern (word.tld)
        text = UrlSlashRegex().Replace(text, m => m.Groups[1].Value + "/" + m.Groups[2].Value);

        return text;
    }

    // "w w w dot" — literal spaced-out letters
    [GeneratedRegex(@"\bw\s+w\s+w\s+dot\s+", RegexOptions.IgnoreCase)]
    private static partial Regex WwwSpacedDotRegex();

    // "www dot"
    [GeneratedRegex(@"\bwww\s+dot\s+", RegexOptions.IgnoreCase)]
    private static partial Regex WwwDotRegex();

    // "https colon slash slash" or "https: slash slash" (colon may already be converted by spoken punct)
    [GeneratedRegex(@"\bhttps\s*(?:colon|:)\s*(?:slash\s+slash|//)\s*", RegexOptions.IgnoreCase)]
    private static partial Regex HttpsColonSlashRegex();

    // "http colon slash slash" or "http: slash slash"
    [GeneratedRegex(@"\bhttp\s*(?:colon|:)\s*(?:slash\s+slash|//)\s*", RegexOptions.IgnoreCase)]
    private static partial Regex HttpColonSlashRegex();

    // "dot com", "dot org", "dot net", "dot io", "dot edu", "dot gov", "dot co"
    [GeneratedRegex(@"\s+dot\s+(com|org|net|io|edu|gov|co)\b", RegexOptions.IgnoreCase)]
    private static partial Regex DotTldRegex();

    // "slash" after a domain pattern (word.tld) followed by a path segment
    [GeneratedRegex(@"(\.\w+)\s+slash\s+(\S+)", RegexOptions.IgnoreCase)]
    private static partial Regex UrlSlashRegex();

    // ── Phone Number Formatting ───────────────────────────────────────

    /// <summary>
    /// Formats sequences of space-separated single digits into phone numbers.
    /// 10 digits → (XXX) XXX-XXXX, 7 digits → XXX-XXXX.
    /// </summary>
    private static string FormatPhoneNumbers(string text)
    {
        // Match 10 space-separated single digits (not preceded or followed by another digit)
        text = TenDigitPhoneRegex().Replace(text, m =>
        {
            string digits = StripSpaces(m.Value);
            return $"({digits[..3]}) {digits[3..6]}-{digits[6..]}";
        });

        // Match 7 space-separated single digits
        text = SevenDigitPhoneRegex().Replace(text, m =>
        {
            string digits = StripSpaces(m.Value);
            return $"{digits[..3]}-{digits[3..]}";
        });

        return text;
    }

    private static string StripSpaces(string s) => s.Replace(" ", "");

    // 10 space-separated single digits at word boundaries
    [GeneratedRegex(@"(?<!\d)(\d\s+\d\s+\d\s+\d\s+\d\s+\d\s+\d\s+\d\s+\d\s+\d)(?!\s*\d)", RegexOptions.None)]
    private static partial Regex TenDigitPhoneRegex();

    // 7 space-separated single digits at word boundaries
    [GeneratedRegex(@"(?<!\d)(\d\s+\d\s+\d\s+\d\s+\d\s+\d\s+\d)(?!\s*\d)", RegexOptions.None)]
    private static partial Regex SevenDigitPhoneRegex();

    // ── Basic Cleanup ──────────────────────────────────────────────────

    private string ApplyBasicCleanup(string text)
    {
        // Collapse horizontal whitespace (not newlines) to single space
        text = CollapseHorizontalSpaceRegex().Replace(text, " ");

        // Remove space before punctuation
        text = SpaceBeforePunctRegex().Replace(text, "$1");

        // Ensure space after punctuation when followed by a letter (but not after newlines or within \n sequences)
        text = SpaceAfterPunctRegex().Replace(text, "$1 $2");

        // Capitalize first character of each sentence (after . ? ! and after newlines)
        text = SentenceStartRegex().Replace(text, m =>
            m.Groups[1].Value + char.ToUpper(m.Groups[2].Value[0]));

        // Capitalize after newlines
        text = NewlineSentenceStartRegex().Replace(text, m =>
            m.Groups[1].Value + char.ToUpper(m.Groups[2].Value[0]));

        // Trim each line
        text = TrimLines(text);

        // Capitalize first character of overall string
        text = text.TrimStart();
        if (text.Length > 0 && char.IsLower(text[0]))
            text = char.ToUpper(text[0]) + text[1..];

        text = text.TrimEnd();

        return text;
    }

    private static string TrimLines(string text)
    {
        if (!text.Contains('\n')) return text.Trim();

        var lines = text.Split('\n');
        for (int i = 0; i < lines.Length; i++)
            lines[i] = lines[i].Trim();
        return string.Join('\n', lines);
    }

    // Collapse horizontal whitespace (spaces, tabs) but preserve newlines
    [GeneratedRegex(@"[^\S\n]+")]
    private static partial Regex CollapseHorizontalSpaceRegex();

    // Remove space before punctuation marks
    [GeneratedRegex(@"\s+([,.\?!:;])")]
    private static partial Regex SpaceBeforePunctRegex();

    // Ensure space after punctuation when directly followed by a letter
    [GeneratedRegex(@"([,.\?!:;])([A-Za-z])")]
    private static partial Regex SpaceAfterPunctRegex();

    // Capitalize after sentence-ending punctuation + space
    [GeneratedRegex(@"([.!?]\s+)([a-z])")]
    private static partial Regex SentenceStartRegex();

    // Capitalize after newlines
    [GeneratedRegex(@"(\n+\s*)([a-z])")]
    private static partial Regex NewlineSentenceStartRegex();
}
