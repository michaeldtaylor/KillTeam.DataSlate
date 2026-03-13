# Spike: Weapon Special Rules Enforcement

**Status**: Draft  
**Author**: Spike  
**Date**: 2025-07  
**Area**: Kill Team Game Tracking — Weapon Special Rules

---

## Introduction

Kill Team V3.0 weapons carry up to a dozen special rules that modify combat resolution in distinct
ways: some modify dice classification, others change damage values, restrict activations, prompt
re-rolls, or require entirely different targeting flows. This spike defines the data model for parsed
weapon rules, categorises every rule into an enforcement tier, shows updated `CombatResolutionService`
and `FightResolutionService` signatures, specifies the re-roll CLI UX, and provides worked examples
and xUnit tests sufficient for a developer to implement rule enforcement from this document alone.

The app is a .NET 10 console application using Spectre.Console for all interactive prompts. All rule
enforcement logic lives in stateless domain services (`CombatResolutionService`,
`FightResolutionService`). The orchestrators (`ShootSessionOrchestrator`, `FightSessionOrchestrator`)
are responsible for calling the correct service methods in the correct order and prompting for
re-rolls.

**Reference**: See `spike-fight-ui.md` for the fight action state machine, `FightDicePool`,
`FightDie`, `DieResult`, `FightResolutionService`, and `FightSessionOrchestrator`. This document
extends those with Brutal, Shock, Stun, Severe, and Rending enforcement.

---

## Rules Recap

