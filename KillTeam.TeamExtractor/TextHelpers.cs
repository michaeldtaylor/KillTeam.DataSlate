using System.Text;
using System.Text.RegularExpressions;

namespace KillTeam.TeamExtractor;

/// <summary>
/// Shared text normalisation helpers used by both the PDF extractor and the YAML output writer.
/// </summary>
internal static partial class TextHelpers
{
    private static readonly HashSet<string> LowercaseWords = new(StringComparer.OrdinalIgnoreCase)
    {
        "of", "the", "a", "an", "and", "but", "or", "for", "nor",
        "at", "to", "by", "in", "up", "as", "via", "with",
    };

    internal static readonly string[] ConstraintSentencePatterns =
    [
        "This operative cannot perform this action",
        "An operative cannot perform this action",
        "This operative cannot perform this ability",
    ];

    /// <summary>
    /// Normalises a string value for output: smart quotes, AP concatenation repair,
    /// trademark symbols, PDF chrome, and control characters.
    /// </summary>
    public static string NormaliseText(string s)
    {
        // Strip ASCII control chars (BEL 0x07, BS 0x08, etc.) but preserve \n
        s = new string(s.Where(c => c >= 32 || c == '\n').ToArray());

        s = s
            .Replace("\u0393\u00C7\u00F3", "\u2022") // mojibake for bullet (ΓÇó → •)
            .Replace('\u2019', '\'')               // right single quotation mark → apostrophe
            .Replace('\u2018', '\'')               // left single quotation mark → apostrophe
            .Replace('\u201C', '"')                // left double quotation mark
            .Replace('\u201D', '"')                // right double quotation mark
            .Replace("\u0393\u00C7\u00D6", "'")   // pdftotext mojibake of right single quote (ΓÇÖ → ')
            .Replace("CONTINUES ON OTHER SIDE", "", StringComparison.OrdinalIgnoreCase)
            .Replace("\u00AE", "")     // ®
            .Replace("\u2122", "");    // ™

        s = ApConcatPattern().Replace(s, m => m.Groups[1].Value + " " + m.Groups[2].Value);

        return s.Trim();
    }

    /// <summary>
    /// Converts a string to title case following Kill Team naming conventions.
    /// Mid-name prepositions and conjunctions are lower-cased; first and last words are always capitalised.
    /// Handles hyphenated compound words by title-casing each hyphen-separated segment.
    /// </summary>
    public static string ToTitleCase(string s)
    {
        if (string.IsNullOrWhiteSpace(s))
        {
            return s;
        }

        var words = s.Split(' ');

        for (var i = 0; i < words.Length; i++)
        {
            var w = words[i];

            if (w.Length == 0)
            {
                continue;
            }

            // First and last words are always capitalised
            if (i == 0 || i == words.Length - 1)
            {
                words[i] = TitleCaseWord(w);
            }
            else if (LowercaseWords.Contains(w))
            {
                words[i] = w.ToLowerInvariant();
            }
            else
            {
                words[i] = TitleCaseWord(w);
            }
        }

        return string.Join(' ', words);
    }

    /// <summary>
    /// Title-cases a single word, handling hyphenated compounds by title-casing each segment.
    /// E.g. "VOID-DANCER" → "Void-Dancer".
    /// </summary>
    private static string TitleCaseWord(string word)
    {
        if (!word.Contains('-'))
        {
            return word.Length == 0 ? word : char.ToUpperInvariant(word[0]) + word[1..].ToLowerInvariant();
        }

        var parts = word.Split('-');

        for (var k = 0; k < parts.Length; k++)
        {
            var p = parts[k];
            parts[k] = p.Length == 0 ? p : char.ToUpperInvariant(p[0]) + p[1..].ToLowerInvariant();
        }

        return string.Join('-', parts);
    }

    [GeneratedRegex(@"([A-Za-z])(\d+AP)\b")]
    private static partial Regex ApConcatPattern();

    // ─── StructureToMarkdown ──────────────────────────────────────────────────────

