# Kill Team DataSlate — Parsing Rules

These rules govern how PDF text is extracted and formatted into YAML output.
All future parsing decisions must follow this document.

## Rule 1 — Text Normalisation (all string fields)

Applied to every string value before output:

- Smart apostrophes `'` (U+2019) and `'` (U+2018) → `'` (U+0027, ASCII apostrophe)
- Smart quotes `"` (U+201C) and `"` (U+201D) → `"` (U+0022, ASCII double quote)
- AP concatenation repair: regex `([A-Za-z])(\d+AP\b)` → `$1 $2` (e.g. "OPTICS1AP" → "OPTICS 1AP")
- Strip "CONTINUES ON OTHER SIDE" (PDF pagination chrome)
- Strip `®` (U+00AE) and `™` (U+2122) trademark symbols
- Strip lone page numbers (isolated numeric lines)

## Rule 2 — Title Case for Names

Applied to: ability names, ploy names, equipment names, rule names, operative names.
NOT applied to free-text description/body fields.

- Lowercase mid-name prepositions and conjunctions: of, the, a, an, and, but, or, for, nor, at, to, by, in, up, as, via, with
- First word and last word are always capitalised regardless
- Examples: "Mirror of Minds", "And They Shall Know No Fear", "Fog of Dreams", "Chapter of the Rose"

## Rule 3 — Markdown in Complex Text Fields

Text description fields (ability.text, ploy.text, rule.text, operativeSelection.text, supplementaryInformation) use GitHub-compatible Markdown.

Goal: capture content and structure for later rendering. Never sacrifice content for formatting.

### Bold

- ALL-CAPS proper nouns (operative types, faction keywords) appearing inline in prose → `**Title Cased**`
  - Examples: `DEATH JESTER` → `**Death Jester**`, `VOID-DANCER TROUPE` → `**Void-Dancer Troupe**`, `ANGEL OF DEATH` → `**Angel of Death**`
  - Hyphenated ALL-CAPS names keep their hyphen: `VOID-DANCER` → `**Void-Dancer**`
- AP cost values inline in text → `**1AP**`, `**2AP**`
  - After AP concatenation repair so input is already "1AP" not "1AP"

### Lists (3 levels, all using `-` with 2-space indentation per level)

| PDF symbol | Unicode | Markdown |
|-----------|---------|----------|
| `↘` / `↙` / `↳` arrow | U+2198 / U+2199 / U+21B3 | `- ` (level 1, 0 indent) |
| `•` filled circle | U+2022 | `  - ` (level 2, 2-space indent) |
| `○` hollow circle | U+25CB | `    - ` (level 3, 4-space indent, when under `•`) OR `  - ` (level 2, when directly under arrow) |

### Line joining

- **Word-wrap continuation**: a line with no bullet/symbol at the start, following a list item or previous text → joined to previous text with a single space
- **New bullet** → new list item (preceded by newline)
- **Blank line** in source → paragraph break (`\n\n`)

## Rule 4 — Strip PDF Chrome

Remove from all extracted text:
- "CONTINUES ON OTHER SIDE" (and variants)
- "OPERATIVES" section heading when it appears at the top of operative selection body text
- `®` and `™` symbols
- Isolated page numbers (a line containing only digits)

## Rule 5 — All 8 PDFs Must Be Captured

Every team folder contains exactly 8 PDFs. All produce required output:

| PDF filename pattern | YAML field | Notes |
|---------------------|------------|-------|
| `*Datacards*` | `datacards[]` | Operative stats, weapons, abilities, special rules |
| `*Faction Equipment*` | `factionEquipment[]` | Equipment items with `name` and `text` fields |
| `*Faction Rules*` | `factionRules[]` | Named rules with text |
| `*Firefight Ploys*` | `firefightPloys[]` | Named ploys with text |
| `*Operative Selection*` | `operativeSelection{}` | Archetype + Markdown composition rules |
| `*Strategy Ploys*` | `strategyPloys[]` | Named ploys with text |
| `*Supplementary Information*` | `supplementaryInformation` | Raw Markdown text (errata, FAQs) |
| `*Universal Equipment*` | `universalEquipment[]` | Equipment items with `name` and `text` fields |

## Rule 6 — YAML Field Order

Metadata first, then PDF-sourced fields in alphabetical PDF filename order:

