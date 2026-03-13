# Spike: Blast and Torrent Multi-Target Weapon Rules

**Status**: Draft  
**Author**: Spike  
**Date**: 2025-07  
**Area**: Kill Team Game Tracking — Blast / Torrent Multi-Target Resolution

---

## 1. Introduction

Blast and Torrent are the only Kill Team V3.0 weapon rules that fundamentally change the *number of
targets* in a Shoot action. All other weapon rules (Piercing, Saturate, Severe, etc.) modify the
resolution of a single attacker-vs-defender exchange; Blast and Torrent require the attacker to
resolve combat against two or more operatives from a single attack roll. This has three distinct
implications for the app.

**Multi-target coordination.** The player fires once and the same set of attack dice is used against
every operative in the blast radius. The orchestrator must collect all targets before the attack roll
is made, then iterate each target independently for defence dice and damage.

**Friendly fire.** Blast and Torrent do not discriminate between friend and foe. The app must present
all operatives in range — including the attacker's own team-mates — and warn the player before dice
are entered. The player makes the targeting decision; the app enforces no restriction.

**Schema implications.** A single action record already stores one `target_operative_id` and one
`defender_dice` column. Multi-target resolution requires an addendum table
(`action_blast_targets`) to store per-target dice results and damage without widening the `actions`
table into an unwieldy shape.

**Reference documents.** The existing Shoot flow (state machine, cover check, save allocation,
`CombatResolutionService`) is fully defined in `spike-shoot-ui.md`. Weapon rule parsing and
`SpecialRuleKind.Blast` / `SpecialRuleKind.Torrent` are defined in `spike-weapon-rules.md §1`. This
spike defines only the delta: the multi-target loop, the `BlastShootContext`, the
`BlastResolutionService`, and the addendum schema table.

---

## 2. Rules Recap

> **Official rule text (verbatim, Kill Team V3.0 reference card):**
>
> **Blast x″**: Make attacks against all operatives within x″ of target, visible to **target**.
>
> **Torrent x″**: Make attacks against all operatives within x″ of target, visible to **shooter**.

### Detailed Mechanics

**Blast x″**

- Select a primary target operative as normal (the initial target, subject to all standard targeting
  rules).
- ALL other operatives (friend and foe) within x″ of that primary target that are also visible to
  the **primary target** are also hit automatically — the attacker does not need line of sight to
  them directly.
- Roll attack dice once. The same dice results are used against every target (primary and
  additional).
- Each target rolls its own defence dice separately.
- Hits, saves, and damage are resolved independently per target.

**Torrent x″**

- Identical to Blast with one substitution: additional targets must be visible to the **shooter**
  (not visible to the primary target).
- One shared attack roll. Each target rolls its own defence dice. Damage resolved independently.

### Blast vs. Torrent Comparison

| Aspect | Blast x″ | Torrent x″ |
|---|---|---|
| Additional target radius | x″ from **primary target** | x″ from **primary target** |
| Visibility check for additional targets | Must be visible to **primary target** | Must be visible to **shooter** |
| Attack dice | **One shared roll** for all targets | **One shared roll** for all targets |
| Defence dice | Each target rolls **separately** | Each target rolls **separately** |
| Damage resolution | Per-target, independent | Per-target, independent |
| Friendly fire | Yes — no faction restriction | Yes — no faction restriction |
| CLI enforcement (distance/visibility) | Player declares from board state | Player declares from board state |

> **CLI enforcement limitation.** The app has no distance or visibility model. For both Blast and
> Torrent the player is asked to declare which additional operatives are in range and visible. The app
> presents all operatives on the board as candidates and trusts the player's selection. This matches
> the approach used for cover (also player-declared). A distance model is a future iteration.

### Worked-Example Weapons

| Weapon | Rule | ATK | Hit | NormalDmg | CritDmg | Other rules |
|---|---|---|---|---|---|---|
| Auxiliary grenade launcher (frag) | Blast 2″ | 4 | 3+ | 2 | 4 | — |
| Bolt sniper rifle (hyperfrag) | Blast 1″ | 4 | 2+ | 2 | 4 | Heavy (Dash only), Silent |
| Heavy bolter (sweeping) | Torrent 1″ | 4 | 3+ | 4 | 5 | Piercing Crits 1 |
| Plague spewer | Torrent 2″ | 5 | 2+ | 3 | 3 | Range 7″, Saturate, Severe |

---

## 3. UX Design

### 3.1 Weapon Selection Banner

When the selected weapon has `SpecialRuleKind.Blast` or `SpecialRuleKind.Torrent`, the weapon list
appends a multi-target notice immediately after selection:

```
  > Auxiliary grenade launcher (frag)  (ATK 4 | Hit 3+ | DMG 2/4 | Blast 2")

  ╔══════════════════════════════════════════════════════════════════╗
  ║  [MULTI-TARGET]  This weapon hits multiple targets.              ║
  ║  Blast 2": all operatives within 2" of your target that are      ║
  ║  visible to the target are also hit — friend or foe.             ║
  ╚══════════════════════════════════════════════════════════════════╝
```

For Torrent, the banner reads "visible to **you** (the shooter)" in place of "visible to the
target".

### 3.2 Step-by-Step Flow

The Blast/Torrent flow replaces the single-target Shoot flow from `spike-shoot-ui.md §2` for the
TargetSelection → AttackDiceEntry → DefenceDiceEntry segment. All other states (CoverCheck per
target, SaveAllocation, DamageApplication, StunCheck, HotCheck) are executed in a loop over each
target.

```
Step 1.  Select primary target           (standard target selection)
Step 2.  Select additional targets       (multi-select from all other operatives)
Step 3.  Confirm full target list        (primary + additional, with friendly-fire warning)
Step 4.  Cover check for primary         (as normal Shoot)
Step 5.  Roll / enter attack dice ONCE   (shared across all targets)
Step 6.  Apply post-roll attack rules    (Severe, Rending, Punishing — once, applied to shared pool)
Step 7.  Re-roll prompts                 (Balanced / Ceaseless / Relentless — once)
Step 8.  For each target (primary first, then additional in declared order):
           a. Cover check for this target
           b. Apply Piercing / PiercingCrits to this target's defence dice count
           c. Roll / enter this target's defence dice
           d. CP re-roll prompt (optional)
           e. Save allocation (app-computed)
           f. Damage applied; wound track updated
           g. Incapacitation check
Step 9.  Blast / Torrent summary         (all targets, all damage)
```