> **Official rule text (verbatim, Kill Team V3.0 reference card):**
>
> | Rule | Exact definition |
> |---|---|
> | **Accurate x** | Retain x normal hits without rolling. Stacks, max 2. |
> | **Balanced** | Re-roll one attack dice. |
> | **Blast x** | Make attacks against all operatives within x″ of target, visible to target. |
> | **Brutal** | Can only block with Criticals. |
> | **Ceaseless** | Re-roll all results of one number (e.g. 2's). |
> | **Devastating x** | Crits inflict x damage. |
> | **D″ Devastating x** | Crits inflict x damage to all operatives within D″. |
> | **Heavy** | Cannot shoot in same activation as moved. |
> | **Heavy (x only)** | Can only move x″ in same activation as shooting. |
> | **Hot** | After shooting roll 1D6; if lower than Hit stat, suffer 2x result dmg. |
> | **Lethal x** | Inflict crits with x+ instead of 6+. |
> | **Limited x** | Has x uses per battle. |
> | **Piercing x** | Remove x defence dice before rolling. |
> | **Piercing x Crits** | Remove x defence dice before rolling, if critical success rolled. |
> | **Punishing** | Retain a fail as a success if any crits retained. |
> | **Range x** | Target must be within x″ of shooter. |
> | **Relentless** | Re-roll any or all attack dice. |
> | **Rending** | Convert a hit to a critical if any criticals retained. |
> | **Saturate** | The defender cannot retain any cover saves. |
> | **Seek** | Targets cannot use terrain for cover. |
> | **Seek Light** | Targets cannot use light terrain for cover. |
> | **Severe** | Convert a hit to a critical if no criticals retained. |
> | **Shock** | First Critical strike discards opponent's worst success. |
> | **Silent** | Can Shoot whilst on Conceal order. |
> | **Stun** | Remove 1APL from target if any Critical Successes retained. |
> | **Torrent x** | Make attacks against all operatives within x″ of target, visible to shooter. |
>
> **Obscured** (target state, not a weapon rule): critical hits become regular hits and one success
> is discarded.
>
> **Fight Assist**: +1 to HIT per non-engaged ally within enemy control range.
>
> **CP Re-roll**: spend 1CP to re-roll 1 die (never re-roll a re-roll).

---

## 1. Data Model — WeaponSpecialRule

### 1.1 Enum and Record

```csharp
/// <summary>
/// All recognised Kill Team V3.0 weapon special rules.
/// Unknown covers roster entries that have no V3.0 reference card definition (e.g. Poison, Toxic).
/// </summary>
public enum SpecialRuleKind
{
    Accurate,           // Accurate x        — retain x normal hits without rolling
    Balanced,           // Balanced          — re-roll one attack die
    Blast,              // Blast x           — attacks all within x″ of target visible to target
    Brutal,             // Brutal            — opponent can only block with Crits
    Ceaseless,          // Ceaseless         — re-roll all dice showing one specific value
    Devastating,        // Devastating x     — crits inflict x damage
    DevastatingAoE,     // D″ Devastating x  — crits inflict x dmg to operatives within D″
    Heavy,              // Heavy             — cannot shoot if moved this activation
    HeavyRestricted,    // Heavy (x only)    — can move at most x″ in same activation as shooting
    Hot,                // Hot               — post-shoot self-damage roll
    Lethal,             // Lethal x          — crit threshold lowered to x+
    Limited,            // Limited x         — x uses per battle
    Piercing,           // Piercing x        — remove x defence dice before rolling
    PiercingCrits,      // Piercing x Crits  — remove x defence dice only if crit was rolled
    Punishing,          // Punishing         — retain a fail as success if any crits retained
    Range,              // Range x           — display only; no distance enforcement in CLI
    Relentless,         // Relentless        — re-roll any or all attack dice
    Rending,            // Rending           — convert 1 hit → crit if any crits retained
    Saturate,           // Saturate          — defender cannot retain cover saves
    Seek,               // Seek              — terrain provides no cover (all types)
    SeekLight,          // Seek Light        — light terrain provides no cover
    Severe,             // Severe            — convert 1 hit → crit if NO crits retained
    Shock,              // Shock             — first crit strike discards opponent's worst die
    Silent,             // Silent            — may shoot while on Conceal order
    Stun,               // Stun              — remove 1APL from target if any crits retained
    Torrent,            // Torrent x         — attacks all within x″ of target visible to shooter
    Unknown,            // Poison, Toxic, or any unrecognised rule — display only
}

/// <summary>
/// A parsed weapon special rule. Parameter holds the numeric argument (e.g. 3 for "Devastating 3")
/// where applicable. RawText preserves the original string for Unknown rules.
/// </summary>
public record WeaponSpecialRule(
    SpecialRuleKind Kind,
    int?    Parameter = null,
    string? RawText   = null
);
```

**Parameter mapping per kind:**

| Kind | Parameter meaning | Example source string |
|---|---|---|
| `Accurate` | x (1 or 2) | `"Accurate 2"` |
| `Blast` | distance in inches | `"Blast 1\""` |
| `Devastating` | damage per crit | `"Devastating 3"` |
| `DevastatingAoE` | damage per crit (range = D, implicit) | `"D\" Devastating 2"` |
| `HeavyRestricted` | max move (inches) when shooting; `RawText` holds activity label | `"Heavy (Dash only)"` |
| `Hot` | *null* (no parameter) | `"Hot"` |
| `Lethal` | crit threshold | `"Lethal 5+"` |
| `Limited` | uses per battle | `"Limited 2"` |
| `Piercing` | dice removed | `"Piercing 1"` |
| `PiercingCrits` | dice removed if crit | `"Piercing Crits 1"` |
| `Range` | inches | `"Range 8\""` |
| `Torrent` | inches | `"Torrent 2\""` |
| `Unknown` | *null* | `"Poison"`, `"Toxic"` |
| All others | *null* | `"Brutal"`, `"Severe"`, etc. |

---

### 1.2 SpecialRuleParser

```csharp
/// <summary>
/// Parses the comma-separated specialRules field from the roster JSON into a typed list.
/// Input examples:
///   "Brutal, Severe, Shock, Poison"
///   "Range 8\", Piercing 1"
///   "Devastating 3, Heavy (Dash only), Piercing 1, Silent"
///   ""  (empty — returns empty list)
/// </summary>
public static class SpecialRuleParser
{
    public static IReadOnlyList<WeaponSpecialRule> Parse(string? input)
    {
        if (string.IsNullOrWhiteSpace(input))
            return Array.Empty<WeaponSpecialRule>();

        return SplitRuleTokens(input)
            .Select(ParseSingleRule)
            .ToList();
    }

    // Splits on commas but respects quoted inch marks (e.g. Range 8", Torrent 2").
    // The inch mark " is a literal character in JSON strings after deserialization.
    private static IEnumerable<string> SplitRuleTokens(string input)
    {
        // Simple split on comma then trim; inch marks do not contain commas so no quoting needed.
        return input.Split(',').Select(t => t.Trim()).Where(t => t.Length > 0);
    }

    private static WeaponSpecialRule ParseSingleRule(string token)
    {
        // Normalise for matching: lowercase, strip trailing inch marks and "+"
        var normalized = token.TrimEnd('"').Trim().ToLowerInvariant();

        // Patterns with numeric parameter
        if (TryParseParametered("accurate", normalized, token, out var r)) return r!;
        if (TryParseParametered("blast", normalized, token, out r)) return r!;
        if (TryParseParametered("devastating", normalized, token, out r)) return r!;
        if (TryParseParametered("limited", normalized, token, out r)) return r!;
        if (TryParseParametered("piercing crits", normalized, token, out r)) return r!;
        if (TryParseParametered("piercing", normalized, token, out r)) return r!;
        if (TryParseParametered("range", normalized, token, out r)) return r!;
        if (TryParseParametered("torrent", normalized, token, out r)) return r!;

        // Lethal x+ — strip "+" from the number
        if (normalized.StartsWith("lethal "))
        {
            var numStr = normalized["lethal ".Length..].TrimEnd('+');
            return int.TryParse(numStr, out var n)
                ? new WeaponSpecialRule(SpecialRuleKind.Lethal, n, token)
                : new WeaponSpecialRule(SpecialRuleKind.Unknown, null, token);
        }

        // D" Devastating x
        if (normalized.StartsWith("d\"") || normalized.StartsWith("d\" devastating") ||
            normalized.StartsWith("d devastating"))
        {
            var parts = normalized.Split(' ');
            var last  = parts.Last().TrimEnd('+');
            return int.TryParse(last, out var n)
                ? new WeaponSpecialRule(SpecialRuleKind.DevastatingAoE, n, token)
                : new WeaponSpecialRule(SpecialRuleKind.Unknown, null, token);
        }

        // Heavy (x only) — parameterless but with activity label in parentheses
        if (normalized.StartsWith("heavy ("))
        {
            // Extract the inner label e.g. "Dash only" from "Heavy (Dash only)"
            var rawLabel = token.Contains('(')
                ? token[(token.IndexOf('(') + 1)..token.LastIndexOf(')')]
                : null;
            return new WeaponSpecialRule(SpecialRuleKind.HeavyRestricted, null, rawLabel?.Trim());
        }

        // Simple keyword rules (no parameter)
        return normalized switch
        {
            "balanced"   => new WeaponSpecialRule(SpecialRuleKind.Balanced),
            "brutal"     => new WeaponSpecialRule(SpecialRuleKind.Brutal),
            "ceaseless"  => new WeaponSpecialRule(SpecialRuleKind.Ceaseless),
            "heavy"      => new WeaponSpecialRule(SpecialRuleKind.Heavy),
            "hot"        => new WeaponSpecialRule(SpecialRuleKind.Hot),
            "punishing"  => new WeaponSpecialRule(SpecialRuleKind.Punishing),
            "relentless" => new WeaponSpecialRule(SpecialRuleKind.Relentless),
            "rending"    => new WeaponSpecialRule(SpecialRuleKind.Rending),
            "saturate"   => new WeaponSpecialRule(SpecialRuleKind.Saturate),
            "seek light" => new WeaponSpecialRule(SpecialRuleKind.SeekLight),
            "seek"       => new WeaponSpecialRule(SpecialRuleKind.Seek),
            "severe"     => new WeaponSpecialRule(SpecialRuleKind.Severe),
            "shock"      => new WeaponSpecialRule(SpecialRuleKind.Shock),
            "silent"     => new WeaponSpecialRule(SpecialRuleKind.Silent),
            "stun"       => new WeaponSpecialRule(SpecialRuleKind.Stun),
            _            => new WeaponSpecialRule(SpecialRuleKind.Unknown, null, token),
        };
    }

    private static bool TryParseParametered(
        string prefix, string normalized, string original,
        out WeaponSpecialRule? result)
    {
        if (!normalized.StartsWith(prefix + " ")) { result = null; return false; }

        var rest   = normalized[(prefix.Length + 1)..].TrimEnd('"').Trim();
        var kind   = prefix switch
        {
            "accurate"      => SpecialRuleKind.Accurate,
            "blast"         => SpecialRuleKind.Blast,
            "devastating"   => SpecialRuleKind.Devastating,
            "limited"       => SpecialRuleKind.Limited,
            "piercing crits"=> SpecialRuleKind.PiercingCrits,
            "piercing"      => SpecialRuleKind.Piercing,
            "range"         => SpecialRuleKind.Range,
            "torrent"       => SpecialRuleKind.Torrent,
            _               => SpecialRuleKind.Unknown,
        };

        result = int.TryParse(rest, out var n)
            ? new WeaponSpecialRule(kind, n, original)
            : new WeaponSpecialRule(SpecialRuleKind.Unknown, null, original);
        return true;
    }
}
```

**Note on `Poison` and `Toxic`**: These appear in the Plague Marines roster JSON but are not defined
in the V3.0 reference card. They parse to `Unknown` and are displayed verbatim in the weapon stats
panel. No combat effect is applied.

---

## 2. Rule Enforcement Tiers

### Tier 1 — Combat Resolution Modifiers
Enforced directly inside `CombatResolutionService` and `FightResolutionService`. The caller passes
the weapon's rule list and the service applies the effect during dice classification, pool building,
or damage calculation.

| Rule | When applied | Effect |
|---|---|---|
| **Lethal x** | Die classification | Roll ≥ x counts as CRIT (instead of only 6) |
| **Accurate x** | Pool building (pre-roll) | Add x normal HITs to the pool before rolling remaining dice |
| **Punishing** | Pool building (post-roll) | If any crits retained, also retain one fail as a HIT |
| **Rending** | Pool building (post-roll) | If any crits retained, convert one HIT → CRIT |
| **Severe** | Pool building (post-roll) | If zero crits retained, convert one HIT → CRIT |
| **Devastating x** | Damage calculation | Crit damage = x (overrides weapon `CritDmg` stat) |
| **Piercing x** | Defence pool (pre-roll) | Remove x defender dice before defender rolls |
| **Piercing Crits x** | Defence pool (pre-roll) | Remove x defender dice only if attacker has ≥ 1 crit |
| **Saturate** | Cover save | `inCover` is treated as `false` — cover save cannot be retained |
| **Seek** | Cover save | All terrain cover ignored (implementation note below) |
| **Seek Light** | Cover save | Light terrain cover ignored (implementation note below) |
| **Brutal** *(fight)* | Block eligibility | Opponent's HIT dice cannot be used to Block |
| **Shock** *(fight)* | First crit Strike | Opponent's worst remaining die is discarded |
| **Stun** *(fight)* | Post-fight state | Target's APL reduced by 1 until end of their next activation |

> **Seek / Seek Light**: The CLI does not currently track terrain type (there is no distance or
> terrain model). For the MVP: if the weapon has `Seek`, treat `inCover = false` regardless of what
> the player answered; if `Seek Light`, no effect yet (terrain type is unknown). Mark with a TODO
> comment in `ShootContext` documentation.

### Tier 2 — Dice Re-roll Prompts
Enforced by the shoot/fight orchestrator immediately after initial dice are entered and before the
dice pool is finalised. The orchestrator asks the player whether to re-roll before passing dice to
the resolution service.

| Rule | Trigger | Re-roll scope |
|---|---|---|
| **Balanced** | After attacker dice entry | One die of the player's choice |
| **Ceaseless** | After attacker dice entry | All dice showing one specific face value |
| **Relentless** | After attacker dice entry | Any or all dice (player selects) |

CP Re-roll (not a weapon rule — available for any Shoot or Fight action): spend 1CP to re-roll 1
die, attack **or** defence. A re-rolled die can never be re-rolled again. See §4 for UX.

### Tier 3 — Activation Restrictions
Enforced by the game session orchestrator **before** a weapon is offered to the player. If the
restriction is violated the weapon is greyed out or a warning is shown.

| Rule | Enforcement |
|---|---|
| **Heavy** | Weapon is unavailable if the operative moved (Reposition, Dash, FallBack, or Charge) earlier in this activation |
| **Heavy (x only)** | Weapon is available, but the only movement actions offered are those ≤ x″ (e.g. "Dash only" means only Dash is available as a movement action in this activation after shooting) |
| **Silent** | Weapon is available even if operative is on Conceal order (bypass the normal "must be Engage order to Shoot" check) |
| **Limited x** | Weapon is offered only if `UsesRemaining > 0`. Uses are tracked in `GameOperativeState.WeaponUses` (weapon GUID → int). Decremented after each use. |
| **Range x** | Display only in weapon list (no distance enforcement in CLI). Shown as `"Range x\""` beside weapon name. |

### Tier 4 — Multi-Target / Alternative Targeting Flow
These rules replace the single-target Shoot flow entirely and are handled by a separate targeting
loop in the orchestrator.

| Rule | Flow change |
|---|---|
| **Blast x** | After target selection, identify all operatives within x″ of target that are visible to the target. Attack each with the same dice roll (resolved sequentially). CLI: not enforceable without distance tracking — display rule text only, prompt player to confirm which operatives are in range |
| **Torrent x** | As Blast x but "visible to shooter" instead of "visible to target". Same CLI limitation |

> **MVP**: Blast and Torrent are display-only. The player is shown the rule text and resolves each
> additional target manually. A future iteration could add a distance/visibility model.

### Tier 5 — Display Only / Unknown

| Rule | Note |
|---|---|
| **Range x** | Shown in weapon stat line; no enforcement (no distance model) |
| **Hot** | Shown in weapon stat line; not enforced in MVP (requires post-shoot self-damage roll — add TODO) |
| **Poison** | Not in V3.0 reference — shown verbatim in weapon stats |
| **Toxic** | Not in V3.0 reference — shown verbatim in weapon stats |
| **Seek Light** | Partially display-only until terrain type is tracked |

---

## 3. Updated Service Signatures

### 3.1 ShootContext

Rather than expanding `ResolveShoot` with many individual flags, all Shoot inputs are bundled into a
`ShootContext` record. This keeps the signature stable as new rules are added.

```csharp
/// <summary>
/// All inputs required to resolve a Shoot action, including parsed weapon special rules.
/// </summary>
public record ShootContext(
    int[]                          AttackDice,      // raw D6 results (including misses)
    int[]                          DefenceDice,     // raw D6 results (before Piercing removal)
    bool                           InCover,         // true if target is in cover
    int                            HitThreshold,    // effective hit threshold (post-Injured adjustment)
    int                            SaveThreshold,   // defender's save stat (e.g. 3)
    int                            NormalDmg,
    int                            CritDmg,         // base; overridden by Devastating x if present
    IReadOnlyList<WeaponSpecialRule> WeaponRules     // parsed from weapon.SpecialRules
)
{
    // Convenience helpers — avoid scattering LINQ in the service
    public bool HasRule(SpecialRuleKind kind) =>
        WeaponRules.Any(r => r.Kind == kind);

    public WeaponSpecialRule? GetRule(SpecialRuleKind kind) =>
        WeaponRules.FirstOrDefault(r => r.Kind == kind);

    public int EffectiveCritDmg =>
        GetRule(SpecialRuleKind.Devastating)?.Parameter ?? CritDmg;
}
```

### 3.2 Updated CombatResolutionService

```csharp
public class CombatResolutionService
{
    // ── Shoot ────────────────────────────────────────────────────────────────

    /// <summary>
    /// Full shoot resolution using a ShootContext.
    /// Applies Lethal, Accurate, Punishing, Rending, Severe, Piercing, PiercingCrits,
    /// Devastating, and Saturate automatically from context.WeaponRules.
    /// </summary>
    public CombatResult ResolveShoot(ShootContext context);

    // Legacy overload — delegates to ResolveShoot(ShootContext) with empty rules.
    // Kept for backward-compatibility with existing tests; do not add new callers.
    public CombatResult ResolveShoot(
        int[] attackDice, int[] defenceDice, bool inCover,
        int hitThreshold, int saveThreshold, int normalDmg, int critDmg)
        => ResolveShoot(new ShootContext(
               attackDice, defenceDice, inCover,
               hitThreshold, saveThreshold, normalDmg, critDmg,
               Array.Empty<WeaponSpecialRule>()));

    // ── Fight (roll phase only — unchanged from spec.md) ──────────────────

    public FightRollResult ResolveFightRolls(
        int[] attackerDice, int[] defenderDice,
        int   attackerHitThreshold, int defenderHitThreshold);

    // ── Die classification helpers (Lethal support) ──────────────────────

    /// <summary>
    /// Classify a single raw D6 roll.
    /// Roll == 6 → Crit. Roll >= effectiveCritThreshold → Crit. Roll >= hitThreshold → Hit. Else Miss.
    /// effectiveCritThreshold defaults to 6 (standard); set lower for Lethal x weapons.
    /// </summary>
    public DieResult CalculateDie(int roll, int hitThreshold, int effectiveCritThreshold = 6);

    // ── Internal pipeline steps (accessible for unit testing) ────────────

    /// <summary>
    /// Apply Piercing/PiercingCrits: returns the number of defence dice to discard before rolling.
    /// piercingX: base Piercing parameter. piercingCritsX: PiercingCrits parameter. attackerCritCount:
    /// crits retained after attacker dice classification.
    /// </summary>
    public int CalculatePiercingReduction(
        int piercingX, int piercingCritsX, int attackerCritCount);

    /// <summary>
    /// Apply Rending post-classification: if any crits in pool, convert first HIT → CRIT.
    /// Returns updated counts.
    /// </summary>
    public (int normals, int crits) ApplyRending(int normals, int crits);

    /// <summary>
    /// Apply Severe post-classification: if zero crits, convert first HIT → CRIT.
    /// Returns updated counts.
    /// </summary>
    public (int normals, int crits) ApplySevere(int normals, int crits);

    /// <summary>
    /// Apply Punishing post-classification: if any crits, add 1 to normals (a fail retained as success).
    /// Returns updated normal count.
    /// </summary>
    public int ApplyPunishing(int normals, int crits, int failCount);
}
```

#### ResolveShoot Internal Pipeline

```
1. Lethal: compute effectiveCritThreshold = Lethal?.Parameter ?? 6
2. Accurate: prepend Accurate.Parameter synthetic HIT dice (count as normal successes, not rolled)
3. Classify each attacker die: CalculateDie(roll, hitThreshold, effectiveCritThreshold)
   → collect (normals, crits, fails)
4. Punishing: if HasRule(Punishing) && crits > 0 && fails > 0 → normals += 1
5. Rending:   if HasRule(Rending) && crits > 0 && normals > 0  → crits += 1; normals -= 1
6. Severe:    if HasRule(Severe)  && crits == 0 && normals > 0 → crits += 1; normals -= 1
7. Piercing: piercingReduction = CalculatePiercingReduction(...)
   effectiveDefenceCount = Max(0, defenceDice.Length - piercingReduction)
8. Cover save:
   effectiveInCover = HasRule(Saturate) || HasRule(Seek) ? false : context.InCover
   if effectiveInCover → prepend 1 synthetic normal-save die to the defence pool
9. Classify defence dice (save): each die >= saveThreshold → save; else fail
   (up to effectiveDefenceCount dice, plus the synthetic cover die if applicable)
10. Blocking algorithm (Shoot — app-allocated, optimal):
    (a) 1 crit save cancels 1 crit attack
    (b) 2 normal saves cancel 1 crit attack (if crits remain)
    (c) 1 normal save cancels 1 normal attack
11. Effective critDmg = context.EffectiveCritDmg
    Damage = unblocked_crits × effectiveCritDmg + unblocked_normals × normalDmg
12. Return CombatResult
```

```csharp
public record CombatResult(
    int  AttackerNormalHits,
    int  AttackerCritHits,
    int  DefenderSaves,         // total saves retained (including cover)
    int  NormalDamageDealt,
    int  CritDamageDealt,
    bool CausedIncapacitation   // caller provides current wounds to compute this
);
```

### 3.3 Updated FightResolutionService

The fight resolution service from `spike-fight-ui.md` gains the following additions.

```csharp
public class FightResolutionService
{
    // ── Existing methods (unchanged) ────────────────────────────────────

    public DieResult  CalculateDie(int roll, int hitThreshold);
    public FightDicePool CalculateDice(IEnumerable<int> rolls, int hitThreshold, DieOwner owner);
    public FightDicePool AutoRoll(int atkDice, int hitThreshold, DieOwner owner);
    public int ApplyStrike(FightDie die, int normalDmg, int critDmg);
    public (FightDicePool UpdatedActivePool, FightDicePool UpdatedOpponentPool) ApplySingleBlock(
        FightDicePool activePool, FightDicePool opponentPool,
        FightDie spendDie, FightDie targetDie);
    public bool IsFightOver(
        FightDicePool attackerPool, FightDicePool defenderPool,
        GameOperativeState attackerState, GameOperativeState defenderState);

    // ── Updated: Brutal changes block eligibility ────────────────────

    /// <summary>
    /// Returns available actions for the active player given both pools.
    /// brutalWeapon: set to true when the OPPONENT's weapon has Brutal — i.e. the active player
    /// is the defender and the attacker's weapon is Brutal. When true, only CRIT dice may Block.
    /// </summary>
    public IReadOnlyList<FightActionChoice> GetAvailableActions(
        FightDicePool activePool,
        FightDicePool opponentPool,
        bool          brutalWeapon = false);

    // ── New: Shock ────────────────────────────────────────────────────

    /// <summary>
    /// Discards the opponent's worst remaining success die (lowest RolledValue).
    /// Called by the orchestrator on the first CRIT Strike made with a Shock weapon.
    /// Returns the updated opponent pool. No-ops if opponentPool is empty.
    /// </summary>
    public FightDicePool ApplyShock(FightDicePool opponentPool);

    // ── New: Post-fight state effects ────────────────────────────────

    /// <summary>
    /// Returns true if Stun should be applied: weapon has Stun AND active player retained any crits.
    /// The orchestrator applies the APL reduction to GameOperativeState after the fight.
    /// </summary>
    public bool ShouldApplyStun(IReadOnlyList<WeaponSpecialRule> weaponRules, int attackerCrits);

    // ── New: Lethal support for fights ───────────────────────────────

    /// <summary>
    /// Fight variant of CalculateDie with Lethal x support.
    /// If weapon has Lethal x, roll >= x counts as Crit (instead of only 6).
    /// </summary>
    public DieResult CalculateDie(int roll, int hitThreshold, int effectiveCritThreshold);

    // ── New: Post-roll transforms (Rending, Severe) ──────────────────

    /// <summary>
    /// Apply Rending to a completed FightDicePool: if any Crits remain, convert the first HIT die
    /// to a CRIT die. Returns the (potentially) updated pool.
    /// </summary>
    public FightDicePool ApplyRending(FightDicePool pool);

    /// <summary>
    /// Apply Severe to a completed FightDicePool: if zero Crits remain, convert the first HIT die
    /// to a CRIT die. Returns the (potentially) updated pool.
    /// </summary>
    public FightDicePool ApplySevere(FightDicePool pool);
}
```

#### Brutal — Block Eligibility Change

When the **attacker's** weapon is Brutal, the **defender** can only use CRIT dice to Block. In
`GetAvailableActions`, when `brutalWeapon = true`:

```
Normal Block pairings (HIT die → HIT die) are excluded entirely.
Only Crit Block pairings (CRIT die → any die) are generated.
```

The orchestrator passes `brutalWeapon: true` when building the *defender's* available actions:

```csharp
// In FightSessionOrchestrator.RunDefenderTurn():
bool brutalWeapon = attackerWeapon.ParsedRules.HasRule(SpecialRuleKind.Brutal);
var actions = _resolutionService.GetAvailableActions(defenderPool, attackerPool, brutalWeapon);
```

#### Shock — First Crit Strike Discards Worst Opponent Die

Shock triggers once per fight, on the first CRIT Strike by the Shock weapon's user. The
orchestrator tracks `shockTriggered` as a local bool:

```csharp
// In FightSessionOrchestrator.RunAttackerTurn():
if (attackerWeapon.ParsedRules.HasRule(SpecialRuleKind.Shock)
    && !_shockTriggered
    && chosenAction.ActionType == FightActionType.Strike
    && chosenAction.SpendDie.Result == DieResult.Crit)
{
    defenderPool = _resolutionService.ApplyShock(defenderPool);
    _shockTriggered = true;
    _console.MarkupLine("[yellow]⚡ Shock! Defender's worst die discarded![/]");
}
```

`ApplyShock` removes the die with the lowest `RolledValue` from the opponent pool (ties broken by
lowest `Id`):

```csharp
public FightDicePool ApplyShock(FightDicePool opponentPool)
{
    if (opponentPool.IsEmpty) return opponentPool;
    var worst = opponentPool.Remaining.OrderBy(d => d.RolledValue).ThenBy(d => d.Id).First();
    return opponentPool.CancelDie(worst.Id);
}
```

#### Stun — Post-Fight State Effect

Stun is not a damage modifier. After `FightSessionOrchestrator.Run()` returns, the calling command
handler checks:

```csharp
if (_resolutionService.ShouldApplyStun(attackerWeapon.ParsedRules, result.AttackerCritHits))
{
    defenderState.StunnedUntilEndOfNextActivation = true;  // APL -= 1 applied during activation
}
```

`GameOperativeState` gains a `StunnedUntilEndOfNextActivation` flag. At the start of the defender's
next activation, APL is temporarily reduced by 1 and the flag cleared.

---

## 4. Re-roll UX Flow

Re-rolls are prompted by the **orchestrator** after initial dice entry, before the pool is passed to
the resolution service. Re-roll rules apply to **attack dice only** unless explicitly noted. A die
can only ever be re-rolled once.

### 4.1 Balanced — Re-roll One Die

```
──────────────────────────────────────────────────────────
  Balanced: You may re-roll one attack die.
──────────────────────────────────────────────────────────

Your rolled dice:  1:6  2:4  3:2  4:1
Select a die to re-roll (or skip):
  > 1: rolled 6  (CRIT)
    2: rolled 4  (HIT)
    3: rolled 2  (MISS)
    4: rolled 1  (MISS)
    [Skip — keep all dice]

> 4: rolled 1  (MISS)
  Die 4 re-rolled: 1 → 5  (HIT ✓)
```

**Spectre.Console**: `SelectionPrompt<RerollOption>` where `RerollOption` is a discriminated union of
`Die(int index, int value, DieResult result)` and `Skip`. The `Skip` entry is always the last item.

The orchestrator replaces `attackDice[3]` with the new roll value, marks index 3 as re-rolled
(stored in a `HashSet<int> rerolledIndices`), then continues. Re-rolled dice are not eligible for a
second re-roll within the same weapon's rule set (though a CP Re-roll of a different die is still
valid).

