# Spike: Fight Action UI Flow

**Status**: Draft  
**Author**: Spike  
**Date**: 2025-07  
**Area**: Kill Team Game Tracking — Fight Action

---

## Introduction

The Fight action is the most complex interactive flow in the Kill Team CLI app. Unlike Shoot or Move, a
Fight involves two operatives, two independent dice pools, and an alternating resolution loop that can
branch in several ways. This spike defines the service-layer design, the Spectre.Console interaction
transcript, the state machine governing turns, edge-case handling, and the persistence model so that a
developer can implement the feature directly from this document.

The app is a .NET 10 console application using Spectre.Console for all interactive prompts. Game state
is tracked in-session and persisted to an `Action` record at the end of each action.

**Worked example operatives** (used throughout this document):

> **Rule note demonstrated in this example**: normal dice cannot block critical successes. Only a
> critical die can cancel a critical die.

| Operative | Player | Side | W | Weapon | ATK | Hit | NormalDmg | CritDmg |
|---|---|---|---|---|---|---|---|---|
| Assault Intercessor Grenadier | Michael | Attacker | 14 | Chainsword | 5 | 3+ | 4 | 5 |
| Plague Marine Champion | Solomon | Defender | 15 | Plague sword | 5 | 3+ | 4 | 5 |

---

## Rules Recap

> **Official rules text (verbatim):**
> Starting with the attacker, players alternate resolving their successful unblocked attack dice
> (or all remaining if their opponent has none). To resolve a dice, they must strike or block:
> - **Strike:** Inflict damage on the enemy operative. A normal success inflicts damage equal to
>   the weapon's Normal Dmg stat (first value of Dmg stat). A critical success inflicts damage
>   equal to the weapon's Critical Dmg stat (second value of Dmg stat).
> - **Block:** Allocate this dice to block one of your opponent's unresolved successes. A normal
>   success can block a normal success. A critical success can block a normal or critical success.

1. Attacker selects an enemy operative within the active operative's control range to fight against.
   That enemy operative will retaliate.
2. Both players select one of their operative's melee weapons.
3. Both players roll attack dice — a number of D6 equal to their selected weapon's Atk stat
   respectively. Each result that equals or beats their weapon's Hit stat is retained as a success.
   Each that doesn't is discarded as a fail. Results of 6 are always successes and are critical; all
   other successes are normal; results of 1 are always a fail.
4. Starting with the attacker, players alternate resolving their successful unblocked attack dice (or
   all remaining if their opponent has none). To resolve a die, they must Strike or Block:
   - **Strike**: Inflict damage on the enemy operative. A normal success inflicts damage equal to the
     weapon's Normal Dmg stat (first value of Dmg stat). A critical success inflicts damage equal to
     the weapon's Critical Dmg stat (second value of Dmg stat).
   - **Block**: Allocate this die to block one of your opponent's unresolved successes. A normal
     success can block a normal success. A critical success can block a normal or critical success.

**Block rules summary:**

| Spend | Can cancel |
|---|---|
| 1 normal die → Block | 1 opponent **normal** die only (cannot block a crit) |
| 1 critical die → Block | 1 opponent die (**normal or critical**) |

Die labelling convention: attacker dice are `A1, A2, A3…`; defender dice are `D1, D2, D3…`.

Damage is applied **immediately** when a Strike is resolved. When one pool is exhausted, the other
player **continues their turns one die at a time** — they can only Strike (no Block option since there
are no opponent dice to cancel). The fight does NOT end when one pool runs out; it ends only when
**both** pools are exhausted **or** either operative is incapacitated. A player **cannot pass** their
turn while they hold dice.

---

## Service Design

### Domain Value Types

```csharp
public enum DieResult { Miss, Hit, Crit }
public enum DieOwner { Attacker, Defender }

/// Represents a single die in the active pool.
public record FightDie(
    int Id,           // Sequential label within a player's pool: attacker = A1, A2…; defender = D1, D2…
    int RolledValue,  // Raw D6 value (1–6)
    DieResult Result  // Hit | Crit (Misses never enter the pool)
);
```

The `Id` field is used as the numeric part of the label. For display, the orchestrator prefixes `A`
(attacker) or `D` (defender) based on the owning pool, giving labels like `A1`, `D2`, etc. The prefix
is not stored on the record — it is derived from the pool's `Owner` property at render time.

### FightDicePool

```csharp
public class FightDicePool
{
    public DieOwner Owner { get; }
    public IReadOnlyList<FightDie> Remaining { get; }   // dice still in pool
    public IReadOnlyList<FightDie> Spent { get; }       // dice used (Strike or Block)
    public IReadOnlyList<FightDie> Cancelled { get; }   // dice cancelled by opponent's block

    public FightDicePool(DieOwner owner, IEnumerable<FightDie> dice);

    public bool IsEmpty => !Remaining.Any();
    public IEnumerable<FightDie> Hits  => Remaining.Where(d => d.Result == DieResult.Hit);
    public IEnumerable<FightDie> Crits => Remaining.Where(d => d.Result == DieResult.Crit);

    // Spend one die (Strike or Block)
    public FightDicePool SpendDie(int dieId);

    // Remove a die from this pool (called on the OPPONENT pool when a Block is resolved)
    public FightDicePool CancelDie(int dieId);
}
```