### 3.3 Spectre.Console Component Choices

| Step | Component | Notes |
|---|---|---|
| Primary target selection | `SelectionPrompt<OperativeChoice>` | Standard — identical to single-target Shoot |
| Additional target multi-select | `MultiSelectionPrompt<OperativeChoice>` | All operatives except primary; player toggles each |
| Friendly-fire warning | `MarkupLine` in yellow | Shown immediately when a friendly is in the selection |
| Confirm target list | `ConfirmationPrompt` | Lists all targets before any dice are entered |
| Attack dice entry | `TextPrompt<string>` | Space-separated ints; entered once; identical to single-target Shoot |
| Per-target defence dice entry | `TextPrompt<string>` | Prompt is repeated per target inside the loop |
| Per-target cover check | `SelectionPrompt` (In cover / Obscured / Neither) | Repeated per target |
| Blast summary panel | Spectre.Console `Table` | Rows: one per target; columns: target, dice, saves, damage |

**`MultiSelectionPrompt` usage for additional targets:**

```csharp
var additionalTargets = AnsiConsole.Prompt(
    new MultiSelectionPrompt<OperativeChoice>()
        .Title("Which other operatives are within [bold]2\"[/] of the target?")
        .InstructionsText("[grey](Space to select, Enter to confirm. Select none if weapon hits primary only.)[/]")
        .NotRequired()               // player may select zero additional targets
        .AddChoices(candidateOperatives));
```

`candidateOperatives` is all operatives on the board except the primary target, regardless of team
affiliation. The prompt title substitutes the actual blast radius at runtime.

**Friendly-fire inline warning** — rendered after each selection change:

```
  ⚠ [yellow]Intercessor Sergeant (Michael) is on YOUR team — friendly fire![/]
```

This is displayed for each friendly operative included in `additionalTargets` after the prompt
returns, before the confirmation step.

---

## 4. CLI Transcript — Frag Grenade (Blast 2″)

### Operatives

| Operative | Player | Team | W | Save | Role |
|---|---|---|---|---|---|
| Intercessor Gunner | Michael | Angels of Death | 14W | 3+ | Attacker |
| Plague Marine Fighter | Solomon | Death Guard | 14W | 3+ | Primary target |
| Plague Marine Warrior | Solomon | Death Guard | 14W | 3+ | Additional — within 2″ of Fighter, visible to Fighter |
| Intercessor Sergeant | Michael | Angels of Death | 15W | 3+ | Additional — within 2″ of Fighter (friendly fire!) |

**Weapon**: Auxiliary grenade launcher (frag) — Blast 2″, ATK 4, Hit 3+, NormalDmg 2, CritDmg 4.

---

### Phase 1 — Target Selection

```
╔══════════════════════════════════════════════════════════════════╗
║                    🎯  SHOOT ACTION  🎯                           ║
║                    Intercessor Gunner                            ║
╚══════════════════════════════════════════════════════════════════╝

Select a target:
  > Plague Marine Fighter   [14W]  [Engage]  no cover
    Plague Marine Warrior   [14W]  [Engage]  in cover
    Plague Marine Champion  [15W]  [Conceal] in cover  ← Conceal + in cover: INVALID

> Plague Marine Fighter
```

---

### Phase 2 — Weapon Selection

```
──────────────────────────────────────────────────────────
  Gunner selects a ranged weapon:
──────────────────────────────────────────────────────────

  > Auxiliary grenade launcher (frag)  (ATK 4 | Hit 3+ | DMG 2/4 | Blast 2")
    Auxiliary grenade launcher (krak)  (ATK 4 | Hit 3+ | DMG 4/5 | Piercing 1)
    Bolt rifle                         (ATK 4 | Hit 3+ | DMG 3/4 | Piercing Crits 1)

> Auxiliary grenade launcher (frag)

  ╔══════════════════════════════════════════════════════════════════╗
  ║  [MULTI-TARGET]  This weapon hits multiple targets.              ║
  ║  Blast 2": all operatives within 2" of Plague Marine Fighter     ║
  ║  that are visible to Fighter are also hit — friend or foe.       ║
  ╚══════════════════════════════════════════════════════════════════╝
```

---

### Phase 3 — Additional Target Selection

```
──────────────────────────────────────────────────────────
  Which other operatives are within 2" of Plague Marine Fighter?
  (Space to select, Enter to confirm. Select none if only the primary is in range.)
──────────────────────────────────────────────────────────

  [x] Plague Marine Warrior    [14W]  [Engage]   (Solomon — Death Guard)
  [x] Intercessor Sergeant     [15W]  [Engage]   (Michael — Angels of Death)
  [ ] Plague Marine Champion   [15W]  [Conceal]
  [ ] Assault Intercessor      [12W]  [Engage]

  ⚠  Intercessor Sergeant (Michael) is on YOUR team — friendly fire!

> (Enter to confirm)
```

---

### Phase 4 — Target List Confirmation

```
──────────────────────────────────────────────────────────
  Confirm targets for Auxiliary grenade launcher (frag):
──────────────────────────────────────────────────────────

  1. Plague Marine Fighter   [14W]  (primary target)
  2. Plague Marine Warrior   [14W]  (additional)
  3. Intercessor Sergeant    [15W]  (additional — ⚠ FRIENDLY)

  ⚠  This weapon will affect 1 friendly operative. Confirm?
  > Yes — proceed with these targets
    No  — go back and reselect

> Yes — proceed with these targets
```

---

### Phase 5 — Attack Dice Entry (Shared — Rolled Once)

```
──────────────────────────────────────────────────────────
  Gunner rolls 4 dice  (Auxiliary grenade launcher (frag), Hit 3+)
  These dice are SHARED across all 3 targets.
──────────────────────────────────────────────────────────

How would you like to enter Gunner's dice?
    🎲 Roll for me
  > ✏  Enter manually

Enter Gunner's dice results (space or comma separated):
> 6 5 3 2

  A1: 6  → CRIT  ✓
  A2: 5  → HIT   ✓
  A3: 3  → HIT   ✓
  A4: 2  → MISS  ✗  (discarded)

Attack pool: 3 dice  (1 CRIT, 2 HITs)
  (No re-roll rules on this weapon.)
```

