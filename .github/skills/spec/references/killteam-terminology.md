# Kill Team — Canonical Terminology

This document defines the canonical Kill Team terms used throughout the codebase.
Use these terms consistently in event fields, context objects, variable names, and UI labels.

---

## Combat Roles

| Term | Context | Definition |
|------|---------|------------|
| **Active operative** | General | The operative currently taking its activation (performing actions). Broadest term. |
| **Attacker** | Fight & Shoot | The operative rolling **attack dice**. Used for both Fight and Shoot. |
| **Defender** | Fight only | The operative rolling **defence dice in a Fight** action. Both operatives fight symmetrically — the non-initiating operative is "the defender." |
| **Target** | Shoot (& Fight setup) | The operative selected as the target of a **Shoot** action. Kill Team rules refer to them as "the target" throughout the entire shoot resolution — including when they roll defence dice. They are **never** called "the defender" in a shoot. |

### Key rule: Shoot uses Target, not Defender

In a Shoot action, the target operative rolls defence dice but retains the "target" label throughout.
"Defender" is only canonical in a **Fight** action where both operatives have symmetric dice-rolling roles.

---

## Codebase Conventions

- **Event fields for Shoot**: use `AttackerName`, `TargetName` — never `DefenderName` on a shoot event.
- **Event fields for Fight**: use `AttackerName`, `DefenderName`.
- **Context objects** (`WeaponCoverContext`, `WeaponEffectContext`): use `Attacker` and `Target` properties (domain objects, not strings).
- **UI labels**: shoot pools display shows `(Attacker)` and `(Target)`; fight pools display shows `(Attacker)` and `(Defender)`.

---

## Other Terms

| Term | Definition |
|------|------------|
| **APL** | Action Point Limit — the number of actions an operative may take per activation. |
| **Wounds** | Hit points. An operative is **incapacitated** when wounds reach 0. |
| **Incapacitated** | Operative removed from play (wounds = 0). |
| **Guard** | Defensive posture; cleared after being targeted. |
| **CP** | Command Points — team resource spent on stratagems and CP re-rolls. |
| **Turning Point** | A round of play (not called a "round" in Kill Team). |
| **Conceal** | One of two operative orders (Conceal / Engage). Conceal operatives cannot shoot. |
| **Engage** | One of two operative orders. Engage operatives may shoot. |
| **Cover** | Light cover or obscured — reduces effective dice hits. |
| **Obscured** | Heavy cover; target is fully obscured and cannot be targeted by normal weapons. |