    /// <summary>
    /// Processes a block of PDF-extracted text through a structured Markdown pipeline:
    /// <list type="number">
    ///   <item>NormaliseText</item>
    ///   <item>Strip PDF chrome (lone page numbers, OPERATIVES header)</item>
    ///   <item>Strip type prefix labels (PSYCHIC., STRATEGIC GAMBIT, ONCE PER BATTLE., …)</item>
    ///   <item>Split inline numbered lists onto separate lines</item>
    ///   <item>Format numbered list items (ALL-CAPS name → bold title case)</item>
    ///   <item>ALL-CAPS headings/labels → <c>**Title Case**</c> (colon, line-heading, inline multi-word)</item>
    ///   <item>Bullet symbol hierarchy → Markdown (<c>- </c>, <c>  - </c>, <c>    - </c>)</item>
    ///   <item>Insert paragraph break before constraint sentences</item>
    /// </list>
    /// </summary>
    public static string StructureToMarkdown(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return text;
        }

        // Step 1: Normalise (smart quotes, AP repair, trademark strip, trim)
        text = NormaliseText(text);

        // Step 2: Strip PDF chrome
        // NormaliseText already stripped "CONTINUES ON OTHER SIDE".
        text = LonePageNumberRegex().Replace(text, "");
        text = OperativesHeaderRegex().Replace(text, "");

        // Step 3: Strip type prefix labels at the very start of the text
        text = TypePrefixRegex().Replace(text.TrimStart(), "").TrimStart();

        // Step 4: Split inline numbered lists
        // "text 2. DUELLER…" → "text\n2. DUELLER…"
        text = InlineNumberedListSplitRegex().Replace(
            text,
            m => "\n" + m.Groups[1].Value + ". ");

        // Step 5: Format numbered list items — ALL-CAPS name → bold, preserving capitalisation
        // "1. AGGRESSIVE This…" → "1. **AGGRESSIVE** This…"
        text = NumberedListItemRegex().Replace(
            text,
            m => m.Groups[1].Value + ". **" + m.Groups[2].Value.TrimEnd() + "**");

        // Step 6a: ALL-CAPS phrase + colon (anywhere) → **ALLCAPS:** preserving capitalisation.
        // The leading \*\*[^*]+\*\* alternative skips already-bolded spans (from pre-processing),
        // so group 1 only participates for plain ALL-CAPS text.
        text = AllCapsWithColonRegex().Replace(
            text,
            m => m.Groups[1].Success ? "**" + m.Groups[1].Value.Trim() + ":**" : m.Value);

        // Step 6b: ALL-CAPS phrase at start of line + space + body text → **ALLCAPS**
        text = AllCapsLineHeadingRegex().Replace(
            text,
            m => "**" + m.Groups[1].Value.Trim() + "** ");

        // Step 6c: Inline ALL-CAPS multi-word sequences → **ALLCAPS** bold, preserving capitalisation.
        // Matches 2+ contiguous ALL-CAPS words. The leading \*\*[^*]+\*\* alternative skips
        // already-bolded spans (from Steps 6a/6b), so group 1 only participates for plain ALL-CAPS.
        text = AllCapsMultiWordRegex().Replace(
            text,
            m => m.Groups[1].Success ? "**" + m.Groups[1].Value + "**" : m.Value);

        // Step 7: Unicode bullet symbols → Markdown hierarchy
        text = FormatBulletSymbols(text);

        // Step 8: Convert constraint sentences to bullet list items.
        // Abilities/actions with a constraint ("This operative cannot perform this action...")
        // display as two icon-marked sections on the card (▶ effect, ◆ constraint).
        // Represent these as a Markdown bullet list. Only fires after '.' to avoid
        // breaking quoted text like "Changed to read: 'This operative cannot...'".
        var hasConstraintBullet = false;
        foreach (var pattern in ConstraintSentencePatterns)
        {
            var before = text;
            text = text.Replace(". " + pattern, ".\n- " + pattern, StringComparison.OrdinalIgnoreCase);
            if (text != before)
            {
                hasConstraintBullet = true;
            }
        }

        // If a constraint bullet was inserted, also prefix the first paragraph with "- "
        // to create a consistent two-item bullet list (effect + constraint).
        if (hasConstraintBullet)
        {
            text = "- " + text;
        }