---

### Phase 6 — Per-Target Resolution Loop

#### Target 1 of 3 — Plague Marine Fighter (Primary)

```
══════════════════════════════════════════════════════════════════
  Target 1 / 3  —  Plague Marine Fighter  (Solomon)  [14W]
══════════════════════════════════════════════════════════════════

How is Plague Marine Fighter positioned?
  > 1. In cover
    2. Obscured
    3. Neither

> 3. Neither

  No Piercing on this weapon. Fighter rolls 3 defence dice (Save 3+).

──────────────────────────────────────────────────────────
  Fighter rolls defence dice (Save 3+) — 3 dice
──────────────────────────────────────────────────────────

Enter Fighter's defence dice results:
> 5 4 1

  D1: 5  → SAVE ✓
  D2: 4  → SAVE ✓
  D3: 1  → MISS ✗

  CP Re-roll available (Solomon's team: 2CP). Spend 1CP?
  > No — keep all dice

  Saves retained: 2

  ── Save Allocation ─────────────────────────────────────
  Attacker: 1 CRIT, 2 HITs
  Saves:    2 normal saves

  (a) 1 crit save → cancel 1 crit attack:  no crit saves. Skip.
  (b) 2 normal saves → cancel 1 crit attack: 2 normals → cancel 1 CRIT. Saves used: 2.
  (c) 0 saves remaining. Skip.

  Unblocked: 0 CRITs, 2 HITs.
  Damage: 2 × 2 = 4 normal damage.

  Plague Marine Fighter: 14W → 10W.
```

---

#### Target 2 of 3 — Plague Marine Warrior (Additional)

```
══════════════════════════════════════════════════════════════════
  Target 2 / 3  —  Plague Marine Warrior  (Solomon)  [14W]
══════════════════════════════════════════════════════════════════

Attack pool (shared):  1 CRIT, 2 HITs  ← same as before

How is Plague Marine Warrior positioned?
  > 1. In cover
    2. Obscured
    3. Neither

> 1. In cover

  Cover: +1 normal save retained automatically.
  Warrior rolls 3 defence dice (Save 3+).

──────────────────────────────────────────────────────────
  Warrior rolls defence dice (Save 3+) — 3 dice
──────────────────────────────────────────────────────────

Enter Warrior's defence dice results:
> 6 2 1

  D1: 6  → CRIT SAVE  ✓
  D2: 2  → MISS        ✗
  D3: 1  → MISS        ✗

  + Cover save: +1 normal save.

  CP Re-roll available (Solomon's team: 2CP). Spend 1CP?
  > No — keep all dice

  Saves retained: 2  (1 crit save + 1 cover normal save)

  ── Save Allocation ─────────────────────────────────────
  Attacker: 1 CRIT, 2 HITs
  Saves:    1 crit save, 1 normal save

  (a) 1 crit save → cancel 1 crit attack: cancel 1 CRIT. Saves used: 1 crit.
  (b) No crits remain. Skip.
  (c) 1 normal save → cancel 1 normal attack: cancel 1 HIT. Saves used: 1 normal.

  Unblocked: 0 CRITs, 1 HIT.
  Damage: 1 × 2 = 2 normal damage.

  Plague Marine Warrior: 14W → 12W.
```

---

#### Target 3 of 3 — Intercessor Sergeant (Additional — Friendly!)

```
══════════════════════════════════════════════════════════════════
  Target 3 / 3  —  Intercessor Sergeant  (Michael)  [15W]
  ⚠  FRIENDLY OPERATIVE — Angels of Death
══════════════════════════════════════════════════════════════════

Attack pool (shared):  1 CRIT, 2 HITs

How is Intercessor Sergeant positioned?
  > 1. In cover
    2. Obscured
    3. Neither

> 3. Neither

  No Piercing. Sergeant rolls 3 defence dice (Save 3+).

──────────────────────────────────────────────────────────
  Sergeant rolls defence dice (Save 3+) — 3 dice
──────────────────────────────────────────────────────────

Enter Sergeant's defence dice results:
> 3 2 1

  D1: 3  → SAVE ✓
  D2: 2  → MISS ✗
  D3: 1  → MISS ✗

  CP Re-roll available (Michael's team: 2CP). Spend 1CP?
  > No — keep all dice

  Saves retained: 1

  ── Save Allocation ─────────────────────────────────────
  Attacker: 1 CRIT, 2 HITs
  Saves:    1 normal save

  (a) 1 crit save → cancel 1 crit attack:  no crit saves. Skip.
  (b) 2 normal saves → cancel 1 crit attack: only 1 save, cannot pair. Skip.
  (c) 1 normal save → cancel 1 normal attack: cancel 1 HIT.

  Unblocked: 1 CRIT, 1 HIT.
  Damage: (1 × 4) + (1 × 2) = 6 damage.

  Intercessor Sergeant: 15W → 9W.
```

---

### Phase 7 — Blast Summary

```
╔══════════════════════════════════════════════════════════════════╗
║                   💥  BLAST SUMMARY  💥                           ║
║  Intercessor Gunner  →  Auxiliary grenade launcher (frag)        ║
║  Blast 2" — 3 targets hit                                        ║
╠══════════════════════════════════════════════════════════════════╣
║  Shared attack roll:  6  5  3  2                                 ║
║  Attack pool:         1 CRIT, 2 HITs                             ║
╠══════════════════════════════════════════════════════════════════╣
║  Target                  Saves  Unblocked       Damage   Wounds  ║
║  Plague Marine Fighter   2      0 CRITs, 2 HITs  4 dmg   10W     ║
║  Plague Marine Warrior   2      0 CRITs, 1 HIT   2 dmg   12W     ║
║  Intercessor Sergeant ⚠  1      1 CRIT,  1 HIT   6 dmg    9W     ║
║  (⚠ = friendly operative)                                        ║
╚══════════════════════════════════════════════════════════════════╝

Add a narrative note? (optional, press Enter to skip)
>
```

---

## 5. Service Design

### 5.1 `BlastShootContext`

`BlastShootContext` bundles the shared attack-roll data with the list of all targets. It extends the
single-target `ShootContext` concept from `spike-weapon-rules.md §3.1`.

