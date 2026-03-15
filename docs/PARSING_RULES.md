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

Text description fields (ability.text, ploy.text, rule.text, operativeSelection.text, supplementaryInfo) use GitHub-compatible Markdown.

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
| `*Faction Equipment*` | `factionEquipment[]` | Equipment with names and descriptions |
| `*Faction Rules*` | `factionRules[]` | Named rules with text |
| `*Firefight Ploys*` | `firefightPloys[]` | Named ploys with text |
| `*Operative Selection*` | `operativeSelection{}` | Archetype + Markdown composition rules |
| `*Strategy Ploys*` | `strategyPloys[]` | Named ploys with text |
| `*Supplementary Information*` | `supplementaryInfo` | Raw Markdown text (errata, FAQs) |
| `*Universal Equipment*` | `universalEquipment[]` | Equipment with names and descriptions |

## Rule 6 — YAML Field Order

Metadata first, then PDF-sourced fields in alphabetical PDF filename order:

```
id, name, faction          (metadata header)
datacards                  (Datacards)
factionEquipment           (Faction Equipment)
factionRules               (Faction Rules)
firefightPloys             (Firefight Ploys)
operativeSelection         (Operative Selection)
strategyPloys              (Strategy Ploys)
supplementaryInfo          (Supplementary Information)
universalEquipment         (Universal Equipment)
```

---

## Rule 7 — pdftotext Mode: `-raw` for Prose, `-layout` for Weapon Parsing

pdftotext is run in two modes depending on what is being parsed:

| Mode | Flag | Used for |
|------|------|----------|
| Layout | `-layout` | `ParseDatacards` — weapon stats rows require fixed column positions |
| Raw | `-raw` | `ParseRulesDoc`, `ParseEquipmentWithDescriptions`, `ParseSupplementaryInfo`, `ParseOperativeSelection` — natural reading order avoids two-column interleaving |

**Why:** pdftotext `-raw` outputs text in PDF content stream order, which naturally reads left-column then right-column for two-column layouts. `-layout` preserves spatial positions and is required for the weapon-stats regex.

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

### Step 8: Constraint Sentence Paragraph Break
Insert `\n\n` before:
- `This operative cannot perform this action`
- `An operative cannot perform this action`
- `This operative cannot perform this ability`

Multiple blank lines (3+) are collapsed to `\n\n`. Result is trimmed.

**Applied to:** ability.text, ploy.text, rule.text, equipment.description, operativeSelection.text, supplementaryInfo.

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


Metadata first, then PDF-sourced fields in alphabetical PDF filename order:

```
id, name, faction          (metadata header)
datacards                  (Datacards)
factionEquipment           (Faction Equipment)
factionRules               (Faction Rules)
firefightPloys             (Firefight Ploys)
operativeSelection         (Operative Selection)
strategyPloys              (Strategy Ploys)
supplementaryInfo          (Supplementary Information)
universalEquipment         (Universal Equipment)
```