### FightResolutionService

Stateless; contains all fight-rule logic. No I/O.

```csharp
public class FightResolutionService
{
    // Classify a single raw D6 roll against a hit threshold.
    // Returns Crit (roll == 6), Hit (roll >= hitThreshold), or Miss.
    public DieResult CalculateDie(int roll, int hitThreshold);

    // Build a FightDicePool from a sequence of raw rolls.
    // Assigns sequential IDs (1, 2, 3…); discards Misses before entering pool.
    public FightDicePool CalculateDice(IEnumerable<int> rolls, int hitThreshold, DieOwner owner);

    // Roll ATK dice automatically and return results (used when player chooses "Roll for me").
    public FightDicePool AutoRoll(int atkDice, int hitThreshold, DieOwner owner);

    // Returns available actions for the active player given both pools.
    //   Strike: one entry per die in active pool (always available when pool non-empty).
    //   Block(normal→normal): one entry per (active normal die, opponent normal die) pairing.
    //   Block(crit→any): one entry per (active crit die, opponent die) pairing.
    //   Normal dice are NEVER offered a Block pairing against opponent crit dice.
    public IReadOnlyList<FightActionChoice> GetAvailableActions(
        FightDicePool activePool, FightDicePool opponentPool);

    // Apply a Strike: deducts die from activePool, returns damage dealt.
    // Caller is responsible for updating operative wounds.
    public int ApplyStrike(FightDie die, int normalDmg, int critDmg);

    // Apply a Block: spends spendDie from activePool; cancels targetDie in opponentPool.
    // Precondition: if spendDie.Result == Hit then targetDie.Result must == Hit.
    //               if spendDie.Result == Crit then targetDie.Result may be Hit or Crit.
    public (FightDicePool UpdatedActivePool, FightDicePool UpdatedOpponentPool) ApplySingleBlock(
        FightDicePool activePool,
        FightDicePool opponentPool,
        FightDie spendDie,
        FightDie targetDie);

    // True when fight is over: both pools empty, or one operative incapacitated.
    public bool IsFightOver(
        FightDicePool attackerPool, FightDicePool defenderPool,
        GameOperativeState attackerState, GameOperativeState defenderState);
}
```

### FightSessionOrchestrator

Stateful; drives the UI loop. Owns the Spectre.Console interaction. Injected with
`FightResolutionService` and `IAnsiConsole`.

```csharp
public class FightSessionOrchestrator
{
    public FightSessionOrchestrator(
        FightResolutionService resolutionService,
        IAnsiConsole console);

    // Entry point — called by the Fight command handler.
    // Returns the completed FightSessionResult for persistence.
    public FightSessionResult Run(
        Operative attacker, GameOperativeState attackerState,
        Operative defender, GameOperativeState defenderState);

    // --- Private orchestration methods ---
    private (Weapon attackerWeapon, Weapon defenderWeapon) PromptWeaponSelection(
        Operative attacker, Operative defender);
    private FightDicePool PromptDiceEntry(Operative operative, Weapon weapon, DieOwner owner);
    private void DisplayDicePools(
        FightDicePool attackerPool, FightDicePool defenderPool,
        GameOperativeState attackerState, GameOperativeState defenderState);
    private FightTurnResult RunAttackerTurn(
        ref FightDicePool attackerPool, ref FightDicePool defenderPool,
        Weapon attackerWeapon, GameOperativeState defenderState);
    private FightTurnResult RunDefenderTurn(
        ref FightDicePool defenderPool, ref FightDicePool attackerPool,
        Weapon defenderWeapon, GameOperativeState attackerState);
    private void DisplayFightSummary(FightSessionResult result);
}
```

### Supporting Types

```csharp
public enum FightActionType { Strike, Block }

public record FightActionChoice(
    FightActionType Type,
    FightDie SpendDie,       // die from the active pool to spend
    FightDie? TargetDie,     // opponent die to cancel (null for Strike)
    string Label             // human-readable text shown in SelectionPrompt
);

public record FightTurnResult(
    DieOwner ActivePlayer,
    FightActionType ActionTaken,
    int DieSpent,             // ID of the spent die (from active pool)
    int? TargetDieCancelled,  // ID of cancelled die (from opponent pool), null for Strike
    int DamageDealt,
    bool CausedIncapacitation
);

public record FightSessionResult(
    int AttackerWeaponId,
    int DefenderWeaponId,
    IReadOnlyList<int> AttackerRolledDice,    // raw values including misses
    IReadOnlyList<int> DefenderRolledDice,
    int AttackerNormalHits,
    int AttackerCritHits,
    int DefenderNormalHits,
    int DefenderCritHits,
    int AttackerNormalDamageDealt,
    int AttackerCritDamageDealt,
    int DefenderNormalDamageDealt,
    int DefenderCritDamageDealt,
    bool AttackerCausedIncapacitation,
    bool DefenderCausedIncapacitation,
    IReadOnlyList<FightTurnResult> TurnLog,
    string? NarrativeNote
);
```

---

## State Machine

### States