```csharp
/// <summary>
/// All inputs required to resolve a Blast or Torrent Shoot action.
/// The attack dice are shared across every target; each target carries its own
/// defence dice, cover status, and operative reference.
/// </summary>
public record BlastShootContext(
    int[]                            AttackDice,        // raw D6 results — shared for all targets
    int                              HitThreshold,      // effective hit threshold (post-Injured)
    int                              NormalDmg,
    int                              CritDmg,
    IReadOnlyList<WeaponSpecialRule> WeaponRules,       // parsed weapon rules (Blast/Torrent + others)
    IReadOnlyList<BlastTargetInput>  Targets            // primary first, then additional
)
{
    public bool HasRule(SpecialRuleKind kind) =>
        WeaponRules.Any(r => r.Kind == kind);

    public WeaponSpecialRule? GetRule(SpecialRuleKind kind) =>
        WeaponRules.FirstOrDefault(r => r.Kind == kind);

    public int EffectiveCritDmg =>
        GetRule(SpecialRuleKind.Devastating)?.Parameter ?? CritDmg;
}

/// <summary>
/// Per-target inputs for one operative in a Blast / Torrent resolution.
/// </summary>
public record BlastTargetInput(
    Guid    OperativeId,         // FK to operatives.id
    string  OperativeName,       // display name shown in MultiSelectionPrompt
    bool    IsFriendly,          // true when target is on attacker's team (warning only)
    int[]   DefenceDice,         // raw D6 results for this target
    bool    InCover,
    bool    IsObscured,
    int     SaveThreshold,
    int     CurrentWounds,
    int     MaxWounds
);
```

### 5.2 `TargetResolutionResult`

Per-target output of one Blast/Torrent resolution pass:

```csharp
/// <summary>
/// Result of resolving one target within a Blast or Torrent Shoot action.
/// Mirrors the columns of <c>action_blast_targets</c>.
/// </summary>
public record TargetResolutionResult(
    Guid   OperativeId,
    string OperativeName,
    int    NormalHits,
    int    CritHits,
    int    Blocks,               // saves retained (including cover; excluding Saturate-suppressed)
    int    NormalDamageDealt,
    int    CritDamageDealt,
    bool   CausedIncapacitation
);
```

### 5.3 `BlastResolutionService`

`BlastResolutionService` delegates per-target resolution to the existing
`CombatResolutionService.ResolveShoot(ShootContext)`. Its own responsibility is:

1. Classify the shared attack dice once (applying Lethal, Accurate, Punishing, Rending, Severe).
2. Iterate each `BlastTargetInput`, build a `ShootContext` using the pre-classified hit counts, and
   call `CombatResolutionService.ResolveShoot`.
3. Collect and return a `BlastResolutionResult`.

```csharp
public class BlastResolutionService
{
    private readonly CombatResolutionService _combat;

    public BlastResolutionService(CombatResolutionService combat)
    {
        _combat = combat;
    }

    /// <summary>
    /// Resolves a complete Blast or Torrent Shoot action.
    /// The shared attack roll is classified once; each target is resolved independently.
    /// </summary>
    public BlastResolutionResult Resolve(BlastShootContext context)
    {
        // 1. Classify attack dice once — apply Lethal, Accurate, Punishing, Rending, Severe.
        //    We reuse the CombatResolutionService pipeline by calling a helper that returns
        //    only the classified attack pool (not a full CombatResult).
        var (sharedNormals, sharedCrits) = ClassifyAttackPool(context);

        // 2. Resolve each target independently using the shared hit counts.
        var targetResults = context.Targets
            .Select(t => ResolveTarget(t, sharedNormals, sharedCrits, context))
            .ToList();

        return new BlastResolutionResult(sharedNormals, sharedCrits, targetResults);
    }

    private TargetResolutionResult ResolveTarget(
        BlastTargetInput         target,
        int                      sharedNormals,
        int                      sharedCrits,
        BlastShootContext         context)
    {
        // Build a ShootContext that presents the pre-classified hit counts as synthetic dice.
        // We represent the shared pool as pre-rolled dice: sharedCrits × 6 + sharedNormals × hitThreshold.
        // CombatResolutionService re-classifies them, but the values map back to the same counts.
        var syntheticAttack = BuildSyntheticAttackDice(sharedNormals, sharedCrits, context.HitThreshold);

        var shootCtx = new ShootContext(
            AttackDice:    syntheticAttack,
            DefenceDice:   target.DefenceDice,
            InCover:       target.InCover,
            HitThreshold:  context.HitThreshold,
            SaveThreshold: target.SaveThreshold,
            NormalDmg:     context.NormalDmg,
            CritDmg:       context.CritDmg,
            WeaponRules:   context.WeaponRules
        );

        // Strip rules that modify the shared attack pool — these were already applied
        // in ClassifyAttackPool and must NOT fire again per-target.
        // The attack pool was already classified in ClassifyAttackPool; passing these rules
        // to ResolveShoot again would double-apply them.
        var poolModifyingRules = new[]
        {
            SpecialRuleKind.Blast, SpecialRuleKind.Torrent,
            SpecialRuleKind.Severe, SpecialRuleKind.Rending,
            SpecialRuleKind.Punishing, SpecialRuleKind.Lethal,
            SpecialRuleKind.Accurate
        };
        var filteredCtx = shootCtx with
        {
            WeaponRules = shootCtx.WeaponRules
                .Where(r => !poolModifyingRules.Contains(r.Kind))
                .ToList()
        };

        var result = _combat.ResolveShoot(filteredCtx);

        bool incapacitated = target.CurrentWounds - result.NormalDamageDealt
                             - result.CritDamageDealt <= 0;

        return new TargetResolutionResult(
            OperativeId:          target.OperativeId,
            OperativeName:        target.OperativeName,
            NormalHits:           result.AttackerNormalHits,
            CritHits:             result.AttackerCritHits,
            Blocks:               result.DefenderSaves,
            NormalDamageDealt:    result.NormalDamageDealt,
            CritDamageDealt:      result.CritDamageDealt,
            CausedIncapacitation: incapacitated
        );
    }

    private (int normals, int crits) ClassifyAttackPool(BlastShootContext context)
    {
        // Delegate classification to CombatResolutionService by calling ResolveShoot
        // against a zero-defence target; extract only the attack pool counts.
        var dummyCtx = new ShootContext(
            AttackDice:    context.AttackDice,
            DefenceDice:   Array.Empty<int>(),
            InCover:       false,
            HitThreshold:  context.HitThreshold,
            SaveThreshold: 7,           // impossible save — all attacks go through
            NormalDmg:     0,
            CritDmg:       0,
            WeaponRules:   context.WeaponRules
                               .Where(r => r.Kind is not SpecialRuleKind.Blast
                                               and not SpecialRuleKind.Torrent)
                               .ToList()
        );
        var dummy = _combat.ResolveShoot(dummyCtx);
        return (dummy.AttackerNormalHits, dummy.AttackerCritHits);
    }

    private static int[] BuildSyntheticAttackDice(int normals, int crits, int hitThreshold)
    {
        // Represent normals as hitThreshold (guaranteed hit, not crit); crits as 6.
        return Enumerable.Repeat(6, crits)
            .Concat(Enumerable.Repeat(hitThreshold, normals))
            .ToArray();
    }
}

/// <summary>
/// The complete result of a Blast or Torrent Shoot action.
/// </summary>
public record BlastResolutionResult(
    int                              SharedNormalHits,
    int                              SharedCritHits,
    IReadOnlyList<TargetResolutionResult> TargetResults
);
```