### 4.2 Ceaseless — Re-roll All Dice Showing One Value

```
──────────────────────────────────────────────────────────
  Ceaseless: You may re-roll all dice showing one value.
──────────────────────────────────────────────────────────

Your rolled dice:  1:4  2:2  3:2  4:6  5:3

Which value would you like to re-roll? (or skip)
  > 4  (1 die — die 1)
    2  (2 dice — dice 2, 3)
    6  (1 die — die 4)
    3  (1 die — die 5)
    [Skip]

> 2  (2 dice — dice 2, 3)
  Re-rolling dice 2 and 3:
    Die 2: 2 → 5  (HIT ✓)
    Die 3: 2 → 4  (HIT ✓)
```

**Spectre.Console**: `SelectionPrompt<CeaselessOption>` enumerating each distinct rolled value plus
`Skip`. Internally, the orchestrator groups by raw value and replaces all matching dice.

### 4.3 Relentless — Re-roll Any or All Dice

```
──────────────────────────────────────────────────────────
  Relentless: You may re-roll any or all attack dice.
──────────────────────────────────────────────────────────

Your rolled dice:  1:6  2:4  3:2  4:1  5:3

Select dice to re-roll (space to toggle, enter to confirm; or select [Skip All]):
  [x] 1: rolled 6  (CRIT)
  [ ] 2: rolled 4  (HIT)
  [x] 3: rolled 2  (MISS)
  [x] 4: rolled 1  (MISS)
  [ ] 5: rolled 3  (HIT)
      [Skip All]

Confirmed. Re-rolling dice 1, 3, 4:
  Die 1: 6 → 3  (HIT)
  Die 3: 2 → 6  (CRIT ✓)
  Die 4: 1 → 4  (HIT ✓)
```