```
WeaponSelection
    │
    ▼
DiceEntry
    │
    ▼
AttackerTurn ◄──────────────────────────────┐
    │                                        │
    │  [attacker pool NOT empty]             │
    │──────────────────────────────────────► DefenderTurn
    │                                        │
    │  [attacker pool empty,                 │  [defender pool empty,
    │   defender pool non-empty]             │   attacker pool non-empty]
    ▼                                        ▼
DefenderStrikesFreely               AttackerStrikesFreely
    (defender turns, Strike-only;       (attacker turns, Strike-only;
     no Block options since attacker     no Block options since defender
     has no dice to cancel)              has no dice to cancel)
    │                                        │
    └──────────┐                   ┌─────────┘
               ▼                   ▼
          [both pools empty OR either operative incapacitated]
                            │
                            ▼
                      FightComplete
```

### State Descriptions

| State | Entry Condition | Actions Available | Exit Condition |
|---|---|---|---|
| `WeaponSelection` | Fight action starts | `SelectionPrompt` for attacker, then defender | Both weapons selected |
| `DiceEntry` | Weapons selected | Roll or enter dice for attacker, then defender | Both pools populated |
| `AttackerTurn` | Attacker pool non-empty | Strike, Block variants | Attacker spends die(s); transitions to DefenderTurn or DefenderStrikesFreely |
| `DefenderTurn` | Defender pool non-empty | Strike, Block variants | Defender spends die(s); transitions to AttackerTurn or AttackerStrikesFreely |
| `DefenderStrikesFreely` | Attacker pool empty, defender pool non-empty | **Strike only** (one die per turn — no Block as there are no attacker dice to target) | Defender pool empty or defender causes incapacitation |
| `AttackerStrikesFreely` | Defender pool empty, attacker pool non-empty | **Strike only** (one die per turn — no Block as there are no defender dice to target) | Attacker pool empty or attacker causes incapacitation |
| `FightComplete` | Both pools empty OR any operative incapacitated | Display summary, persist | — |

### Transition Guards

- **AttackerTurn → DefenderTurn**: after attacker spends a die, if defender pool is non-empty and neither operative is incapacitated.
- **AttackerTurn → AttackerStrikesFreely**: if defender pool is already empty at the start of attacker's turn (defender was wiped or all blocked) — attacker continues, Strike-only.
- **DefenderTurn → AttackerTurn**: after defender spends a die, if attacker pool is non-empty and neither operative is incapacitated.
- **DefenderTurn → DefenderStrikesFreely**: if attacker pool is already empty at the start of defender's turn — defender continues, Strike-only.
- **Any → FightComplete**: checked after every die resolution — triggered when `IsFightOver()` returns `true` (both pools empty, or operative wounds ≤ 0).

> **Key rule**: the fight is ALWAYS die-by-die until both pools are exhausted or an operative is incapacitated. There is no batch auto-resolution; each remaining die in `StrikeFreely` states still triggers a prompt so damage is shown step-by-step.

---

## CLI Interaction Transcript

**Scenario**: Michael's Assault Intercessor Grenadier (attacker) fights Solomon's Plague Marine
Champion (defender).

- Grenadier: 14W, Chainsword (ATK 5, Hit 3+, NormalDmg 4, CritDmg 5)
- Champion:  15W, Plague sword (ATK 5, Hit 3+, NormalDmg 4, CritDmg 5)

---

### Phase 1 — Weapon Selection

```
╔══════════════════════════════════════════════════════════╗
║                  ⚔  FIGHT ACTION  ⚔                      ║
║      Assault Intercessor Grenadier  vs  Plague Marine Champion ║
╚══════════════════════════════════════════════════════════╝

[Attacker] Select Grenadier's melee weapon:
  > Chainsword  (ATK 5 | Hit 3+ | DMG 4/5)

[Defender] Select Champion's melee weapon:
  > Plague sword  (ATK 5 | Hit 3+ | DMG 4/5)
```

---

### Phase 2 — Dice Entry

```
──────────────────────────────────────────────────────────
  Grenadier rolls 5 dice (Chainsword, Hit 3+)
──────────────────────────────────────────────────────────

How would you like to enter Grenadier's dice?
    🎲 Roll for me
  > ✏  Enter manually

Enter Grenadier's dice results (space or comma separated, e.g. 6 4 2):
> 6 6 4 3 1

  A1: 6  → CRIT ✓
  A2: 6  → CRIT ✓
  A3: 4  → HIT  ✓
  A4: 3  → HIT  ✓
  A5: 1  → MISS ✗  (discarded)

Grenadier's pool: 4 dice  (2 CRITs, 2 HITs)

──────────────────────────────────────────────────────────
  Champion rolls 5 dice (Plague sword, Hit 3+)
──────────────────────────────────────────────────────────

How would you like to enter Champion's dice?
    🎲 Roll for me
  > ✏  Enter manually

Enter Champion's dice results (space or comma separated, e.g. 6 4 2):
> 5 4 4 2 1

  D1: 5  → HIT  ✓
  D2: 4  → HIT  ✓
  D3: 4  → HIT  ✓
  D4: 2  → MISS ✗  (discarded)
  D5: 1  → MISS ✗  (discarded)

Champion's pool: 3 dice  (0 CRITs, 3 HITs)
```

---

### Phase 3 — Dice Pool Display