### 5.4 `ShootSessionOrchestrator` Changes

The orchestrator detects Blast/Torrent at weapon-selection time and switches to the multi-target
path:

```csharp
// In ShootSessionOrchestrator.RunAsync():

bool isMultiTarget = selectedWeapon.ParsedRules
    .Any(r => r.Kind is SpecialRuleKind.Blast or SpecialRuleKind.Torrent);

if (isMultiTarget)
{
    await RunBlastTorrentFlowAsync(attacker, selectedWeapon, primaryTarget);
}
else
{
    await RunSingleTargetFlowAsync(attacker, selectedWeapon, primaryTarget);
}
```

`RunBlastTorrentFlowAsync` implements the nine-step flow described in §3.2, using
`BlastResolutionService.Resolve` after collecting all inputs.

After resolution, the orchestrator:

1. Persists one `Action` row (primary target in `target_operative_id`; shared dice in
   `attacker_dice`; `defender_dice` = null since defence is per-target).
2. Persists one `action_blast_targets` row per target (primary included), filling all per-target
   columns.
3. Updates `game_operative_states.current_wounds` for every affected operative.

---

## 6. Schema Change

### 6.1 `action_blast_targets` Table DDL

This table is an addendum to the existing schema. Each row represents one operative's result within
a single Blast or Torrent weapon use.

```sql
-- ─────────────────────────────────────────────────────────────────────────────
-- Migration 003: Blast / Torrent multi-target addendum
-- ─────────────────────────────────────────────────────────────────────────────

CREATE TABLE IF NOT EXISTS action_blast_targets (
    id                      TEXT    PRIMARY KEY,
    action_id               TEXT    NOT NULL REFERENCES actions (id) ON DELETE CASCADE,
    target_operative_id     TEXT    NOT NULL REFERENCES operatives (id),
    defender_dice           TEXT,               -- JSON int[], e.g. '[5,4,1]'; null if no dice entered
    normal_hits             INTEGER NOT NULL DEFAULT 0,
    critical_hits           INTEGER NOT NULL DEFAULT 0,
    blocks                  INTEGER NOT NULL DEFAULT 0,
    normal_damage_dealt     INTEGER NOT NULL DEFAULT 0,
    critical_damage_dealt   INTEGER NOT NULL DEFAULT 0,
    caused_incapacitation   INTEGER NOT NULL DEFAULT 0   -- 0 = false, 1 = true
);
```

**Relationship to `actions`:**

- The primary target is still stored in `actions.target_operative_id` (unchanged — for
  backward-compatibility with queries that join `actions` to `operatives` directly).
- `actions.attacker_dice` holds the shared attack roll JSON (unchanged column meaning).
- `actions.defender_dice` is **null** for Blast/Torrent actions — defence is per-target in
  `action_blast_targets`.
- All targets (primary and additional) have a row in `action_blast_targets`. The primary target row
  is therefore duplicated across both tables for query convenience; the canonical damage record is
  in `action_blast_targets`.

**Migration placement:** This is **Migration 003**. Migrations 001 (initial schema) and 002 (if
any intermediate addendum exists) are unaffected. Add `(3, Migration_003)` to `Migrations.All` in
the `Migrations` static class.

```csharp
private const string Migration_003 = """
    -- ── Migration 003: Blast / Torrent multi-target support ──────────────
    CREATE TABLE IF NOT EXISTS action_blast_targets (
        id                      TEXT    PRIMARY KEY,
        action_id               TEXT    NOT NULL REFERENCES actions (id) ON DELETE CASCADE,
        target_operative_id     TEXT    NOT NULL REFERENCES operatives (id),
        defender_dice           TEXT,
        normal_hits             INTEGER NOT NULL DEFAULT 0,
        critical_hits           INTEGER NOT NULL DEFAULT 0,
        blocks                  INTEGER NOT NULL DEFAULT 0,
        normal_damage_dealt     INTEGER NOT NULL DEFAULT 0,
        critical_damage_dealt   INTEGER NOT NULL DEFAULT 0,
        caused_incapacitation   INTEGER NOT NULL DEFAULT 0
    );
    """;
```

### 6.2 `spike-schema-ddl.md` Reference

Add the following note in `spike-schema-ddl.md` in the section after Migration 001 (or after the
most recently numbered migration):

> **Migration 003 — Blast/Torrent multi-target support**
> Adds `action_blast_targets`. See `spike-blast-torrent.md §6` for full DDL and rationale.

### 6.3 Updated ER Diagram Fragment

```
actions
  id ──────────────────────────────────────────────────────┐
  activation_id                                            │
  target_operative_id (primary target — FK operatives)     │
  attacker_dice (shared JSON int[])                        │  1
  defender_dice (null for Blast/Torrent)                   │
  ...                                                      │
                                                           │ 0..*
                                          action_blast_targets
                                            id
                                            action_id ────►┘
                                            target_operative_id ──► operatives
                                            defender_dice
                                            normal_hits
                                            critical_hits
                                            blocks
                                            normal_damage_dealt
                                            critical_damage_dealt
                                            caused_incapacitation
```