        // Sentence-start paragraph breaks: specific Kill Team phrasings that begin a new
        // paragraph in the PDF but arrive joined to the preceding sentence by a space.
        text = text.Replace(". Your kill team", ".\n\nYour kill team");
        text = text.Replace(". Use this ", ".\n\nUse this ");
        text = text.Replace(". When selecting ", ".\n\nWhen selecting ");
        text = text.Replace(". Designer's Note:", ".\n\nDesigner's Note:");
        // Equipment: lore paragraph → rule trigger (no blank line in PDF, must be inserted)
        text = text.Replace(". You can use ", ".\n\nYou can use ");
        text = text.Replace(". Once per ", ".\n\nOnce per ");
        text = text.Replace(". When this equipment ", ".\n\nWhen this equipment ");

        // Collapse 3+ consecutive newlines to a single paragraph break
        text = MultipleBlankLinesRegex().Replace(text, "\n\n");

        return text.TrimStart().TrimEnd();
    }

    /// <summary>
    /// Processes text line by line, converting Unicode bullet symbols to Markdown list syntax
    /// and tracking list depth for correct indentation.
    /// Already-formatted Markdown bullet lines (<c>- </c>, <c>  - </c>, <c>    - </c>) are
    /// passed through unchanged so that text pre-processed by <see cref="NormaliseText"/> is not
    /// double-converted.
    /// </summary>
    private static string FormatBulletSymbols(string text)
    {
        var lines = text.Split('\n');
        var result = new StringBuilder(text.Length);
        var currentItem = new StringBuilder();
        var currentDepth = 0;

        void FlushItem()
        {
            if (currentItem.Length > 0)
            {
                result.Append(currentItem).Append('\n');
                currentItem.Clear();
            }
        }

        foreach (var rawLine in lines)
        {
            var line = rawLine.TrimEnd();

            if (string.IsNullOrWhiteSpace(line))
            {
                FlushItem();
                result.Append('\n');
                currentDepth = 0;
                continue;
            }

            // Already-formatted Markdown bullets — pass through and update depth tracker
            if (line.StartsWith("    - ", StringComparison.Ordinal))
            {
                FlushItem();
                currentItem.Append(line);
                currentDepth = 3;
            }
            else if (line.StartsWith("  - ", StringComparison.Ordinal))
            {
                FlushItem();
                currentItem.Append(line);
                currentDepth = 2;
            }
            else if (line.StartsWith("- ", StringComparison.Ordinal))
            {
                FlushItem();
                currentItem.Append(line);
                currentDepth = 1;
            }
            // Unicode directional arrows → level-1 bullet
            else if (line[0] is '\u2198' or '\u2199' or '\u21B3') // ↘ ↙ ↳
            {
                FlushItem();
                currentItem.Append("- ").Append(line[1..].TrimStart());
                currentDepth = 1;
            }
            // Filled bullet (•) → level-2
            else if (line[0] == '\u2022')
            {
                FlushItem();
                currentItem.Append("  - ").Append(line[1..].TrimStart());
                currentDepth = 2;
            }
            // Hollow circle (○) → level-3 under bullets, level-2 under arrows
            else if (line[0] == '\u25CB')
            {
                FlushItem();
                var prefix = currentDepth >= 2 ? "    - " : "  - ";
                currentItem.Append(prefix).Append(line[1..].TrimStart());
            }
            else
            {
                var trimmedLine = line.TrimStart();

                // A numbered list item (e.g. "6. **Hardy** Whenever…") always starts a new block —
                // even when depth > 0 — so it is never treated as a bullet continuation.
                var isNumberedItem = trimmedLine.Length >= 3
                    && char.IsAsciiDigit(trimmedLine[0])
                    && trimmedLine[1] == '.'
                    && trimmedLine[2] == ' ';

                if (currentDepth > 0 && currentItem.Length > 0 && !isNumberedItem)
                {
                    // Continuation of the current list item
                    currentItem.Append(' ').Append(trimmedLine);
                }
                else
                {
                    // Regular prose line or new numbered item
                    FlushItem();
                    currentItem.Append(trimmedLine);
                    currentDepth = 0;
                }
            }
        }

        FlushItem();

        return result.ToString().TrimEnd('\n', '\r', ' ');
    }

    // ─── StructureToMarkdown regexes ──────────────────────────────────────────────

    /// <summary>Lines containing only digits (lone page numbers).</summary>
    [GeneratedRegex(@"(?m)^\d+\s*$")]
    private static partial Regex LonePageNumberRegex();

    /// <summary>"OPERATIVES" as a standalone line (operative-selection section header).</summary>
    [GeneratedRegex(@"(?m)^OPERATIVES\s*$", RegexOptions.IgnoreCase)]
    private static partial Regex OperativesHeaderRegex();

    /// <summary>
    /// Type-prefix labels that may appear at the very start of ability/ploy text:
    /// PSYCHIC, STRATEGIC GAMBIT, ONCE PER BATTLE, ONCE PER TURNING POINT.
    /// </summary>
    [GeneratedRegex(
        @"^(PSYCHIC\.?\s+|STRATEGIC GAMBIT\.?\s+|ONCE PER BATTLE\.\s+|ONCE PER TURNING POINT\.\s+)",
        RegexOptions.IgnoreCase)]
    private static partial Regex TypePrefixRegex();

    /// <summary>
    /// Inline numbered-list boundary: whitespace + digit(s) + ". " immediately followed by
    /// at least two uppercase letters (beginning of an ALL-CAPS list-item name).
    /// </summary>
    [GeneratedRegex(@"\s+(\d+)\.\s+(?=[A-Z]{2})")]
    private static partial Regex InlineNumberedListSplitRegex();

    /// <summary>
    /// A numbered-list item at the start of a line: "1. ALL CAPS NAME".
    /// Captures the number and the ALL-CAPS name (only uppercase letters, hyphens, apostrophes).
    /// Each word in the name must contain at least two uppercase characters to avoid
    /// accidentally capturing the first letter of the following sentence.
    /// </summary>
    [GeneratedRegex(@"^(\d+)\.\s+([A-Z][A-Z'\-]+(?:\s+[A-Z][A-Z'\-]+)*)", RegexOptions.Multiline)]
    private static partial Regex NumberedListItemRegex();

    /// <summary>
    /// An ALL-CAPS phrase (4+ chars) immediately followed by a colon — a sub-section label.
    /// Leading alternative skips already-bolded spans so they are not double-processed.
    /// </summary>
    [GeneratedRegex(@"\*\*[^*]+\*\*|([A-Z][A-Z'\-]{3,}(?:\s+[A-Z][A-Z'\-]+)*):")] 
    private static partial Regex AllCapsWithColonRegex();

    /// <summary>
    /// An ALL-CAPS phrase at the start of a line followed by space + text, but NOT directly
    /// followed by faction-keyword qualifiers (operative / friendly / enemy).
    /// </summary>
    [GeneratedRegex(
        @"^([A-Z][A-Z'\-]{3,}(?:\s+[A-Z][A-Z'\-]+)*)(?=\s+(?!operative\b|friendly\b|enemy\b)[A-Za-z])",
        RegexOptions.Multiline)]
    private static partial Regex AllCapsLineHeadingRegex();

    /// <summary>Three or more consecutive newlines collapsed to a paragraph break.</summary>
    [GeneratedRegex(@"\n{3,}")]
    private static partial Regex MultipleBlankLinesRegex();

    /// <summary>
    /// Two or more contiguous ALL-CAPS words (possibly hyphenated or with apostrophes)
    /// appearing inline in prose. Used by Step 6c to convert them to bold, preserving capitalisation.
    /// Single-word abbreviations (APL, CP, etc.) never match because the pattern requires 2+ words.
    /// The leading alternative <c>\*\*[^*]+\*\*</c> matches already-bolded spans first so they are
    /// skipped (returned as-is), preventing double-bolding of text processed by Steps 6a/6b.
    /// </summary>
    [GeneratedRegex(@"\*\*[^*]+\*\*|([A-Z][A-Z'\-]+(?:\s+[A-Z][A-Z'\-]+)+)")]
    private static partial Regex AllCapsMultiWordRegex();
}