```
┌──────────────────────────────────────────────────────────────┐
│                      ⚔  Dice Pools  ⚔                        │
├─────────────────────────────┬────────────────────────────────┤
│  Grenadier (Attacker)       │  Champion (Defender)           │
│  Wounds: 14                 │  Wounds: 15                    │
├─────────────────────────────┼────────────────────────────────┤
│  [A1: CRIT  rolled 6]       │  [D1: HIT  rolled 5]           │
│  [A2: CRIT  rolled 6]       │  [D2: HIT  rolled 4]           │
│  [A3: HIT   rolled 4]       │  [D3: HIT  rolled 4]           │
│  [A4: HIT   rolled 3]       │                                │
└─────────────────────────────┴────────────────────────────────┘
```

Die format: `Xn: RESULT  rolled V` — `X` is `A` (attacker) or `D` (defender); `n` is a sequential
number; `RESULT` is `CRIT` or `HIT` (misses never enter the pool); `rolled V` is the raw D6 face
value. The face value is shown so players can verify against their physical dice, but it does **not**
determine damage — damage is always from the weapon's NormalDmg or CritDmg stat.

---

### Phase 4 — Alternating Resolution

> **UX design principle**: Die resolution is split into two steps — first select which die to
> use, then choose the action for it (Strike or Block). This mirrors physical play (pick up a die,
> decide what to do) and keeps the option count manageable. Step 1 shows at most N dice; Step 2
> shows at most 1 Strike + M eligible block targets.

#### Turn 1 — Michael's turn (Grenadier, Attacker)

**Step 1 — Select a die:**

```
─── Michael's turn (Grenadier) ─────────────────────────────────
Your dice:  [A1: CRIT [rolled 6]]  [A2: CRIT [rolled 6]]  [A3: HIT [rolled 4]]  [A4: HIT [rolled 3]]
Solomon's:  [D1: HIT [rolled 5]]   [D2: HIT [rolled 4]]   [D3: HIT [rolled 4]]

Select a die to resolve:
  > A1: CRIT [rolled 6]
    A2: CRIT [rolled 6]
    A3: HIT [rolled 4]
    A4: HIT [rolled 3]
```

**Step 2 — Choose action for A1: CRIT [rolled 6]:**

```
A1: CRIT [rolled 6] selected.

  ⚔  1. STRIKE → 5 crit damage to Champion
  🛡  2. BLOCK → cancel a die (crit blocks any):
         D1: HIT [rolled 5]
         D2: HIT [rolled 4]
         D3: HIT [rolled 4]
> 1

  ⚔  Grenadier strikes Champion for 5 CRIT damage!
  Champion: 15 → 10W.
```

Pool update:

```
┌─────────────────────────────┬────────────────────────────────┐
│  Grenadier (Attacker)       │  Champion (Defender)           │
│  Wounds: 14                 │  Wounds: 10                    │
├─────────────────────────────┼────────────────────────────────┤
│  ~~[A1: CRIT  rolled 6]~~ ✓       │  [D1: HIT  rolled 5]                 │
│  [A2: CRIT  rolled 6]             │  [D2: HIT  rolled 4]                 │
│  [A3: HIT   rolled 4]             │  [D3: HIT  rolled 4]                 │
│  [A4: HIT   rolled 3]             │                                │
└─────────────────────────────┴────────────────────────────────┘
```

---

#### Turn 2 — Solomon's turn (Champion, Defender) ← KEY MOMENT: normal cannot block crit

Solomon holds three normal dice. Michael still holds `A2: CRIT [rolled 6]`, `A3: HIT [rolled 4]`, and `A4: HIT [rolled 3]`.
Because Solomon's dice are all normal (HIT), `A2: CRIT` does not appear as a Block target. Only a
critical die can block a critical die. This is surfaced in Step 2 when the player selects a die and
the Block options only list opponent normals:

**Step 1 — Select a die:**

```
─── Solomon's turn (Champion) ───────────────────────────────────
Your dice:  [D1: HIT [rolled 5]]  [D2: HIT [rolled 4]]  [D3: HIT [rolled 4]]
Michael's:  [A2: CRIT [rolled 6]]  [A3: HIT [rolled 4]]  [A4: HIT [rolled 3]]

Select a die to resolve:
  > D1: HIT [rolled 5]
    D2: HIT [rolled 4]
    D3: HIT [rolled 4]
```

**Step 2 — Choose action for D1: HIT [rolled 5]:**

```
D1: HIT [rolled 5] selected.

  ⚔  1. STRIKE → 4 normal damage to Grenadier
  🛡  2. BLOCK → cancel a die (normal blocks normal only):
         A3: HIT [rolled 4]
         A4: HIT [rolled 3]
  ⚠  Cannot block A2: CRIT — normal dice cannot block critical successes
> 2

  Select opponent die to cancel:
  > A3: HIT [rolled 4]
    A4: HIT [rolled 3]
> A3: HIT [rolled 4]

  ✓ D1(5) used to block A3(4). A3: HIT cancelled.
```

`A2: CRIT` does not appear in the Block sub-list at all. The `⚠ Cannot block A2: CRIT` line is
informational only — it teaches the rule rather than being a selectable entry.

Pool update after Turn 2:

```
┌─────────────────────────────┬────────────────────────────────┐
│  Grenadier (Attacker)       │  Champion (Defender)           │
│  Wounds: 14                 │  Wounds: 10                    │
├─────────────────────────────┼────────────────────────────────┤
│  ~~[A1: CRIT  rolled 6]~~ ✓       │  ~~[D1: HIT  rolled 5]~~ 🛡          │
│  [A2: CRIT  rolled 6]             │  [D2: HIT  rolled 4]                 │
│  ~~[A3: HIT   rolled 4]~~ 🛡       │  [D3: HIT  rolled 4]                 │
│  [A4: HIT   rolled 3]             │                                │
└─────────────────────────────┴────────────────────────────────┘
```

Strikethrough with ✓ = spent on Strike; strikethrough with 🛡 = cancelled by block.

---

#### Turn 3 — Michael's turn (Grenadier)

**Step 1:**

```
─── Michael's turn (Grenadier) ─────────────────────────────────
Your dice:  [A2: CRIT [rolled 6]]  [A4: HIT [rolled 3]]
Solomon's:  [D2: HIT [rolled 4]]   [D3: HIT [rolled 4]]

Select a die to resolve:
  > A2: CRIT [rolled 6]
    A4: HIT [rolled 3]
```

**Step 2 — A2: CRIT [rolled 6]:**

```
A2: CRIT [rolled 6] selected.

  ⚔  1. STRIKE → 5 crit damage to Champion
  🛡  2. BLOCK → cancel a die (crit blocks any):
         D2: HIT [rolled 4]
         D3: HIT [rolled 4]
> 1

  ⚔  Grenadier strikes Champion for 5 CRIT damage!
  Champion: 10 → 5W.
  ⚠  Champion is INJURED (5W < 7.5W threshold — below half starting wounds)!
```

*Shows: Strike with a critical die again; injured status triggered.*

---

#### Turn 4 — Solomon's turn (Champion)

**Step 1:**

```
─── Solomon's turn (Champion) ──────────────────────────────────
Your dice:  [D2: HIT [rolled 4]]  [D3: HIT [rolled 4]]
Michael's:  [A4: HIT [rolled 3]]

Select a die to resolve:
  > D2: HIT [rolled 4]
    D3: HIT [rolled 4]
```

**Step 2 — D2: HIT [rolled 4]:**

```
D2: HIT [rolled 4] selected.

  ⚔  1. STRIKE → 4 normal damage to Grenadier
  🛡  2. BLOCK → cancel a die (normal blocks normal only):
         A4: HIT [rolled 3]
> 1

  ⚔  Champion strikes Grenadier for 4 NORMAL damage!
  Grenadier: 14 → 10W.
```

*Shows: Strike with a normal die.*

---

#### Turn 5 — Michael's turn (Grenadier)

**Step 1:**

```
─── Michael's turn (Grenadier) ─────────────────────────────────
Your dice:  [A4: HIT [rolled 3]]
Solomon's:  [D3: HIT [rolled 4]]

Select a die to resolve:
  > A4: HIT [rolled 3]
```

**Step 2 — A4: HIT [rolled 3]:**

```
A4: HIT [rolled 3] selected.

  ⚔  1. STRIKE → 4 normal damage to Champion
  🛡  2. BLOCK → cancel a die (normal blocks normal only):
         D3: HIT [rolled 4]
> 1

  ⚔  Grenadier strikes Champion for 4 NORMAL damage!
  Champion: 5 → 1W.
```

*Shows: Strike freely — both pools still have dice but Michael chooses to Strike.*

Michael's pool is now empty: A1 struck, A2 struck, A3 cancelled by D1 block, A4 struck — all four
dice spent.

---

#### Turn 6 — Solomon's turn (Champion) — DefenderStrikesFreely

```
══ DefenderStrikesFreely ════════════════════════════════════════
  Grenadier has no dice remaining.
  Champion strikes freely — one die per turn.
════════════════════════════════════════════════════════════════

─── Solomon's turn (Champion) ──────────────────────────────────
Your dice:  [D3: HIT [rolled 4]]
Michael's dice: (empty)

Select die to Strike with:
  > [D3: HIT  rolled 4]  → 4 normal damage to Grenadier

  ⚔  Champion strikes Grenadier for 4 NORMAL damage!
  Grenadier: 10 → 6W.
```

*Shows: Strike-freely when opponent pool is empty — prompt still appears, Strike-only option.*

Both pools are now empty. `IsFightOver()` returns `true`.

**Final state**: Champion 1W (Injured), Grenadier 6W.

---

### Phase 5 — One Pool Exhausted (AttackerStrikesFreely / DefenderStrikesFreely)

When the opponent pool is empty, the active player enters a Strike-only loop. They still take
individual turns — one die per turn — with a prompt confirming the Strike. The prompt simply
has no Block options in the menu. This ensures every die is accounted for and damage is shown
step-by-step.

If the active player has multiple dice remaining, a die-selection prompt is shown:

```
══ DefenderStrikesFreely ════════════════════════════════════════
  Grenadier has no dice remaining.
  Champion strikes freely — one die per turn.
════════════════════════════════════════════════════════════════

Your dice: [D2: HIT [rolled 4]]  [D3: HIT [rolled 4]]

Select die to Strike with:
  > [D2: HIT  rolled 4]  → 4 normal damage
    [D3: HIT  rolled 4]  → 4 normal damage
```

If only one die remains, the prompt still appears — just with a single option:

```
Your dice: [D3: HIT [rolled 4]]

Select die to Strike with:
  > [D3: HIT  rolled 4]  → 4 normal damage
```

There is no auto-resolution; every Strike is confirmed turn-by-turn.

---

### Phase 6 — Fight Summary

```
╔════════════════════════════════════════════════════════════════╗
║                      ⚔  Fight Summary  ⚔                      ║
╠════════════════════════════════════════════════════════════════╣
║  Grenadier (Attacker)           │  Champion (Defender)        ║
║  Weapon: Chainsword             │  Weapon: Plague sword       ║
║  Rolled: 6 6 4 3 1              │  Rolled: 5 4 4 2 1          ║
║  Pool:   CRIT CRIT HIT HIT      │  Pool:   HIT HIT HIT        ║
║                                 │                             ║
║  Strikes dealt: 3               │  Strikes dealt: 2           ║
║  Damage dealt:  14              │  Damage dealt:  8           ║
║    Crit dmg:    10              │    Normal dmg:  8           ║
║    Normal dmg:  4               │    Crit dmg:    0           ║
║  Blocks made:   0               │  Blocks made:   1           ║
║                                 │                             ║
║  Grenadier: 14 → 6W             │  Champion: 15 → 1W          ║
║                                 │  ⚠ INJURED                  ║
╚════════════════════════════════════════════════════════════════╝

Add a narrative note? (leave blank to skip)
> Two crits from the Grenadier halved the Champion before he could retaliate — A3 blocked, but not enough.
```

---

## Edge Cases

### 1. Normal Die Cannot Block a Critical Success

A normal die can **only** block a normal die. When the active player selects a normal die in Step 1
and the opponent holds only critical dice, **no Block sub-list appears in Step 2** — only Strike is
offered. The informational `⚠` line reminds the player why.

Example: Solomon holds `D3: HIT` only; Michael holds `A2: CRIT` only.

**Step 1:**
```
─── Solomon's turn (Champion) ──────────────────────────────────
Your dice:  [D3: HIT [rolled 4]]
Michael's:  [A2: CRIT [rolled 6]]

Select a die to resolve:
  > D3: HIT [rolled 4]
```

**Step 2:**
```
D3: HIT [rolled 4] selected.

  ⚔  1. STRIKE → 4 normal damage to Grenadier
  ⚠  Cannot block A2: CRIT — normal dice cannot block critical successes
> 1
```

`GetAvailableActions()` generates zero Block entries; the `⚠` line is rendered by the orchestrator
when the selected die is normal and all opponent dice are crits.

### 2. Attacker Incapacitated by Defender's Strike Mid-Fight

The fight does **not** abort immediately upon incapacitation — the Strike die resolution that caused
it completes first, then `IsFightOver()` returns `true` and the loop exits. No further turns are
taken. The fight summary is displayed as normal.

Persistence: `Action.CausedIncapacitation = true` is set on the defender's action record.
`GameOperativeState.IsIncapacitated = true` is set on the Grenadier.

### 3. Player Has Only Crits and Wants to Block a Normal

A **crit die blocks any opponent die** — normal or crit. In Step 2, the Block sub-list shows all
opponent dice (including their normals and crits):

```
A1: CRIT [rolled 6] selected.

  ⚔  1. STRIKE → 5 crit damage
  🛡  2. BLOCK → cancel a die (crit blocks any):
         D1: HIT [rolled 5]
         D2: HIT [rolled 4]   ← normals listed because crit can block them
```

No special handling beyond ensuring `GetAvailableActions()` includes `Block` entries when
`activePool.Crits.Any() && !opponentPool.IsEmpty`.

### 4. Both Pools Exhausted Simultaneously

This can only happen if both players spend their last die on the same turn pair. After each die spend,
`IsFightOver()` is called. If both pools are empty at that point the fight ends with no further turns.
The summary reflects zero remaining dice on both sides and no incapacitation (unless wounds hit zero).

### 5. Attacker Has 0 Hits After Rolling

If all attacker dice are misses, the attacker's pool is empty from the start. The state machine skips
directly to `DefenderExhaustsAttacker` — the defender resolves all their dice as Strikes freely. The
attacker turn is bypassed entirely. This is checked during the transition from `DiceEntry` to the
first turn.

```
──────────────────────────────────────────────────────────
  Grenadier rolled no hits. Grenadier's pool is empty.
  Champion resolves all dice freely.
──────────────────────────────────────────────────────────
```

### 6. Defender Using a Ranged Weapon

The weapon-selection `SelectionPrompt` for the defender filters to melee weapons only
(`weapon.Type == WeaponType.Melee`). If the defender has no melee weapons configured, an error
panel is displayed and the Fight action is cancelled:

```
[red]Error:[/] Champion has no melee weapons. Cannot perform Fight action.
Press any key to return.
```

This scenario should ideally be caught before the Fight option is offered to the attacker, but the
defensive check here prevents a crash if data is inconsistent.

### 7. Operative Already Incapacitated Before Fight

The Fight command handler checks `GameOperativeState.IsIncapacitated` for both operatives before
entering `FightSessionOrchestrator.Run()`. If either is incapacitated, the action is rejected with
an explanation panel. This is a pre-condition guard, not an in-fight edge case.

---

## Spectre.Console Components