---

## 7. Interaction with Other Rules

All secondary rules on a Blast or Torrent weapon are applied as follows. Rules that modify the
**attack pool** are applied **once** (on the shared roll). Rules that modify the **defence pool or
damage** are applied **per target**.

### 7.1 Piercing Crits 1 — Heavy Bolter (Sweeping), Torrent 1″

`Piercing Crits 1` removes one defence die from the target's pool before they roll, but only if the
attacker retained at least one critical hit in their pool.

The shared attack pool is classified once; `sharedCrits` is known before any target's defence dice
are entered. Inside `ResolveTarget`, `CombatResolutionService.CalculatePiercingReduction` is called
with the shared crit count:

```csharp
// Inside CombatResolutionService.ResolveShoot — existing pipeline step 7:
int piercingX      = context.GetRule(SpecialRuleKind.Piercing)?.Parameter ?? 0;
int piercingCritsX = context.GetRule(SpecialRuleKind.PiercingCrits)?.Parameter ?? 0;
// attackerCritCount comes from the shared pool, passed via synthetic dice
int reduction = CalculatePiercingReduction(piercingX, piercingCritsX, attackerCritCount);
```

Because `BlastResolutionService.ResolveTarget` builds synthetic dice that reproduce the shared
`sharedCrits` count accurately, `CombatResolutionService` sees the correct crit count for the
Piercing Crits check on every target independently. If the shared roll had zero crits, Piercing
Crits 1 does not remove any dice from any target.

**CLI display (per target):**

```
  Piercing Crits 1: 1 shared CRIT retained — removing 1 defence die from Fighter.
  Fighter rolls 2 dice (3 base − 1 Piercing Crits = 2).
```

### 7.2 Saturate — Plague Spewer, Torrent 2″

`Saturate` suppresses the cover save. It applies to **every target** because it is evaluated inside
`CombatResolutionService.ResolveShoot` at step 8 (`effectiveInCover = HasRule(Saturate) ? false :
context.InCover`). Since `BlastResolutionService` passes the weapon rules (including `Saturate`) in
every `ShootContext` built for each target, cover is suppressed across all targets simultaneously.

**CLI display (before per-target loop):**

```
  Saturate: cover saves are suppressed for ALL targets.
```

Each per-target cover prompt still appears so the player can record board state accurately, but the
app ignores the `InCover` flag for save allocation when `Saturate` is present.

### 7.3 Severe — Plague Spewer, Torrent 2″