```
id, name, grandFaction, faction   (metadata header)
datacards                          (Datacards)
factionEquipment                   (Faction Equipment)
factionRules                       (Faction Rules)
firefightPloys                     (Firefight Ploys)
operativeSelection                 (Operative Selection)
strategyPloys                      (Strategy Ploys)
supplementaryInformation                  (Supplementary Information)
universalEquipment                 (Universal Equipment)
```

### Faction and grandFaction derivation

Both are extracted from the keyword line of the first operative on the Datacards PDF. The keyword line is all-caps, comma-separated, e.g.:

```
ANGEL OF DEATH, IMPERIUM, ADEPTUS ASTARTES, FIGHTER
```

| Index | Value | YAML field |
|-------|-------|------------|
| `[0]` | Operative type keyword | (used as `primaryKeyword`) |
| `[1]` | Grand faction | `grandFaction` (e.g. `Imperium`, `Chaos`) |
| `[2]` | Sub-faction / battle faction | `faction` (e.g. `Adeptus Astartes`) |

If fewer than 3 keywords are found, the fallback value `"UNKNOWN — UPDATE ME"` is written.

---

## Rule 7 — PdfPig: Dual-Mode Extraction for Datacards, Content-Order for Prose

All PDF text extraction uses PdfPig (pure .NET, no external dependencies). Datacards require **two** extraction modes:

| Mode | PdfPig API | Used for |
|------|------------|----------|
| Layout | `page.GetWords()` spatial grouping | `ParseDatacards` — weapon stats rows require spatially-fixed column positions for the stats-header regex |
| Raw (content order) | `ContentOrderTextExtractor.GetText(page)` | Back-card prose (abilities, 1AP actions, footnote rules) in `ParseDatacards`; `ParseRulesDoc`; `ParseEquipmentWithDescriptions`; `ParseSupplementaryInformation`; `ParseOperativeSelection` |

**Why content-order mode for back-card prose:** Two-column card layouts in spatial grouping interleave text from left and right columns at the same Y position (e.g. a single operative's ability text is interleaved with a neighbouring operative's ability text). Content-order mode reads text in PDF content stream order — left column fully, then right column — which unambiguously separates one operative's abilities from another's.

### Split-Word Merge (Layout Mode)

Kill Team PDFs render the first letter of some words as separate text objects (different `TJ`/`Tj` operators). PdfPig's word extractor sees them as two words (e.g. `R` + `ange`, `P` + `SYCHIC`). `BuildLayoutLineText` merges a single letter closely followed by the rest of the word when the gap is less than 2× average character width.

### Weapon Rule Continuation Detection

When a weapon row's rules text ends with a trailing comma, the next non-blank line is treated as a continuation (appended to the rules). This handles cases where PdfPig places long rule lists across multiple lines (e.g. `Heavy (Dash only), Piercing 1,` followed by `Silent`).

### Strikethrough Detection

`GetStruckPhrases` uses PdfPig's `page.ExperimentalAccess.Paths` to find thin horizontal lines (height ≤ 1pt) drawn over text. Each letter on the page is checked against these strikethrough rectangles to determine which characters are struck through.

### Datacard Dual-Pass Strategy

`ParseDatacards` calls `BuildRawBackCardSections` which scans the raw-mode lines and returns **two** separate lookups (operative name → raw ability lines):

| Dictionary | Trigger | Consumer |
|------------|---------|----------|
| `FrontOnlySections` | Weapon-table header `NAME ATK HIT DMG WR` (single-page operatives, Plague Marines pattern) | First layout-mode occurrence of the operative |
| `BackCardSections` | `RULES CONTINUE ON OTHER SIDE` (two-page operatives, Angels of Death pattern) | `ParseBackOfCard` (second layout-mode occurrence) |

In both variants the ALL-CAPS operative name is the **end** of the raw block (not the start). Sections that contain no parseable ability, 1AP action, or footnote rule (`ContainsParsableContent` = false) are discarded; those operatives fall back to layout-mode parsing.

Faction keyword lines (all-caps, comma-separated, e.g. `PLAGUE MARINE , CHAOS, HERETIC ASTARTES, FIGHTER`) appear between the last ability and the operative name in raw mode — they are skipped during block collection.

---

## Rule 8 — StructureToMarkdown Pipeline

`TextHelpers.StructureToMarkdown(string text)` processes all prose text fields through 8 steps before they are stored in the model:

### Step 1: NormaliseText
Call `NormaliseText` — smart quotes, AP concatenation repair, trademark strip, trim.

