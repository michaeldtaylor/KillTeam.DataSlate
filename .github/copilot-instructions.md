# Copilot Instructions — KillTeam DataSlate

This file is the authoritative source of Copilot behaviour for this repository.
All code generation and edits must follow the rules below.

---

## C# Coding Conventions

Full reference: `.github/skills/spec/references/csharp-conventions.md`

Key rules to apply at all times:

1. **Always use braces** — all control-flow blocks (`if`, `else`, `for`, `foreach`, `while`, `using`) must use braces, even for single-statement bodies.
2. **`throw new` inside a braced block** — never inline or braceless.
3. **Blank line between `using` block, namespace declaration, and class** — each separated by one blank line.
4. **Blank line between variable declarations and statements** — when a method opens with variable declarations, add a blank line before the first statement.
5. **Blank line between all class/interface members** — properties, methods, events, etc. all separated by one blank line.
6. **Use `required` on mandatory properties** — unless the type is a `System.Text.Json` deserialization target (add a comment explaining why it is omitted).
7. **Enum members each on their own line** — never inline. Include a trailing comma on the last item.
8. **Trailing comma on last item** — enums, multi-line collection initialisers, multi-line argument/parameter lists.
9. **Use `init` instead of `set`** — unless the property is mutable game state or a JSON deserialization target.
10. **Always use `var`** — for local variables when type is inferable. Exception: variables initialised with `null`.
11. **File-scoped namespaces** — `namespace Foo.Bar;` not `namespace Foo.Bar { }`.
12. **No expression-bodied methods or constructors** — use block bodies.

---

## Parsing Rules

Full reference: `docs/kt_extractor_parsing_rules.md`

Key rules to apply when generating or modifying parser code:

- **Text normalisation** (Rule 1): Smart quotes → ASCII, AP concatenation repair, strip trademark symbols and "CONTINUES ON OTHER SIDE", strip lone page numbers.
- **Title Case for names** (Rule 2): Ability, ploy, equipment, rule, and operative names use Title Case. Mid-name prepositions (`of`, `the`, `a`, `an`, `and`, etc.) are lowercase; first and last words always capitalised.
- **Markdown in prose fields** (Rule 3 / Rule 8): Use GitHub-compatible Markdown in all `text` fields. Apply the full `StructureToMarkdown` pipeline (11 steps: normalise → strip chrome → strip type prefixes → numbered list splits → bold headings → bullet hierarchy → constraint bullets → sentence breaks → blockquotes → ploy breaks).
- **Strip PDF chrome** (Rule 4): Remove "CONTINUES ON OTHER SIDE", "OPERATIVES" standalone headings, `®`/`™`, isolated page numbers.
- **All 8 PDFs must produce output** (Rule 5): `datacards`, `factionEquipment`, `factionRules`, `firefightPloys`, `operativeSelection`, `strategyPloys`, `supplementaryInformation`, `universalEquipment`.
- **YAML field order** (Rule 6): Metadata header (`id`, `name`, `grandFaction`, `faction`) first, then PDF-sourced fields in alphabetical PDF filename order.
- **PdfPig dual-mode extraction** (Rule 7): Layout mode for weapon stats rows; content-order mode for all prose. Apply split-word merge in layout mode. Detect strikethrough via `ExperimentalAccess.Paths`.
- **`abilities` vs `specialActions`** (Rule 9): Passive rules (no AP cost) → `abilities`; active AP-costed actions → `specialActions`.
- **Type prefix stripping** (Rule 10): Strip `PSYCHIC.`, `STRATEGIC GAMBIT.`, `ONCE PER BATTLE.`, `ONCE PER TURNING POINT.` from the start of body text.
- **YAML file naming** (Rule 11): Kebab-case slug — lowercase, spaces to `-`, strip `'`, `(`, `)`. The `id` field matches the filename slug.
- **Supplementary info parsing** (Rule 12): Bullet symbols appear on their own line (symbol then content on the next line). Arrow lines get a preceding `\n\n`. ALL-CAPS header merging rules apply. Kill Team Selection section at end of PDF gets special suppression treatment.
- **Equipment font-size paragraph breaks** (Rule 13): Transition from ≥8.8pt to ≤8.6pt text marks lore→rules boundary; insert `\n\n` at that boundary.
- **Inline weapon table** (Rule 14): WR column is included in the Markdown table when present, not as a separate line.