`Severe` converts one hit to a critical if the attacker retained zero crits in their pool. It is
applied to the **shared attack pool** during `ClassifyAttackPool` (step 6 of
`CombatResolutionService`'s pipeline). The resulting `(sharedNormals, sharedCrits)` counts reflect
Severe having fired (or not) once. Every target then receives the same post-Severe pool.

`Severe` is **never applied per-target** — it is a property of the single attack event, not of the
relationship between the attacker and an individual defender.

**CLI display (after attack dice classification, before per-target loop):**

```
  Severe triggered — no crits retained. Converting 1 HIT → CRIT.
  Shared pool after Severe: 2 HITs, 1 CRIT.
```

### 7.4 Summary Table

| Rule | Scope | Evaluated |
|---|---|---|
| Lethal x | Attack pool | Once (shared classification) |
| Accurate x | Attack pool | Once (shared) |
| Punishing | Attack pool | Once (shared) |
| Rending | Attack pool | Once (shared) |
| **Severe** | Attack pool | **Once (shared)** |
| Piercing x | Defence pool | **Per target** |
| Piercing Crits x | Defence pool | **Per target** (uses shared crit count) |
| **Saturate** | Cover save | **Per target** (suppressed for all) |
| Devastating x | Damage | Per target (same effective crit damage for each) |
| Heavy / Heavy (x) | Activation restriction | Checked once before weapon selection |
| Silent | Activation restriction | Checked once before weapon selection |
| Limited x | Uses counter | Decremented once per Blast/Torrent action (not per target) |

> **Note (R-04):** Attack-pool rules (Accurate, Lethal, Severe, Rending, Punishing) are applied
> ONCE on the shared pool in `ClassifyAttackPool` and are **stripped from the per-target
> `ResolveShoot` calls** to prevent double-application. Concretely: `ResolveTarget` builds
> `filteredCtx` by removing `SpecialRuleKind.Accurate`, `.Lethal`, `.Severe`, `.Rending`,
> `.Punishing` (along with `.Blast`/`.Torrent`) before passing to `CombatResolutionService.ResolveShoot`.

---

## 8. Edge Cases

### 8.1 No Additional Targets in Range

The player selects zero additional targets in the `MultiSelectionPrompt`. The app proceeds with
only the primary target using the normal single-target Shoot flow, but still logs the weapon's Blast
or Torrent notation.

```
  No additional targets selected. Resolving as single-target Shoot.
  (Blast 2" notation retained in action record.)
```

The `action_blast_targets` table still receives one row for the primary target (for consistency; all
Blast/Torrent actions are persisted the same way regardless of target count).

### 8.2 All Additional Targets Miss (No Damage)

All defence dice succeed for one or more additional targets, leaving zero unblocked attacks.
`BlastResolutionService` still returns a `TargetResolutionResult` for each target with
`NormalDamageDealt = 0`, `CritDamageDealt = 0`, `CausedIncapacitation = false`.
`action_blast_targets` rows are written for every target regardless of outcome. The summary table
shows zero damage for those targets.

```
  Plague Marine Warrior: all 3 hits blocked!  0 damage.  [12W — unchanged]
```

### 8.3 Primary Target Incapacitated Mid-Blast

When the primary target's damage is resolved and `CausedIncapacitation = true`, the app continues
the loop to resolve remaining additional targets. The primary target's incapacitation is recorded
immediately (wounds set to 0; `game_operative_states.is_incapacitated = 1`) but does not interrupt
the Blast resolution.

```
  ⚠  Plague Marine Fighter incapacitated! (0W)
  Continuing Blast resolution for remaining targets...
```

Incapacitated operatives cannot use defence dice in Kill Team rules; however, because each target's
defence dice are entered by the player, the player should simply enter no dice (or zero saves) for
an operative already on the table as incapacitated. The app does not prevent dice entry for an
already-incapacitated target — it is the player's responsibility.

### 8.4 Friendly Fire Warning

The multi-select prompt renders a `⚠ FRIENDLY` warning inline for any operative on the attacker's
team. The confirmation screen repeats the warning. The app applies no rule restriction — friendly
fire is legal in Kill Team. The action record stores friendly targets identically to enemy targets.

```
  ⚠  Intercessor Sergeant (Michael) is on YOUR team — friendly fire!
     This is legal — Kill Team allows friendly fire from Blast/Torrent weapons.
     Sergeant will roll their own defence dice separately.
```

### 8.5 Zero Additional Candidates (No Other Operatives on Board)

If fewer than two operatives are on the board, the `MultiSelectionPrompt` is skipped and the app
notes: "No other operatives are present — weapon fires as single-target." This prevents the prompt
from appearing with an empty list.

### 8.6 Blast vs. Torrent Visibility Prompt

The app cannot enforce visibility (no spatial model). It distinguishes Blast from Torrent only in
the phrasing of the additional-target prompt:

- **Blast**: "Which other operatives are within x″ of **[primary target name]** and visible to
  **[primary target name]**?"
- **Torrent**: "Which other operatives are within x″ of **[primary target name]** and visible to
  **you (the shooter)**?"

---

## 9. xUnit Tests

Test conventions: `MethodName_Scenario_ExpectedResult`. Framework: xUnit + FluentAssertions.

```csharp
public class BlastResolutionServiceTests
{
    private readonly CombatResolutionService _combat = new();
    private BlastResolutionService Sut() => new(_combat);

    // ── Test 1 ───────────────────────────────────────────────────────────

    [Fact]
    public void Resolve_BlastThreeTargets_EachTargetResolvesIndependently()
    {
        // Frag grenade (Blast 2"): ATK 4, Hit 3+, NormalDmg 2, CritDmg 4.
        // Shared roll: 6, 5, 3, 2 → 1 CRIT, 2 HITs (MISS discarded).
        // Target 1 (Fighter):  3 defence dice → 5, 4, 1 → 2 saves → 0 CRITs unblocked, 0 HITs unblocked (2 normals cancel 1 CRIT + 1 HIT blocked... recalculate)
        // Simplified: use zero-defence targets for isolation of shared pool logic.
        var ctx = new BlastShootContext(
            AttackDice:   new[] { 6, 5, 3, 2 },
            HitThreshold: 3,
            NormalDmg:    2,
            CritDmg:      4,
            WeaponRules:  SpecialRuleParser.Parse("Blast 2\""),
            Targets: new[]
            {
                new BlastTargetInput(Guid.Parse("00000001-0000-0000-0000-000000000001"), "Fighter",  IsFriendly: false,
                    DefenceDice: Array.Empty<int>(), InCover: false, IsObscured: false,
                    SaveThreshold: 7, CurrentWounds: 14, MaxWounds: 14),
                new BlastTargetInput(Guid.Parse("00000002-0000-0000-0000-000000000002"), "Warrior",  IsFriendly: false,
                    DefenceDice: Array.Empty<int>(), InCover: false, IsObscured: false,
                    SaveThreshold: 7, CurrentWounds: 14, MaxWounds: 14),
                new BlastTargetInput(Guid.Parse("00000003-0000-0000-0000-000000000003"), "Sergeant", IsFriendly: true,
                    DefenceDice: Array.Empty<int>(), InCover: false, IsObscured: false,
                    SaveThreshold: 7, CurrentWounds: 15, MaxWounds: 15),
            }
        );

        var result = Sut().Resolve(ctx);

        // 1 CRIT + 2 HITs, no saves → damage per target = (1×4) + (2×2) = 8
        result.SharedCritHits.Should().Be(1);
        result.SharedNormalHits.Should().Be(2);
        result.TargetResults.Should().HaveCount(3);
        result.TargetResults.Should().AllSatisfy(r =>
        {
            r.CritDamageDealt.Should().Be(4);    // 1 CRIT × CritDmg 4
            r.NormalDamageDealt.Should().Be(4);  // 2 HITs × NormalDmg 2
        });
    }

    // ── Test 2 ───────────────────────────────────────────────────────────

    [Fact]
    public void Resolve_TorrentVisibilityPhrasing_ServiceBehaviourIdenticalToBlast()
    {
        // The service layer has no visibility enforcement — Blast and Torrent differ only
        // in orchestrator prompt text. This test verifies that a Torrent weapon resolves
        // identically to Blast given the same inputs (i.e. no special service branching).
        var blastCtx = new BlastShootContext(
            AttackDice: new[] { 6, 4 }, HitThreshold: 3, NormalDmg: 4, CritDmg: 5,
            WeaponRules: SpecialRuleParser.Parse("Blast 1\""),
            Targets: new[]
            {
                new BlastTargetInput(Guid.Parse("00000001-0000-0000-0000-000000000001"), "Target1", false,
                    Array.Empty<int>(), false, false, 7, 14, 14),
            });

        var torrentCtx = blastCtx with
        {
            WeaponRules = SpecialRuleParser.Parse("Torrent 1\"")
        };

        var blastResult   = Sut().Resolve(blastCtx);
        var torrentResult = Sut().Resolve(torrentCtx);

        blastResult.SharedCritHits.Should().Be(torrentResult.SharedCritHits);
        blastResult.SharedNormalHits.Should().Be(torrentResult.SharedNormalHits);
        blastResult.TargetResults[0].NormalDamageDealt
            .Should().Be(torrentResult.TargetResults[0].NormalDamageDealt);
    }

    // ── Test 3 ───────────────────────────────────────────────────────────

    [Fact]
    public void Resolve_FriendlyFireIncluded_FriendlyTargetDamageResolved()
    {
        // Friendly operative is treated identically to an enemy by the service layer.
        // IsFriendly is informational only (used by orchestrator for the warning).
        var ctx = new BlastShootContext(
            AttackDice: new[] { 6, 6 },   // 2 crits
            HitThreshold: 3, NormalDmg: 2, CritDmg: 4,
            WeaponRules: SpecialRuleParser.Parse("Blast 2\""),
            Targets: new[]
            {
                new BlastTargetInput(Guid.Parse("ae000001-0000-0000-0000-000000000001"), "Enemy",   IsFriendly: false,
                    Array.Empty<int>(), false, false, 7, 14, 14),
                new BlastTargetInput(Guid.Parse("af000001-0000-0000-0000-000000000001"), "Ally",   IsFriendly: true,
                    Array.Empty<int>(), false, false, 7, 15, 15),
            });

        var result = Sut().Resolve(ctx);

        result.TargetResults.Should().HaveCount(2);
        // Both targets take identical damage from the same shared 2-CRIT pool, no saves
        result.TargetResults[0].CritDamageDealt.Should().Be(8);   // 2 × 4
        result.TargetResults[1].CritDamageDealt.Should().Be(8);
        result.TargetResults[1].CausedIncapacitation.Should().BeFalse(); // 15W − 8 = 7W, alive
    }

    // ── Test 4 ───────────────────────────────────────────────────────────

    [Fact]
    public void Resolve_PiercingCrits1_AppliedPerTargetBasedOnSharedCritCount()
    {
        // Heavy bolter (sweeping): Torrent 1", Piercing Crits 1.
        // Shared roll includes 1 crit → PiercingCrits removes 1 die from each target.
        // Each target has 3 defence dice but only 2 are rolled after Piercing Crits 1.
        var ctx = new BlastShootContext(
            AttackDice: new[] { 6, 5, 4 },   // 1 CRIT, 2 HITs
            HitThreshold: 3, NormalDmg: 4, CritDmg: 5,
            WeaponRules: SpecialRuleParser.Parse("Torrent 1\", Piercing Crits 1"),
            Targets: new[]
            {
                new BlastTargetInput(Guid.Parse("00000001-0000-0000-0000-000000000001"), "Target1", false,
                    new[] { 5, 4, 3 },     // 3 dice provided; Piercing Crits removes 1 → 2 used
                    InCover: false, IsObscured: false, SaveThreshold: 3,
                    CurrentWounds: 14, MaxWounds: 14),
            });

        var result = Sut().Resolve(ctx);

        // With 1 CRIT in pool, PiercingCrits 1 fires: defender rolls only 2 of 3 dice.
        // Dice used: [5, 4] → 2 saves. Unblocked: 0 CRITs (1 crit save cancels + 1 normal covers),
        // 1 HIT. Damage = 1 × 4 = 4.
        result.TargetResults[0].Blocks.Should().Be(2);
        result.TargetResults[0].NormalDamageDealt.Should().Be(4);
    }

    // ── Test 5 ───────────────────────────────────────────────────────────

    [Fact]
    public void Resolve_NoAdditionalTargets_SingleTargetBehaviourPreserved()
    {
        // When Targets contains only one entry (the primary), resolution is identical
        // to a standard single-target Shoot action.
        var ctx = new BlastShootContext(
            AttackDice: new[] { 5, 4, 3 },   // 3 HITs, 0 CRITs
            HitThreshold: 3, NormalDmg: 2, CritDmg: 4,
            WeaponRules: SpecialRuleParser.Parse("Blast 2\""),
            Targets: new[]
            {
                new BlastTargetInput(Guid.Parse("00000001-0000-0000-0000-000000000001"), "OnlyTarget", false,
                    new[] { 2 },           // 1 save die, fails (2 < 3+)
                    InCover: false, IsObscured: false, SaveThreshold: 3,
                    CurrentWounds: 14, MaxWounds: 14),
            });

        var result = Sut().Resolve(ctx);

        result.TargetResults.Should().HaveCount(1);
        result.TargetResults[0].NormalHits.Should().Be(3);
        result.TargetResults[0].Blocks.Should().Be(0);
        result.TargetResults[0].NormalDamageDealt.Should().Be(6); // 3 × 2
    }

    // ── Test 6 ───────────────────────────────────────────────────────────

    [Fact]
    public void Resolve_Saturate_CoverSaveSuppressedForAllTargets()
    {
        // Plague spewer (Torrent 2", Saturate, Severe).
        // All targets in cover; Saturate should suppress cover save for each.
        var ctx = new BlastShootContext(
            AttackDice: new[] { 5, 4, 3 },   // 3 HITs (no crits → Severe fires)
            HitThreshold: 2, NormalDmg: 3, CritDmg: 3,
            WeaponRules: SpecialRuleParser.Parse("Torrent 2\", Saturate, Severe"),
            Targets: new[]
            {
                new BlastTargetInput(Guid.Parse("00000001-0000-0000-0000-000000000001"), "CoveredTarget1", false,
                    Array.Empty<int>(),
                    InCover: true,     // would normally add +1 cover save
                    IsObscured: false, SaveThreshold: 3, CurrentWounds: 14, MaxWounds: 14),
                new BlastTargetInput(Guid.Parse("00000002-0000-0000-0000-000000000002"), "CoveredTarget2", false,
                    Array.Empty<int>(),
                    InCover: true,
                    IsObscured: false, SaveThreshold: 3, CurrentWounds: 14, MaxWounds: 14),
            });

        var result = Sut().Resolve(ctx);

        // Severe fires: 0 crits → convert 1 HIT to CRIT → pool: 1 CRIT, 2 HITs.
        // Saturate: no cover saves on either target.
        // Zero defence dice entered → all damage goes through.
        result.TargetResults.Should().AllSatisfy(r =>
        {
            r.Blocks.Should().Be(0);                    // Saturate: cover save suppressed
            r.CritDamageDealt.Should().Be(3);           // 1 CRIT × 3
            r.NormalDamageDealt.Should().Be(6);         // 2 HITs × 3
        });
    }
}
```

---

## 10. `spec.md` Note

Add the following entry in `spec.md` under the **Weapon Special Rules** section, adjacent to the
existing references to `spike-weapon-rules.md`:

> **Blast x″ / Torrent x″ — Multi-Target Resolution**
> Full UX design, CLI transcript, service design (`BlastResolutionService`, `BlastShootContext`,
> `TargetResolutionResult`), schema addendum (`action_blast_targets`), rule-interaction analysis,
> and xUnit test stubs are documented in **`spike-blast-torrent.md`**.
> Schema migration: `Migration_003` (see `spike-schema-ddl.md` for placement within migration
> sequence).

---

*End of spike.*