| UI Element | Component | Notes |
|---|---|---|
| Fight action header | `Panel` with `Rule` border title | `[bold yellow]⚔ FIGHT ACTION ⚔[/]` |
| Weapon selection | `SelectionPrompt<Weapon>` | Filtered to melee; uses `Title()` for context |
| Roll or Enter? | `SelectionPrompt<string>` | Two choices: auto-roll, manual entry |
| Manual dice entry | `TextPrompt<string>` | Validated via `Validator` callback (values 1–6, space/comma separated) |
| Dice pool table | `Table` | Two columns (Attacker / Defender); re-rendered after each turn |
| Strike/Block choice | `SelectionPrompt<FightActionChoice>` | `FightActionChoice.Label` is the display string |
| Damage dealt | `Markup` | `[red]⚔ … for N CRIT damage![/]` or `[red]… for N NORMAL damage![/]` |
| Block resolved | `Markup` | `[green]✓ Dx(v) used to block Ax(v). Ax: HIT/CRIT cancelled.[/]` |
| Injury notice | `Markup` | `[yellow]⚠ OPERATIVE INJURED[/]` (below half wounds) |
| Incapacitation | `Markup` + `Rule` | `[bold red]💀 OPERATIVE INCAPACITATED[/]` |
| Wound counter | `Markup` inline in table | Normal colour; ≤ half starting wounds: `[yellow]`; 0 wounds: `[bold red]` |
| Turn header | `Rule` | e.g. `─── Michael's turn (Grenadier) ───` |
| Fight summary | `Table` inside a `Panel` | Two-column layout (attacker left, defender right) |
| Narrative note | `TextPrompt<string>` | Optional; `AllowEmpty()` enabled |
| Section separators | `Rule` | Plain horizontal rule between phases |

### Colour Conventions

| Colour | Used For |
|---|---|
| `[yellow]` | CRIT dice; injury warnings |
| `[green]` | HIT dice; successful blocks |
| `[dim]` | Spent/cancelled dice shown in history |
| `[red]` | Damage dealt; critically low wounds |
| `[bold red]` | Incapacitation events |
| `[blue]` | Defender dice labels (D1, D2…) in pool table |

---

## Persistence Model

After `FightSessionOrchestrator.Run()` returns a `FightSessionResult`, the calling command handler
maps it to the `Action` record. The `Action` belongs to the **attacker's activation** — the defender's
dice are stored on the same row as secondary data.

```
Action {
    Type              = Fight
    WeaponId          = FightSessionResult.AttackerWeaponId
    TargetOperativeId = Champion.Id

    // Raw rolls including misses — serialised as JSON int arrays
    AttackerDice      = [6, 6, 4, 3, 1]
    DefenderDice      = [5, 4, 4, 2, 1]

    // Attacker's resolved pool
    NormalHits        = FightSessionResult.AttackerNormalHits    // 2
    CriticalHits      = FightSessionResult.AttackerCritHits      // 2

    // Damage attacker dealt
    NormalDamageDealt   = FightSessionResult.AttackerNormalDamageDealt   // 4
    CriticalDamageDealt = FightSessionResult.AttackerCritDamageDealt     // 10

    // Whether this action produced an incapacitation (either direction)
    CausedIncapacitation = FightSessionResult.AttackerCausedIncapacitation
                        || FightSessionResult.DefenderCausedIncapacitation

    NarrativeNote     = FightSessionResult.NarrativeNote
}
```

**Note on defender data**: The `Action` schema does not have first-class columns for the defender's
weapon, damage dealt by defender, or defender's hit counts. These are derivable from `DefenderDice`
plus the defender's weapon record. If richer defender tracking is required, the schema should be
extended with `DefenderWeaponId`, `DefenderNormalHits`, `DefenderCritHits`,
`DefenderNormalDamageDealt`, and `DefenderCritDamageDealt`.

The turn log (`FightSessionResult.TurnLog`) is **not** persisted to the `Action` row — it is
in-memory only and used for the summary display. If turn-by-turn replay is a future requirement, a
separate `ActionTurnEvent` child table would be needed.

### `GameOperativeState` Updates

Both operatives' states are updated in the game session state (not in the `Action` row):

```csharp
// Defender state (updated as attacker strikes)
defenderState.CurrentWounds  -= totalDamageFromAttacker;
defenderState.IsIncapacitated = defenderState.CurrentWounds <= 0;

// Attacker state (updated as defender strikes)
attackerState.CurrentWounds  -= totalDamageFromDefender;
attackerState.IsIncapacitated = attackerState.CurrentWounds <= 0;

// Record which action produced an incapacitation (for scoring/narrative)
attackerState.CausedIncapacitation = FightSessionResult.AttackerCausedIncapacitation;
```

---

## Testing

Test conventions, framework dependencies, and snapshot guidance are defined in the main spec:
wiki/specs/kill-team-game-tracking/spec.md — see the **Testing** section.

The fight test suite should be implemented in `KillTeamAgent.Tests` and cover the following cases:

### FightResolutionService Unit Tests

**CalculateDie / CalculateDice**

- `CalculateDie_Roll6_ReturnsCrit`
- `CalculateDie_RollEqualsThreshold_ReturnsHit`
- `CalculateDie_RollBelowThreshold_ReturnsMiss`
- `CalculateDie_Roll1_AlwaysMiss`
- `CalculateDice_BuildsPoolDiscardingMisses`