**Spectre.Console**: `MultiSelectionPrompt<RerollOption>` (the Spectre.Console multi-select
component). A "Skip All" entry at the bottom deselects all and confirms immediately.

### 4.4 CP Re-roll (Any Shoot or Fight Dice)

CP Re-roll is available at any point during dice entry — attack or defence. It is offered as a
prompt after the weapon re-roll prompts. Only one die may be CP-re-rolled per action. A die that was
already re-rolled (by weapon rule or by a previous CP Re-roll) cannot be re-rolled again.

```
──────────────────────────────────────────────────────────
  CP Re-roll available (1CP). Spend 1CP to re-roll one die?
  Team has 2CP.
──────────────────────────────────────────────────────────

  > Yes — select a die
    No  — keep all dice

> Yes — select a die

Which die to re-roll?
  Attack dice:
    1: rolled 3  (HIT)
    2: rolled 1  (MISS) ← already re-rolled — not shown
    3: rolled 2  (MISS)
  Defence dice:
    D1: rolled 5  (SAVE)
    D2: rolled 1  (MISS)
    [Cancel]

> 3: rolled 2  (MISS)
  Die 3: 2 → 5  (HIT ✓)
  1CP spent. Team CP: 2 → 1.
```

**Rules enforced in the prompt**:
- Already-re-rolled dice are excluded from the selection list.
- Team must have ≥ 1CP to trigger the prompt.
- The prompt is skipped silently if all dice have already been re-rolled or team has 0CP.