### Step 2: Strip PDF Chrome
- Lone page numbers (lines containing only digits) → remove
- `OPERATIVES` as a standalone line → remove
- `CONTINUES ON OTHER SIDE` → already stripped by `NormaliseText`

### Step 3: Strip Type Prefix Labels
At the start of the text, strip:
- `PSYCHIC.` or `PSYCHIC ` (followed by period or space)
- `STRATEGIC GAMBIT.` or `STRATEGIC GAMBIT ` (followed by period or space)
- `ONCE PER BATTLE. ` (followed by space)
- `ONCE PER TURNING POINT. ` (followed by space)

### Step 4: Split Inline Numbered Lists
Split `\s+(\d+)\.\s+` when followed by at least two consecutive uppercase letters.
`"text 2. DUELLER Whenever…"` → `"text\n2. DUELLER Whenever…"`

### Step 5: Format Numbered List Items → Markdown
Lines matching `^(\d+)\.\s+([A-Z][A-Z'\-]+(?:\s+[A-Z][A-Z'\-]+)*)`:
- `1. AGGRESSIVE This…` → `1. **Aggressive** This…`
Each ALL-CAPS name word must have at least 2 uppercase chars (avoids capturing the next sentence's capital).

### Step 6: ALL-CAPS Headings → Bold Title Case
**6a** — ALL-CAPS phrase (4+ chars) followed by `:` anywhere:
- `CHAPTER TACTICS:` → `**Chapter Tactics:**`

**6b** — ALL-CAPS phrase (4+ chars) at the start of a line, followed by sentence text, NOT followed by `operative`/`friendly`/`enemy`:
- `HARLEQUIN'S PANOPLY The tools of…` → `**Harlequin's Panoply** The tools of…`

**6c** — Two or more contiguous ALL-CAPS words (each 2+ chars) appearing inline in prose (not already at the start of a line / already handled by 6a or 6b):
- `…activate a friendly ANGEL OF DEATH operative…` → `…activate a friendly **Angel of Death** operative…`
- Requires at least two words to avoid bolding single-letter initials or abbreviations already captured by the stats-column regex.

### Step 7: Bullet Symbol Hierarchy → Markdown
Process line by line, tracking list depth:

| Symbol | Unicode | Markdown output |
|--------|---------|-----------------|
| `↘` / `↙` / `↳` | U+2198 / U+2199 / U+21B3 | `- ` (depth 1) |
| `•` | U+2022 | `  - ` (depth 2) |
| `○` | U+25CB | `    - ` (depth ≥ 2 parent) or `  - ` (depth 1 parent) |
| Already-formatted `- ` / `  - ` / `    - ` | — | Pass through unchanged |

Continuation line (non-bullet follows a list item, not a numbered item) → append with space.
Numbered items (`^\d+\. `) always start a new block, never treated as continuation.

### Step 8: Constraint Sentence Bullets

Abilities/actions with a constraint sentence ("This operative cannot perform this action...") display on the physical card as two icon-marked sections: ▶ for the effect and ◆ for the constraint. These are represented as a Markdown bullet list.

**Patterns** (only fire after `.` to avoid breaking quoted errata text):
- `For the purposes of action restrictions` (action classification note)
- `This operative cannot perform this action`
- `This operative cannot perform this ability`
- `An operative cannot perform this action`

When multiple patterns match in the same text, only the **earliest** match is used for the split — later patterns are part of the same constraint section. This ensures e.g. "For the purposes...Fight action. This operative cannot..." stays as one bullet.

When a pattern matches, the constraint is prefixed with `- ` and the preceding effect text is also prefixed with `- `, creating a two-item bullet list:

```yaml
text: |-
  - Select one enemy operative visible to and within 7" of this operative...
  - This operative cannot perform this action while within control range of an enemy operative.
```

### Step 9: Sentence-Start Paragraph Breaks

Specific Kill Team phrasings that begin a new paragraph in the PDF but arrive joined to the preceding sentence by a space. Each is replaced with `.\n\n`:

| Pattern | Context |
|---------|---------|
| `. Your kill team` | Faction rules → kill team composition instruction |
| `. Use this ` | Ploy/rule body → usage instruction |
| `. When selecting ` | Rules → selection instruction |
| `. Designer's Note:` | Rules → designer's note callout |
| `. You can use ` | Equipment lore → rule trigger |
| `. Once per ` | Equipment lore → frequency constraint trigger |
| `. When this equipment ` | Equipment lore → equipment trigger |
| `. During each friendly ` | Faction rule lore → rule paragraph |
| `. Each friendly ` | Faction rule → separate rule paragraph |

Multiple blank lines (3+) are collapsed to `\n\n`. Result is trimmed.

### Step 10: Bordered Callout Blockquotes

Paragraphs that appear inside a visible border/box in the PDF are converted to Markdown blockquotes (`> `). Each line of a matching paragraph is prefixed with `> `.

| Pattern | Example |
|---------|---------|
| Starts with `Designer's Note:` | `> Designer's Note: If you're playing a series of games...` |
| Starts with `Some ` and contains ` rules refer` | `> Some **ANGEL OF DEATH** rules refer to a 'bolt weapon'...` |

**Applied to:** ability.text, ploy.text, rule.text, equipment.text, operativeSelection.text, supplementaryInformation.

### Step 11: Ploy-Specific Paragraph Breaks (strategy and firefight ploys only)

Kill Team ploys have a lore/flavour paragraph followed by a mechanical rule paragraph. In the raw PDF text these arrive joined by a space. These patterns are applied ONLY to strategy and firefight ploy PDFs (not faction rules, where the same phrases appear mid-paragraph).

`ParseRulesDoc` is called with `isPloy: true` for strategy and firefight ploy PDFs. When enabled, `TextHelpers.ApplyPloyParagraphBreaks` inserts `\n\n` before these rule-start patterns (after `.`):

| Pattern | Context |
|---------|---------|
| `Whenever a friendly ` | Ploy rule targeting a friendly operative |
| `Whenever an operative ` | Ploy rule targeting any operative |
| `Whenever you're ` | Ploy rule about player action |
| `Friendly ` | Ploy rule starting with operative subject |
| `Select one ` | Ploy rule with selection instruction |
| `Select two ` | Ploy rule with selection instruction |
| `You can ignore ` | Ploy rule with override instruction |
| `Change your ` | Ploy rule with state change instruction |
| `Place one ` | Ploy rule with setup instruction |
| `Up to D` | Ploy rule with quantity instruction |
| `One friendly ` | Ploy rule with operative count |

`BuildOperativeSelectionMarkdown` applies its own constraint and sentence-break patterns (subset of the above) plus blank-line deduplication to suppress consecutive empty lines from the raw PDF.

---

## Rule 9 — `abilities` (passive) vs `specialActions` (active)

Operative abilities are split into two separate YAML arrays:

| Array | Condition | Schema |
|-------|-----------|--------|
| `abilities` | `apCost == null` (passive rules, no AP cost) | `$defs/ability` — no `apCost` field |
| `specialActions` | `apCost > 0` (active 1AP actions) | `$defs/specialAction` — `apCost` required, minimum 1 |

Both arrays are optional (omitted when empty). Routing happens at parse time in `ParseBackContent`.

Two-column 1AP back-of-card header (`NAME 1AP   NAME 1AP`) → both actions go to `specialActions`.
Two-column passive back-of-card → both abilities go to `abilities`.
Front-of-card ability lines → always passive → `abilities`.
Single-column 1AP lines (matching `^[A-Z][A-Z0-9'\-]+(?:\s+[A-Z][A-Z0-9'\-]+)*\s+1AP$` after normalisation) → routed to `specialActions` regardless of column layout.

---

## Rule 10 — Type Prefix Stripping

The following type prefix labels are PDF formatting artefacts and must be stripped from the START of ability/ploy/rule body text:

| PDF prefix | Meaning |
|-----------|---------|
| `PSYCHIC.` / `PSYCHIC ` | Psychic ability type label |
| `STRATEGIC GAMBIT.` / `STRATEGIC GAMBIT ` | Strategy ploy sub-type |
| `ONCE PER BATTLE.` | Frequency constraint (before main text) |
| `ONCE PER TURNING POINT.` | Frequency constraint (before main text) |

These are stripped by `StructureToMarkdown` Step 3 (see Rule 8).

---

## Rule 11 — YAML File Naming: Kebab-Case Slug

Team YAML files in `teams/` are named using the `Slugify(teamName)` function:

- Lowercase the full name
- Replace spaces with `-`
- Strip `'` apostrophes, `(` and `)` parentheses

Examples:

| Team name | File |
|-----------|------|
| Angels of Death | `teams/angels-of-death.yaml` |
| Void-Dancer Troupe | `teams/void-dancer-troupe.yaml` |
| Corsair Voidscarred | `teams/corsair-voidscarred.yaml` |

The `id` field inside the YAML is the same slug value. `Program.cs` writes to `{team.Id}.yaml`. `TeamYamlTests` references files by this same slug.

---

## Rule 12 — Supplementary Info Parsing

`ParsesupplementaryInformation` processes the Supplementary Information PDF in raw mode.

### Bullet symbol lines

In this PDF, `•` and `○` symbols appear on their **own line** (unlike Operative Selection where they are inline). The content follows on the **next line**.

- A bare `•`/`○` line sets `lastLineWasBulletSymbol = true`
- When `lastLineWasBulletSymbol` is true, the NEXT line skips ALL header and ability-subheader detection and is appended as bullet content directly

### Arrow lines

Lines starting with `↘`/`↙`/`↳` must start on their own line so `FormatBulletSymbols` can process them. A `\n\n` paragraph break is inserted before them.

### Paragraph break triggers

A `\n\n` is inserted before a line when:
- The line starts with `"Other than "` (following inline content without a blank line)
- The line starts with `"Some "` (same condition)

### ALL-CAPS header merging

Consecutive ALL-CAPS lines are merged into one heading when:
- The pending header ends with `&` (mid-word wrap, e.g. `SEEK &` + `DESTROY`)
- The pending header ends with `,` (mid-phrase wrap, e.g. `MALIGNANT PLAGUECASTER OPERATIVE,` + `PUTRESCENT VITALITY ACTION`)
- The pending header contains `,` but the incoming line does not (word-wrapped mid-phrase continuation)

Otherwise each standalone ALL-CAPS line gets its own heading.

**Header level:**
- Contains a comma → `##` (sub-item, e.g. `## FACTION EQUIPMENT, POISON VENTS`)
- No comma → `#` (section heading, e.g. `# ERRATA JANUARY '26`)

Example: `SEEK &` followed by `DESTROY` → `# Seek & Destroy` (merged, no comma → `#`).
Example: `ARCHETYPES` followed by `SECURITY` → two separate `#` headings (not merged).

### Kill Team Selection section (end of PDF)

The last pages of the supplementary PDF show a visual "Kill Team Selection" reference with operative images and weapon loadouts. pdftotext extracts the title in fragments across multiple lines, repeated for each page.

**Detection:** An ALL-CAPS line ending with ` KILL TEAM` (e.g., `ANGELS OF DEATH KILL TEAM`).

**Output:** Emit `**ANGELS OF DEATH >> KILL TEAM SELECTION**` once (the `»` U+00BB decorative arrow becomes `>>`).

**Fragment suppression:** Once the heading is emitted, all subsequent occurrences of:
- Team name words (e.g., `ANGELS`, `OF`, `DEATH`)
- `»` (U+00BB) decorative arrow
- `KILL`, `TEAM`, `SELECTION`

…are suppressed for the remainder of the document. These are repeated page header fragments with no informational value.

**Content:** Remaining lines (operative names, weapon names) are formatted naturally — operative names as `**Bold headings**`, weapon names as prose. The weapon-to-operative pairing is not guaranteed to be accurate due to PDF reading order of image-heavy pages.

---

## Rule 13 — Equipment Description: Preserve PDF Blank Lines

`ParseEquipmentWithDescriptions` collects description text line-by-line from the raw PDF. Blank lines in the PDF represent structural paragraph breaks (e.g. lore text → rules text, prose → weapon table). These blank lines are preserved as `\n\n` paragraph breaks in the description `StringBuilder`, using the same pattern as `ParseRulesDoc`.

This is a structural approach: paragraph breaks come from the PDF's own blank lines, not from string-pattern matching.

---

## Rule 14 — Inline Weapon Table: WR as Table Column

`BuildInlineWeaponTableMarkdown` renders weapon stats found inside equipment descriptions as a Markdown table. When weapon rules (WR) text is present, it is included as a 5th column in the table rather than as a separate `**WR:** text` line below.

| WR present | Header | Data row |
|------------|--------|----------|
| Yes | `\| NAME \| ATK \| HIT \| DMG \| WR \|` | `\| Frag grenade \| 4 \| 4+ \| 2/4 \| Range 6", Blast 2", Saturate \|` |
| No | `\| NAME \| ATK \| HIT \| DMG \|` | `\| Frag grenade \| 4 \| 4+ \| 2/4 \|` |

This matches the PDF's visual structure where WR is part of the same weapon stats block.