**ApplyStrike**

- `ApplyStrike_CritDie_ReturnsCritDmg`
- `ApplyStrike_NormalDie_ReturnsNormalDmg`

**ApplySingleBlock**

- `Block_NormalDie_CancelsOpponentNormal` — spend a HIT die to cancel an opponent HIT die; verify
  spendDie moves to `Spent`, targetDie moves to opponent `Cancelled`.
- `Block_NormalDie_CannotCancelOpponentCrit` — confirms this pairing is excluded from
  `GetAvailableActions`: active pool `[D1: HIT]`, opponent pool `[A2: CRIT]` → zero Block entries.
  This is a constraint enforced by the action generator, not `ApplySingleBlock` itself (which should
  be called only with valid pairings).
- `Block_CritDie_CancelsOpponentNormal` — spend a CRIT die to cancel an opponent HIT die; valid.
- `Block_CritDie_CancelsOpponentCrit` — spend a CRIT die to cancel an opponent CRIT die; valid.

**GetAvailableActions**

- `GetAvailableActions_StrikeAlwaysIncluded_WhenPoolNonEmpty`
- `GetAvailableActions_NormalDie_DoesNotOfferBlockAgainstCrit` — active pool: `[D1: HIT]`; opponent
  pool: `[A2: CRIT]`. Assert: zero Block entries returned. Normal dice must not be paired with
  opponent crits.
- `GetAvailableActions_CritDie_OffersBlockAgainstNormal` — active has CRIT, opponent has HIT →
  `Block` entry present.
- `GetAvailableActions_CritDie_OffersBlockAgainstCrit` — active has CRIT, opponent has CRIT →
  `Block` entry present.
- `GetAvailableActions_NormalDie_OffersBlockAgainstNormal` — active has HIT, opponent has HIT →
  `Block` entry present.
- `GetAvailableActions_EmptyOpponentPool_NoBlockEntries` — opponent pool empty → no Block entries.

**IsFightOver**

- `IsFightOver_BothPoolsEmpty_ReturnsTrue`
- `IsFightOver_OnePoolEmpty_ReturnsFalse`
- `IsFightOver_AttackerIncapacitated_ReturnsTrue`
- `IsFightOver_DefenderIncapacitated_ReturnsTrue`

### Snapshot Test — Grenadier vs Champion (6-turn worked example)

A Verify snapshot test drives `FightSessionOrchestrator` with the fixed input sequence:
- Attacker: Grenadier, 14W, Chainsword ATK 5 Hit 3+ NormalDmg 4 CritDmg 5
- Defender: Champion, 15W, Plague sword ATK 5 Hit 3+ NormalDmg 4 CritDmg 5
- Attacker dice: 6, 6, 4, 3, 1
- Defender dice: 5, 4, 4, 2, 1
- Turn decisions (in order):
  1. Michael: Strike A1
  2. Solomon: Block D1 → A3
  3. Michael: Strike A2
  4. Solomon: Strike D2
  5. Michael: Strike A4
  6. Solomon (DefenderStrikesFreely): Strike D3

The snapshot captures full console output. Verify the final state is Champion 1W (Injured),
Grenadier 6W.

### Incapacitation Scenario Test

A separate snapshot or assertion test where a single crit from the attacker reduces the defender to
0 wounds mid-fight (both pools still non-empty). Assert `IsFightOver()` returns `true` after that
turn and no further turns are taken.

---

## Open Questions

1. **Defender's weapon ID on Action row** — The current schema stores only `WeaponId` (the attacker's).
   Should `DefenderWeaponId` be added as a first-class column, or is it acceptable to reconstruct from
   `DefenderDice` + operative data? Adding it is low-cost and makes queries simpler.

2. **Turn log persistence** — Should `FightSessionResult.TurnLog` be persisted (e.g. as a JSON column
   or child table) to support post-game replay or statistics ("how often does the first attacker crit
   win the fight")? This would require a schema change.

3. **Autopilot mode for "exhaust remaining pool"** — When a pool is empty and the remaining player must
   Strike all remaining dice, should the app auto-resolve all Strikes without prompting (just animate
   the damage), or should the player still confirm each Strike? The transcript above shows prompting
   only for die choice when multiple dice remain. This is a UX preference.

4. **Narrative note prompt** — Should the narrative note be prompted only if
   `CausedIncapacitation == true` (to encourage flavour text on dramatic kills), or always? Always
   feels consistent; conditional feels noisier for routine fights.

5. **Wound floor** — Can wounds go negative (overkill) or are they floored at 0? The domain model
   should clarify. The transcript assumes floor at 0 for display purposes but the raw damage dealt is
   recorded accurately.

6. **Multiple melee weapons with identical names** — If an operative has two weapons with the same name
   (e.g. two profiles of the same weapon), the `SelectionPrompt` needs a disambiguation strategy (show
   weapon ID, show stats inline). The transcript already shows stats inline — confirm this is the
   canonical approach.

7. **Undo last die** — Should the fight support an "undo last action" option? This is complex because
   damage is applied immediately. Options: (a) no undo, (b) undo only if the opponent hasn't acted yet,
   (c) full undo stack. Not implementing for MVP.