---

## 5. Worked Example — Flail of Corruption vs Chainsword (Fight)

### Operatives

| Operative | Player | Wounds | Weapon | ATK | Hit | NormalDmg | CritDmg | Special Rules |
|---|---|---|---|---|---|---|---|---|
| Plague Marine Fighter | Solomon | 14W | Flail of Corruption | 5 | 3+ | 4 | 5 | Brutal, Severe, Shock, Poison† |
| Assault Intercessor Grenadier | Michael | 14W | Chainsword | 5 | 3+ | 4 | 5 | — |

*† Poison = Unknown / display-only*

**Active rules in this fight:**
- **Brutal** (Fighter's weapon): Grenadier can only Block with CRIT dice
- **Severe** (Fighter's weapon): if Fighter rolls zero crits → convert 1 HIT to CRIT
- **Shock** (Fighter's weapon): first CRIT Strike by Fighter discards Grenadier's worst die

---

### Phase 1 — Weapon Selection

```
╔══════════════════════════════════════════════════════════════════╗
║                      ⚔  FIGHT ACTION  ⚔                          ║
║  Plague Marine Fighter  vs  Assault Intercessor Grenadier         ║
╚══════════════════════════════════════════════════════════════════╝

[Attacker — Solomon] Select Fighter's melee weapon:
  > Flail of Corruption  (ATK 5 | Hit 3+ | DMG 4/5 | Brutal, Severe, Shock, Poison)

[Defender — Michael] Select Grenadier's melee weapon:
  > Chainsword  (ATK 5 | Hit 3+ | DMG 4/5)
```

---

### Phase 2 — Dice Entry

```
──────────────────────────────────────────────────────────
  Fighter rolls 5 dice (Flail of Corruption, Hit 3+)
──────────────────────────────────────────────────────────

Enter Fighter's dice results (space or comma separated):
> 4 3 3 2 1

  F1: 4  → HIT  ✓
  F2: 3  → HIT  ✓
  F3: 3  → HIT  ✓
  F4: 2  → MISS ✗  (discarded)
  F5: 1  → MISS ✗  (discarded)

Fighter's pool before Severe: 3 HITs, 0 CRITs.

  ⚡ Severe triggered — no crits retained. Converting F1: HIT → CRIT.

Fighter's pool: 3 dice  (1 CRIT, 2 HITs)

──────────────────────────────────────────────────────────
  Grenadier rolls 5 dice (Chainsword, Hit 3+)
──────────────────────────────────────────────────────────

Enter Grenadier's dice results:
> 5 4 3 2 1

  G1: 5  → HIT  ✓
  G2: 4  → HIT  ✓
  G3: 3  → HIT  ✓
  G4: 2  → MISS ✗  (discarded)
  G5: 1  → MISS ✗  (discarded)

Grenadier's pool: 3 dice  (0 CRITs, 3 HITs)
```

> **Severe logic**: `ApplySevere(pool)` is called on the Fighter's pool after classification. It finds
> zero CRITs and converts the first HIT die (F1, lowest Id) to a CRIT by replacing its `Result`.
> The raw `RolledValue` (4) is preserved for display.

---

### Phase 3 — Dice Pool Display

```
┌─────────────────────────────────────┬────────────────────────────────────┐
│  Fighter (Attacker)                 │  Grenadier (Defender)              │
│  Wounds: 14                         │  Wounds: 14                        │
│  Brutal, Severe, Shock              │  —                                 │
├─────────────────────────────────────┼────────────────────────────────────┤
│  [F1: CRIT  rolled 4] ← Severe      │  [G1: HIT  rolled 5]               │
│  [F2: HIT   rolled 3]               │  [G2: HIT  rolled 4]               │
│  [F3: HIT   rolled 3]               │  [G3: HIT  rolled 3]               │
└─────────────────────────────────────┴────────────────────────────────────┘

⚠ Brutal active: Grenadier can only Block with CRIT dice.
```

---

### Phase 4 — Alternating Resolution

#### Turn 1 — Solomon's turn (Fighter, Attacker)

**Step 1 — Select die:**
```
─── Solomon's turn (Fighter) ───────────────────────────────────────
Your dice:  [F1: CRIT [rolled 4]]  [F2: HIT [rolled 3]]  [F3: HIT [rolled 3]]
Michael's:  [G1: HIT [rolled 5]]   [G2: HIT [rolled 4]]   [G3: HIT [rolled 3]]

Select a die to resolve:
  > F1: CRIT [rolled 4]
    F2: HIT [rolled 3]
    F3: HIT [rolled 3]
```

**Step 2 — F1: CRIT [rolled 4] — Shock triggers:**
```
F1: CRIT [rolled 4] selected.

  ⚔  1. STRIKE → 5 crit damage to Grenadier
  🛡  2. BLOCK → cancel a die (crit blocks any):
         G1: HIT [rolled 5]
         G2: HIT [rolled 4]
         G3: HIT [rolled 3]
> 1

  ⚔  Fighter strikes Grenadier for 5 CRIT damage!
  Grenadier: 14 → 9W.

  ⚡ Shock! First crit strike — Grenadier's worst die discarded.
     G3: HIT [rolled 3] removed from pool.
```

> **Shock logic**: Orchestrator checks `Shock` in weapon rules, `!shockTriggered`, and die is CRIT
> Strike → calls `ApplyShock(grenadierPool)`. Removes G3 (lowest `RolledValue` = 3). Sets
> `shockTriggered = true`.

Pool after Turn 1:

```
┌─────────────────────────────────────┬────────────────────────────────────┐
│  Fighter (Attacker)                 │  Grenadier (Defender)              │
│  Wounds: 14                         │  Wounds: 9                         │
├─────────────────────────────────────┼────────────────────────────────────┤
│  ~~[F1: CRIT  rolled 4]~~ ✓         │  [G1: HIT  rolled 5]               │
│  [F2: HIT   rolled 3]               │  [G2: HIT  rolled 4]               │
│  [F3: HIT   rolled 3]               │  ~~[G3: HIT  rolled 3]~~ ⚡ (Shock) │
└─────────────────────────────────────┴────────────────────────────────────┘
```

---

#### Turn 2 — Michael's turn (Grenadier, Defender)

Brutal is active: Grenadier holds only HIT dice (no CRITs). Block options are suppressed by
`GetAvailableActions(..., brutalWeapon: true)`.

**Step 1:**
```
─── Michael's turn (Grenadier) ─────────────────────────────────────
Your dice:  [G1: HIT [rolled 5]]  [G2: HIT [rolled 4]]
Solomon's:  [F2: HIT [rolled 3]]  [F3: HIT [rolled 3]]

Select a die to resolve:
  > G1: HIT [rolled 5]
    G2: HIT [rolled 4]
```

**Step 2 — G1: HIT [rolled 5] — Brutal restricts Block:**
```
G1: HIT [rolled 5] selected.

  ⚔  1. STRIKE → 4 normal damage to Fighter
  ⚠  Brutal active — normal dice cannot Block. Only CRIT dice may Block.
     (Grenadier has no CRIT dice — Block unavailable.)
> 1

  ⚔  Grenadier strikes Fighter for 4 NORMAL damage!
  Fighter: 14 → 10W.
```

> **Brutal logic**: `GetAvailableActions(grenadierPool, fighterPool, brutalWeapon: true)` returns
> only Strike entries. Normal Block pairings are suppressed; because Grenadier has no CRITs, no
> Block pairings at all are generated. The `⚠` line is rendered by the orchestrator when the active
> pool has no CRITs and `brutalWeapon = true`.

---

#### Turns 3–4 — Both Players Strike

The remaining dice (F2, F3 for Fighter; G2 for Grenadier) are resolved as Strikes. Brutal continues
to suppress Block for the Grenadier. The fight ends when all dice are spent.

**Final state**: Fighter 6W, Grenadier 1W. Grenadier is Injured (1W < 7W threshold).

---

## 6. Worked Example — Bolt Sniper Rifle (Mortis) Shoot

### Operative and Weapon

| Field | Value |
|---|---|
| Operative | Eliminator Sniper (Michael) |
| Move | 7″ |
| APL | 3 |
| Wounds | 12W |
| Save | 3+ |
| Weapon | Bolt sniper rifle (mortis) |
| ATK | 4 |
| Hit | 2+ |
| DMG | 3/3 (NormalDmg 3, CritDmg 3) |
| Special Rules | `Devastating 3, Heavy (Dash only), Piercing 1, Silent` |

Parsed rules:
```csharp
// SpecialRuleParser.Parse("Devastating 3, Heavy (Dash only), Piercing 1, Silent")
[
    new WeaponSpecialRule(SpecialRuleKind.Devastating,      Parameter: 3,    RawText: "Devastating 3"),
    new WeaponSpecialRule(SpecialRuleKind.HeavyRestricted,  Parameter: null, RawText: "Dash only"),
    new WeaponSpecialRule(SpecialRuleKind.Piercing,         Parameter: 1,    RawText: "Piercing 1"),
    new WeaponSpecialRule(SpecialRuleKind.Silent,           Parameter: null, RawText: "Silent"),
]
```

Target: Plague Marine Warrior, 14W, Save 3+.

---

### Activation-Level Checks (Tier 3)

Before the Shoot action is executed:

```
[Heavy (Dash only) check]
  Sniper already Dashed earlier this activation.
  Dashing is permitted with Heavy (Dash only).
  ✓ Weapon is available.
```

If the Sniper had used Reposition or Charge instead, the weapon would be unavailable:

```
  ⚠ Bolt sniper rifle (mortis) — Heavy (Dash only):
    You moved with Reposition this activation. This weapon cannot be used.
    Select a different weapon or end activation.
```

**Silent**: The Sniper may shoot while on Conceal order (normal Conceal-order Shoot restriction is
bypassed).

---

### Shoot Resolution

```
╔══════════════════════════════════════════════════════════════════╗
║                     🎯  SHOOT ACTION  🎯                          ║
║  Eliminator Sniper  →  Plague Marine Warrior                      ║
╚══════════════════════════════════════════════════════════════════╝

Weapon: Bolt sniper rifle (mortis)
  ATK 4 | Hit 2+ | DMG 3/3 | Devastating 3, Heavy (Dash only), Piercing 1, Silent

Is target in cover? (Y/N) > Y

──────────────────────────────────────────────────────────
  Sniper rolls 4 dice (Hit 2+)
──────────────────────────────────────────────────────────

Enter attack dice results:
> 6 5 4 2

  A1: 6  → CRIT  ✓
  A2: 5  → HIT   ✓
  A3: 4  → HIT   ✓
  A4: 2  → HIT   ✓

Attack pool: 4 dice  (1 CRIT, 3 HITs)

  Piercing 1: removing 1 defender die before defender rolls.
  Defender will roll 2 dice (3 base − 1 Piercing).
  Cover save: target is in cover — retaining 1 additional normal save.
  Defender total: 3 saves to roll (2 rolled + 1 cover).

──────────────────────────────────────────────────────────
  Warrior rolls defence dice (Save 3+) — 2 dice (after Piercing 1)
──────────────────────────────────────────────────────────

Enter defence dice results:
> 4 2

  D1: 4  → SAVE ✓  (4 >= 3+)
  D2: 2  → MISS ✗

  + Cover save: +1 normal save (retained automatically).

  Total saves: 2  (D1 normal + cover normal)
```

**Blocking algorithm** (app-allocated, Shoot mode):

```
Attacker: 1 CRIT, 3 HITs
Saves:    2 normal saves

Step (a): 1 crit save → cancel 1 crit attack:
  Not available — no crit saves. Skip.
Step (b): 2 normal saves → cancel 1 crit attack (if crits remain):
  2 normals available, 1 crit available → cancel 1 CRIT. Saves used: 2. Saves remaining: 0.
Step (c): remaining normal saves cancel normal attacks 1:1:
  0 saves remaining. Skip.

Unblocked: 0 CRITs, 3 HITs.
```

**Damage calculation** with Devastating 3:

```
  Devastating 3: each CRIT deals 3 damage (overrides base CritDmg of 3 — same value here).
  Unblocked crits:   0 × 3 = 0 crit damage.
  Unblocked normals: 3 × 3 = 9 normal damage.
  Total: 9 damage.

  Plague Marine Warrior: 14W → 5W.
```

> **Devastating x and CritDmg interaction**: `ShootContext.EffectiveCritDmg` returns
> `Devastating?.Parameter ?? CritDmg`. For the mortis rifle, both equal 3. For a hypothetical weapon
> with `CritDmg = 5` and `Devastating 3`, the effective crit damage would be 3, not 5 — Devastating
> **overrides** (lowers) the base stat.

**Saturate example** (for contrast — this weapon does not have Saturate):

If the weapon had `Saturate`, step 8 of the pipeline would set `effectiveInCover = false`. The cover
save die would not be prepended, and the defender would roll only 2 dice (after Piercing):

```
  Saturate active: cover save suppressed. Defender rolls 2 dice only.
```

---

### Shoot Summary Output

```
╔══════════════════════════════════════════════════════════════════╗
║                     🎯  Shoot Summary  🎯                          ║
╠══════════════════════════════════════════════════════════════════╣
║  Sniper → Warrior                                                ║
║  Weapon: Bolt sniper rifle (mortis)                              ║
║                                                                  ║
║  Attacker rolled: 6  5  4  2                                     ║
║  Pool: CRIT  HIT  HIT  HIT                                       ║
║  Piercing 1: 1 defence die removed                               ║
║  Defender rolled: 4  2   (+1 cover)                              ║
║  Saves: 2  (1 rolled + 1 cover)                                  ║
║                                                                  ║
║  Blocked: 1 CRIT (via 2 normals), 0 HITs                        ║
║  Unblocked: 0 CRITs, 3 HITs                                      ║
║  Devastating 3: crit damage would have been 3 per crit           ║
║  Damage dealt: 9 normal damage                                   ║
║                                                                  ║
║  Warrior: 14W → 5W                                               ║
╚══════════════════════════════════════════════════════════════════╝
```

---

## 7. xUnit Tests

Test conventions: `MethodName_Scenario_ExpectedResult`. Framework: xUnit + FluentAssertions.
See `spec.md → Testing` for shared infrastructure (`TestDbBuilder`, etc.).

```csharp
public class SpecialRuleParserTests
{
    [Fact]
    public void Parse_BrutalSevereShockPoison_ReturnsCorrectList()
    {
        var rules = SpecialRuleParser.Parse("Brutal, Severe, Shock, Poison");

        rules.Should().HaveCount(4);
        rules[0].Kind.Should().Be(SpecialRuleKind.Brutal);
        rules[0].Parameter.Should().BeNull();
        rules[1].Kind.Should().Be(SpecialRuleKind.Severe);
        rules[2].Kind.Should().Be(SpecialRuleKind.Shock);
        rules[3].Kind.Should().Be(SpecialRuleKind.Unknown);
        rules[3].RawText.Should().Be("Poison");
    }

    [Fact]
    public void Parse_DevastatingHeavyPiercing_ParametersCorrect()
    {
        var rules = SpecialRuleParser.Parse("Devastating 3, Heavy (Dash only), Piercing 1, Silent");

        rules.Should().HaveCount(4);
        rules[0].Kind.Should().Be(SpecialRuleKind.Devastating);
        rules[0].Parameter.Should().Be(3);
        rules[1].Kind.Should().Be(SpecialRuleKind.HeavyRestricted);
        rules[1].RawText.Should().Be("Dash only");
        rules[2].Kind.Should().Be(SpecialRuleKind.Piercing);
        rules[2].Parameter.Should().Be(1);
        rules[3].Kind.Should().Be(SpecialRuleKind.Silent);
    }

    [Fact]
    public void Parse_EmptyString_ReturnsEmptyList()
    {
        SpecialRuleParser.Parse("").Should().BeEmpty();
        SpecialRuleParser.Parse(null).Should().BeEmpty();
    }
}

public class CombatResolutionServiceTests
{
    private readonly CombatResolutionService _sut = new();

    [Theory]
    [InlineData(5, 3, 5, DieResult.Crit)]   // Lethal 5+: roll 5 → CRIT
    [InlineData(4, 3, 5, DieResult.Hit)]    // Lethal 5+: roll 4 → HIT (>= threshold 3)
    [InlineData(6, 3, 5, DieResult.Crit)]   // roll 6 → always CRIT regardless of Lethal
    [InlineData(2, 3, 5, DieResult.Miss)]   // below threshold → MISS
    public void CalculateDie_LethalThreshold_RollAtLethal_ReturnsCrit(
        int roll, int hitThreshold, int critThreshold, DieResult expected)
    {
        _sut.CalculateDie(roll, hitThreshold, critThreshold).Should().Be(expected);
    }

    [Fact]
    public void ResolveShoot_Piercing1_RemovesOneDefenceDie()
    {
        // 4 attack dice all hit; 3 defence dice (save 3+); Piercing 1 → defender rolls 2
        var ctx = new ShootContext(
            AttackDice:  new[] { 5, 4, 4, 3 },
            DefenceDice: new[] { 5, 4, 2 },           // 3 dice, but Piercing removes 1
            InCover:     false,
            HitThreshold:   3,
            SaveThreshold:  3,
            NormalDmg:   3,
            CritDmg:     4,
            WeaponRules: SpecialRuleParser.Parse("Piercing 1")
        );

        var result = _sut.ResolveShoot(ctx);

        // Only 2 defence dice remain (5→save, 4→save); saves = 2; unblocked normals = 2
        result.DefenderSaves.Should().Be(2);
        result.NormalDamageDealt.Should().Be(2 * 3);  // 2 unblocked normals × NormalDmg 3
    }

    [Fact]
    public void ResolveShoot_Devastating3_CritDamageIs3_RegarldessOfBaseCritDmg()
    {
        // Base CritDmg = 5; Devastating 3 should override to 3
        var ctx = new ShootContext(
            AttackDice:  new[] { 6, 6 },              // 2 crits
            DefenceDice: Array.Empty<int>(),
            InCover:     false,
            HitThreshold:   3,
            SaveThreshold:  3,
            NormalDmg:   3,
            CritDmg:     5,                           // base CritDmg = 5
            WeaponRules: SpecialRuleParser.Parse("Devastating 3")
        );

        var result = _sut.ResolveShoot(ctx);

        result.CritDamageDealt.Should().Be(2 * 3);    // 2 crits × Devastating 3 = 6, NOT 2 × 5
    }

    [Fact]
    public void ResolveShoot_Saturate_CoverSaveNotApplied()
    {
        // Without Saturate: inCover=true adds 1 save die. With Saturate: cover save suppressed.
        var ctxWithout = new ShootContext(
            AttackDice:  new[] { 4 },                 // 1 normal hit
            DefenceDice: Array.Empty<int>(),
            InCover:     true,                        // would normally add cover save
            HitThreshold:   3, SaveThreshold: 3,
            NormalDmg: 3, CritDmg: 4,
            WeaponRules: Array.Empty<WeaponSpecialRule>());

        var ctxSaturate = ctxWithout with
        {
            WeaponRules = SpecialRuleParser.Parse("Saturate")
        };

        var withoutResult = _sut.ResolveShoot(ctxWithout);
        var saturateResult = _sut.ResolveShoot(ctxSaturate);

        withoutResult.DefenderSaves.Should().Be(1);    // cover save retained
        saturateResult.DefenderSaves.Should().Be(0);   // Saturate suppresses cover
        saturateResult.NormalDamageDealt.Should().Be(3); // 1 unblocked normal × 3
    }
}

public class FightResolutionServiceTests
{
    private readonly FightResolutionService _sut = new();

    [Fact]
    public void ApplyShock_FirstCritStrike_DiscardsDefenderWorstDie()
    {
        // Grenadier holds G1:5, G2:4, G3:3 (raw values). Shock discards lowest → G3.
        var grenadierPool = new FightDicePool(DieOwner.Defender, new[]
        {
            new FightDie(Id: 1, RolledValue: 5, Result: DieResult.Hit),
            new FightDie(Id: 2, RolledValue: 4, Result: DieResult.Hit),
            new FightDie(Id: 3, RolledValue: 3, Result: DieResult.Hit),
        });

        var updated = _sut.ApplyShock(grenadierPool);

        updated.Remaining.Should().HaveCount(2);
        updated.Remaining.Should().NotContain(d => d.Id == 3);   // worst die (rolled 3) discarded
        updated.Cancelled.Should().ContainSingle(d => d.Id == 3);
    }

    [Fact]
    public void GetAvailableActions_BrutalWeapon_DefenderNormalsCannotBlock()
    {
        // Defender holds HIT dice only. Brutal active → no Block entries generated.
        var defenderPool = new FightDicePool(DieOwner.Defender, new[]
        {
            new FightDie(Id: 1, RolledValue: 5, Result: DieResult.Hit),
            new FightDie(Id: 2, RolledValue: 4, Result: DieResult.Hit),
        });
        var attackerPool = new FightDicePool(DieOwner.Attacker, new[]
        {
            new FightDie(Id: 1, RolledValue: 4, Result: DieResult.Hit),
        });

        var actions = _sut.GetAvailableActions(defenderPool, attackerPool, brutalWeapon: true);

        actions.Should().NotContain(a => a.Type == FightActionType.Block);
        actions.Should().OnlyContain(a => a.Type == FightActionType.Strike);
    }

    [Fact]
    public void ApplyRending_CritExists_NormalConvertedToCrit()
    {
        // Pool has 1 CRIT and 2 HITs. Rending should convert first HIT to CRIT.
        var pool = new FightDicePool(DieOwner.Attacker, new[]
        {
            new FightDie(Id: 1, RolledValue: 6, Result: DieResult.Crit),
            new FightDie(Id: 2, RolledValue: 4, Result: DieResult.Hit),
            new FightDie(Id: 3, RolledValue: 3, Result: DieResult.Hit),
        });

        var result = _sut.ApplyRending(pool);

        result.Crits.Should().HaveCount(2);  // original crit + converted
        result.Hits.Should().HaveCount(1);   // one HIT remains
    }

    [Fact]
    public void ApplySevere_ZeroCrits_NormalConvertedToCrit()
    {
        // Pool has 3 HITs, 0 CRITs. Severe converts first HIT to CRIT.
        var pool = new FightDicePool(DieOwner.Attacker, new[]
        {
            new FightDie(Id: 1, RolledValue: 4, Result: DieResult.Hit),
            new FightDie(Id: 2, RolledValue: 3, Result: DieResult.Hit),
            new FightDie(Id: 3, RolledValue: 3, Result: DieResult.Hit),
        });

        var result = _sut.ApplySevere(pool);

        result.Crits.Should().HaveCount(1);  // F1 converted
        result.Hits.Should().HaveCount(2);   // F2 and F3 remain as HITs
    }

    [Fact]
    public void ApplySevere_HasCrit_NothingConverted()
    {
        // Severe only fires when zero crits. If 1 crit exists, pool is unchanged.
        var pool = new FightDicePool(DieOwner.Attacker, new[]
        {
            new FightDie(Id: 1, RolledValue: 6, Result: DieResult.Crit),
            new FightDie(Id: 2, RolledValue: 3, Result: DieResult.Hit),
        });

        var result = _sut.ApplySevere(pool);

        result.Crits.Should().HaveCount(1);
        result.Hits.Should().HaveCount(1);  // no change
    }
}
```

---

## 8. Spectre.Console Components (Additions)

| UI Element | Component | Notes |
|---|---|---|
| Balanced re-roll prompt | `SelectionPrompt<RerollOption>` | One die + `[Skip]` |
| Ceaseless re-roll prompt | `SelectionPrompt<CeaselessOption>` | Distinct values + `[Skip]` |
| Relentless re-roll prompt | `MultiSelectionPrompt<RerollOption>` | Multi-select + `[Skip All]` |
| CP Re-roll prompt | `SelectionPrompt<string>` → then `SelectionPrompt<RerollTarget>` | Separated: confirm spend, then choose die |
| Weapon rule annotations | `Markup` inline in weapon selection list | `[dim]Brutal, Severe, Shock[/]` |
| Severe triggered | `Markup` | `[yellow]⚡ Severe — converting first HIT → CRIT[/]` |
| Shock triggered | `Markup` | `[yellow]⚡ Shock! Defender's worst die discarded[/]` |
| Brutal warning (no blocks) | `Markup` | `[yellow]⚠ Brutal active — normal dice cannot Block[/]` |
| Piercing applied | `Markup` | `[dim]Piercing 1: 1 defence die removed[/]` |
| Devastating applied | `Markup` | `[dim]Devastating 3: each crit deals 3 damage[/]` |
| Stun applied | `Markup` | `[yellow]⚡ Stun — {operative} loses 1APL until next activation[/]` |

---

## 9. Persistence Notes

### WeaponRules on the Action Row

The `Action` domain model does not need a dedicated column for parsed rules — rules are derivable
from `WeaponId` at any time. However, the `ShootContext` (and analogous fight context) must be
constructed with parsed rules each time resolution is needed. The roster import pipeline should parse
and store rules at import time:

```csharp
// In RosterImportService:
weapon.ParsedRules = SpecialRuleParser.Parse(weapon.SpecialRulesRaw);
```

`WeaponSpecialRule` objects are not persisted to SQLite — they are always derived from the raw
`specialRules` string, which IS stored in the `weapons` table.

### GameOperativeState Extensions

Two new fields for Stun and Limited tracking:

```
GameOperativeState
  + StunnedUntilEndOfNextActivation (bool)
  + WeaponUsesRemaining (JSON blob: { "weaponId": remainingUses, ... })
```

SQLite schema additions:

```sql
ALTER TABLE game_operative_states
  ADD COLUMN stunned_next_activation INTEGER NOT NULL DEFAULT 0;

ALTER TABLE game_operative_states
  ADD COLUMN weapon_uses_remaining TEXT;  -- JSON: {"guid": int}
```

---

## Open Questions

1. **Seek / Seek Light terrain type**: The app has no terrain model. Should the player be asked
   "Is target in light cover?" separately from "Is target in cover?" to enable Seek Light
   enforcement? Or remain display-only until a terrain model is added?

2. **Blast / Torrent multi-target**: The current Action schema stores a single `TargetOperativeId`.
   For Blast/Torrent, multiple targets need separate `Action` rows or a child table. Should the
   schema be extended now (even if the targeting flow is not enforced)?

3. **Hot self-damage**: Hot requires the shooter to roll a D6 post-shoot and potentially take damage.
   This creates a new `Action` type on the shooter's own activation. How is this best represented in
   the schema?

4. **Stun and APL in the active activation**: Stun removes 1APL "until end of their next
   activation". If the target has already activated this TP, the reduction applies next TP. If not
   yet activated, it applies immediately to any remaining actions this TP. The orchestrator needs to
   check `StunnedUntilEndOfNextActivation` when computing available AP for an activation.

5. **Accurate x with Relentless**: Accurate synthetic dice are pre-rolled successes — they should
   not be eligible for Relentless re-rolls (they were never rolled). The re-roll prompt must exclude
   them from the selection list.

6. **DevastatingAoE (D″ Devastating x)**: No weapon in the current starter set rosters uses this
   variant. Implementation deferred; parser stub exists (`DevastatingAoE` enum value).
