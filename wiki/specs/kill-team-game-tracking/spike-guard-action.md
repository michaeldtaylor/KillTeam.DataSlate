# Spike: Guard Action — Interrupt Flow

**Status**: Draft  
**Author**: Spike  
**Date**: 2025-07  
**Area**: Kill Team Game Tracking — Guard Action

---

## 1. Introduction

The Guard action is Kill Team's primary reactive mechanic. Unlike all other actions, which resolve
completely within a single activation, Guard creates a persistent interrupt trigger that can fire
during an *opponent's* activation — sometimes several activations later in the same Turning Point.
This ITD (In Turn Delay) nature means the app must maintain per-operative guard state, check it
after every enemy action, and be able to suspend one player's activation, run a complete combat
sub-flow for a different operative, and then resume the original activation cleanly.

The complexity has three layers:

1. **State management**: `IsOnGuard` must be set, cleared, and checked at the right moments across
   different activations and at Turning Point boundaries.
2. **Interrupt injection**: after every enemy action the app must query whether any friendly
   operative can fire a guard interrupt, prompt the guarding player, and optionally launch a full
   Shoot or Fight sub-flow mid-activation.
3. **Sub-flow integration**: the guard interrupt itself is a complete Shoot or Fight action. Rather
   than duplicating that logic, this spike defines a thin wrapper that delegates to the existing
   `ShootSessionOrchestrator` and `FightSessionOrchestrator` documented in `spike-shoot-ui.md` and
   `spike-fight-ui.md` respectively.

**Worked example operatives** (used throughout this document):

| Operative | Player | Side | W | APL | Save | Weapons |
|---|---|---|---|---|---|---|
| Intercessor Sergeant | Michael | Attacker / Initiative | 15 | 3 | 3+ | Bolt rifle (ATK 4, Hit 3+, DMG 3/4, Heavy), Fists (ATK 4, Hit 4+, DMG 3/4) |
| Plague Marine Fighter | Solomon | Defender | 14 | 2 | 3+ | Boltgun (ATK 4, Hit 3+, DMG 3/4, Heavy), Plague knife (ATK 4, Hit 3+, DMG 3/4) |

**Worked scenario**: The Plague Marine Fighter (Solomon) took the Guard action during a prior
activation. Michael's Intercessor Sergeant activates and performs a Dash. The Dash triggers the
Guard interrupt prompt. Solomon chooses to SHOOT with the Boltgun. After the shoot resolves,
Michael's activation continues with the remaining AP.

---

## 2. Rules Recap

> **Official rules text (verbatim, Kill Team V3.0):**
>
> **GUARD (ITD) 1AP:**
> Until the start of the next Turning Point, each time a **visible enemy operative** performs an
> action, you can **interrupt** after that action: perform a **FIGHT** or **SHOOT** against that
> enemy operative. That **FIGHT** or **SHOOT** then resolves normally. After this interrupt is
> resolved, or if the operative is **Engaged**, or if the operative's **order changes**, the guard
> ends.

### Guard State Entry Conditions

- Operative must be on **Engage** order (cannot take Guard on Conceal order).
- Operative must **not** be within control range (6″) of any enemy operative at the time of taking
  the action.
- Costs **1AP**.
- Sets `IsOnGuard = true` on the operative's `GameOperativeState`.

### Guard State Exit Conditions (four cases)

| # | Trigger | When it happens |
|---|---|---|
| 1 | **Interrupt resolved** | After the guard operative's FIGHT or SHOOT sub-flow completes. |
| 2 | **Guard operative becomes Engaged** | When an enemy operative moves within 6″ of the guard operative (enters control range). Checked after every enemy action that could cause movement. |
| 3 | **Guard operative's order changes** | Any effect (ploy, rule, forced change) that sets the operative's `Order` to Conceal — immediately clears the guard. |
| 4 | **Turning Point starts** | At the very start of a new Turning Point, all `IsOnGuard` flags are reset to `false` before any activations begin. |

### When the Interrupt Fires

After every action a visible enemy operative performs, the app checks whether any friendly operative
is On Guard and visible to that enemy operative. Visibility is the player's responsibility: the
prompt asks the guarding player to confirm whether the enemy is visible before offering the
interrupt. If the player responds **Yes**, visibility is confirmed and the interrupt proceeds. If
**No**, the interrupt is skipped (guard state is preserved for future actions).

The guarding player may also **Skip** (decline to use the interrupt) even when visibility is
confirmed. Skipping does **not** clear the guard state — the operative remains On Guard for
subsequent actions in the same Turning Point.

---

## 3. Domain Model Changes

### `GameOperativeState` — new field

```csharp
public class GameOperativeState
{
    // ... existing fields ...

    // Set when the operative takes the Guard action (1AP, Engage order, not in control range).
    // Cleared on interrupt resolve, becoming Engaged, order change, or new Turning Point.
    public bool IsOnGuard { get; set; }
}
```

### `Activation` — new field

```csharp
public class Activation
{
    // ... existing fields ...

    // True when this activation was created by a Guard interrupt rather than by the normal
    // alternating-activation flow. The activation has exactly one action (FIGHT or SHOOT).
    public bool IsGuardInterrupt { get; set; }
}
```

### `Action.Type` enum — new value

```csharp
public enum ActionType
{
    Reposition,
    Dash,
    FallBack,
    Charge,
    Shoot,
    Fight,
    Guard,      // ← NEW: the operative takes the Guard action and becomes On Guard
    Other,
}
```

`Guard` actions cost **1AP** and have no `TargetOperativeId` or `WeaponId`. They carry no dice
fields. Their sole effect is setting `IsOnGuard = true` on the acting operative's state.

---

## 4. Service Design

### `GuardResolutionService`

Stateless; contains all Guard-rule logic. No I/O.

```csharp
public class GuardResolutionService
{
    // Returns all operatives from friendlyStates that are currently On Guard and eligible
    // to interrupt. An operative is eligible if IsOnGuard == true and IsIncapacitated == false.
    // Visibility is NOT checked here — it is confirmed interactively by the player at prompt time.
    public IReadOnlyList<GameOperativeState> GetEligibleGuards(
        IEnumerable<GameOperativeState> friendlyStates);

    // Clears IsOnGuard on all operatives in the collection.
    // Called at the start of each new Turning Point before any activations begin.
    // Returns the updated list for fluent chaining / test assertions.
    public IReadOnlyList<GameOperativeState> ClearAllGuards(
        IEnumerable<GameOperativeState> states);

    // Returns true when the guard operative's guard is still valid given the acting enemy.
    // Returns false if the enemy operative is within 6″ of the guard operative — i.e. the
    // guard operative is now Engaged (6″ control range).
    // "Within 6 inches" is confirmed by the player responding to the engagement check prompt;
    // the service receives the result as a boolean parameter for testability.
    public bool IsGuardStillValid(GameOperativeState guard, bool enemyIsInControlRange);
}
```

> **Design note — distance input**: Physical distance cannot be measured by the app. Rather than
> passing `GameOperativeState actingEnemy` and trying to derive distance, the service accepts a
> `bool enemyIsInControlRange` parameter. The orchestrator prompts the player: "Is the guard
> operative now within 6″ of the enemy? (Y/N)". This keeps the service fully unit-testable without
> geometry.

### `GuardInterruptOrchestrator`

Stateful; drives the guard-interrupt UI loop. Owns the Spectre.Console interaction. Injected with
`GuardResolutionService`, `ShootSessionOrchestrator`, `FightSessionOrchestrator`, and
`IAnsiConsole`.

```csharp
public class GuardInterruptOrchestrator
{
    public GuardInterruptOrchestrator(
        GuardResolutionService resolutionService,
        ShootSessionOrchestrator shootOrchestrator,
        FightSessionOrchestrator fightOrchestrator,
        IAnsiConsole console);

    // Entry point — called after every enemy action.
    // actingEnemy:     the operative whose action just completed.
    // friendlyStates:  all friendly operative states (from which eligible guards are filtered).
    // operativeRoster: full roster data keyed by operative ID (for weapon lists, stats, etc.).
    // Returns a list of guard activations created (empty if none triggered).
    public IReadOnlyList<Activation> CheckAndRunInterrupts(
        Operative actingEnemy,
        GameOperativeState actingEnemyState,
        IEnumerable<GameOperativeState> friendlyStates,
        IReadOnlyDictionary<Guid, Operative> operativeRoster);

    // --- Private orchestration methods ---
    private GuardInterruptChoice PromptInterruptChoice(
        GameOperativeState guard, Operative guardOperative, Operative actingEnemy);
    private GuardActionChoice PromptActionChoice(
        GameOperativeState guard, Operative guardOperative);
    private Activation RunShootInterrupt(
        Operative guardOperative, GameOperativeState guardState,
        Operative target, GameOperativeState targetState);
    private Activation RunFightInterrupt(
        Operative guardOperative, GameOperativeState guardState,
        Operative target, GameOperativeState targetState);
    private bool PromptEnemyInControlRange(
        Operative guardOperative, Operative actingEnemy);
}

public enum GuardInterruptChoice { Interrupt, Skip }
public enum GuardActionChoice    { Shoot, Fight }
```

---

## 5. Play Loop Changes

### Updated Firefight Phase pseudocode

The only structural change to the existing play loop is the insertion of `CheckGuardInterrupt` after
every action an operative records during an enemy team's activation.

```
FirefightPhase:
    ClearAllGuards(allStates)          ← at Turning Point start (already in TP transition)

    while readyOperatives.Any():
        activeTeam = DetermineNextTeam()
        operative  = PromptOperativeSelection(activeTeam)
        order      = PromptOrderSelection(operative)
        activation = CreateActivation(operative, order)

        while operative.HasAP():
            action = PromptActionSelection(operative)

            if action == Guard:
                if NOT CanTakeGuard(operative, allStates):
                    ShowError("Cannot take Guard: either on Conceal or enemy within 6″.")
                    continue
                RecordAction(activation, Guard, 1AP)
                operative.State.IsOnGuard = true
                ShowStatus("[ON GUARD 🔒]")
                continue

            RecordAction(activation, action)

            if IsEnemyTeamActivation(activeTeam):
                CheckGuardInterrupt(operative, activation, guardTeam.States)  ← NEW

        MarkExpended(operative)

CheckGuardInterrupt(actingEnemy, enemyActivation, friendlyStates):
    eligibleGuards = resolutionService.GetEligibleGuards(friendlyStates)
    if eligibleGuards.IsEmpty: return

    for each guard in eligibleGuards:
        enemyInControlRange = PromptEnemyInControlRange(guard.Operative, actingEnemy)
        if NOT resolutionService.IsGuardStillValid(guard, enemyInControlRange):
            guard.IsOnGuard = false
            console.MarkupLine($"[yellow]⚠ {guard.Operative.Name} is now Engaged — guard cleared.[/]")
            continue

        choice = interruptOrchestrator.PromptInterruptChoice(guard, actingEnemy)
        if choice == Skip:
            // Guard state preserved. No changes. Next eligible guard (if any) is checked.
            continue

        actionChoice = interruptOrchestrator.PromptActionChoice(guard, actingEnemy)
        interruptActivation = CreateGuardInterruptActivation(guard.Operative, IsGuardInterrupt=true)

        if actionChoice == Shoot:
            result = interruptOrchestrator.RunShootInterrupt(guard, actingEnemy)
        else:  // Fight
            result = interruptOrchestrator.RunFightInterrupt(guard, actingEnemy)

        PersistActivation(interruptActivation)
        guard.IsOnGuard = false
        console.MarkupLine($"[green]✓ {guard.Operative.Name}'s guard is resolved.[/]")

    // Return to actingEnemy's activation, resuming where it left off.
```

### `CanTakeGuard` pre-condition check

```csharp
private bool CanTakeGuard(GameOperativeState operative, IEnumerable<GameOperativeState> allStates)
{
    if (operative.Order == Order.Conceal) return false;
    bool enemyInControlRange = /* player prompted: "Is any enemy within 6″? (Y/N)" */ ...;
    return !enemyInControlRange;
}
```

---

## 6. CLI Interaction Transcript

### Part A — Taking the Guard Action

The Plague Marine Fighter (Solomon) activates on a prior turn and takes Guard. The Fighter is on
Engage order with 2AP and no enemies within 6″.

```
╔══════════════════════════════════════════════════════════════════╗
║             ⚡  ACTIVATION  —  Plague Marine Fighter             ║
║             Solomon  ·  2AP remaining                            ║
╚══════════════════════════════════════════════════════════════════╝

  Order: [Engage]

Select an action:
    Reposition   (1AP)
    Dash         (1AP)
    Fall Back    (1AP)
    Charge       (1AP)
    Shoot        (1AP)
    Fight        (1AP)
  > Guard        (1AP)  ← take Guard; become On Guard
    Other        (1AP)
    End Activation

> Guard

──────────────────────────────────────────────────────────
  Guard requires Engage order and no enemy within 6″.
  Fighter is on Engage order. ✓
──────────────────────────────────────────────────────────

  Is any enemy operative currently within 6″ of Fighter? (Y/N)
> N

  ✓ Guard action recorded. Plague Marine Fighter is now ON GUARD.

  AP spent: 1  (1AP remaining)
```

> **Note**: if the operative is on Conceal order, the Guard option is still displayed in the list
> but selecting it shows an error before spending AP:
>
> ```
> ⚠ Cannot take Guard: Fighter is on Conceal order. Guard requires Engage order.
> ```

After the action is recorded, the board status display updates to reflect the guard state (see
§7). Solomon elects to End Activation with the remaining 1AP:

```
Select an action:
    Reposition   (1AP)
    ...
  > End Activation

> End Activation

  Fighter marked as Expended. [ON GUARD 🔒]
```

---

### Part B — Guard Interrupt Triggered

**Setup**: Michael's Intercessor Sergeant activates (Initiative team, 3AP). Solomon's Plague Marine
Fighter is On Guard (from Part A above).

#### Step 1 — Michael selects Dash

```
╔══════════════════════════════════════════════════════════════════╗
║             ⚡  ACTIVATION  —  Intercessor Sergeant              ║
║             Michael  ·  3AP remaining                            ║
╚══════════════════════════════════════════════════════════════════╝

  Order: [Engage]

Select an action:
    Reposition   (1AP)
  > Dash         (1AP)
    Fall Back    (1AP)
    Charge       (1AP)
    Shoot        (1AP)
    Fight        (1AP)
    Guard        (1AP)
    Other        (1AP)
    End Activation

> Dash

  ✓ Dash recorded. (2AP remaining)
```

#### Step 2 — App checks for guard interrupts

Immediately after recording the Dash, the app calls `CheckGuardInterrupt`.

```
──────────────────────────────────────────────────────────────────
  🔒 GUARD INTERRUPT CHECK
──────────────────────────────────────────────────────────────────

  Plague Marine Fighter (Solomon) is On Guard.

  Is Fighter now within 6″ of Intercessor Sergeant? (Y/N)
> N

  Fighter is still On Guard.
```

#### Step 3 — Interrupt prompt

```
──────────────────────────────────────────────────────────────────
  🔒 GUARD INTERRUPT OPPORTUNITY
──────────────────────────────────────────────────────────────────

  Plague Marine Fighter is on guard.
  Michael's Intercessor Sergeant just performed: Dash

  Solomon — interrupt the Sergeant's activation?

  > Interrupt
    Skip  (guard remains active)

> Interrupt
```

#### Step 4 — Action choice

```
──────────────────────────────────────────────────────────────────
  Choose guard interrupt action (Fighter vs Intercessor Sergeant):
──────────────────────────────────────────────────────────────────

  > SHOOT  (Boltgun or Plague knife — ranged weapons only for Shoot)
    FIGHT  (must be within control range)

> SHOOT
```

#### Step 5 — Guard interrupt activation created

```
  ✓ Guard interrupt activation created for Plague Marine Fighter.
    [IsGuardInterrupt = true]
```

#### Step 6 — Shoot sub-flow (abbreviated)

The full Shoot flow runs exactly as documented in `spike-shoot-ui.md`. The summary below shows
only the entry point, key prompts specific to this scenario, and the exit point.

```
╔══════════════════════════════════════════════════════════════════╗
║                    🎯  SHOOT ACTION  🎯                           ║
║       Plague Marine Fighter  →  Intercessor Sergeant             ║
╚══════════════════════════════════════════════════════════════════╝

  Target confirmed: Intercessor Sergeant  [15/15W]  [Engage]

  Fighter selects a ranged weapon:
  > Boltgun  (ATK 4 | Hit 3+ | DMG 3/4 | Heavy)
    [Plague knife is Melee — not offered for Shoot]

  How is Intercessor Sergeant positioned?
  > 1. In cover
    2. Obscured
    3. Neither

> 3

  Fighter rolls 4 dice  (Boltgun, Hit 3+):

  Enter Fighter's dice (space or comma separated):
> 5 4 3 1

  A1: 5  → HIT  ✓
  A2: 4  → HIT  ✓
  A3: 3  → HIT  ✓
  A4: 1  → MISS ✗  (discarded)

  Attack pool: 3 dice  (0 CRITs, 3 HITs)

  ──────────────────────────────────────────────────────────
  Intercessor Sergeant rolls 3 defence dice  (Save 3+):
  ──────────────────────────────────────────────────────────

  Enter Sergeant's defence dice:
> 6 4 2

  D1: 6  → SAVE ✓  (CRIT save)
  D2: 4  → SAVE ✓
  D3: 2  → MISS ✗  (discarded)

  ──────────────────────────────────────────────────────────
  Save allocation (auto-optimal):
  ──────────────────────────────────────────────────────────

    Saves available : 1 crit save, 1 normal save (2 total)
    Attacks         : 3 HITs

    D1 (CRIT save)  → blocks A1 (HIT)
    D2 (save)       → blocks A2 (HIT)
    A3 (HIT)        → UNBLOCKED → 3 normal damage

  ──────────────────────────────────────────────────────────
  Damage applied:
  ──────────────────────────────────────────────────────────

    [red]💥 3 normal damage to Intercessor Sergeant.[/]
    Sergeant: 15W → 12W

  Shoot action complete.
  Narrative note (optional, Enter to skip):
> Boltgun rounds crack against the Sergeant's armour.

  ✓ Action recorded.
```

> The complete Shoot sub-flow (cover rules, Piercing, Stun, Hot, CP re-rolls, etc.) is fully
> documented in `spike-shoot-ui.md`. The interrupt does not change that flow — it only changes the
> activation context in which it runs.

#### Step 7 — Guard state cleared and activation resumed

```
──────────────────────────────────────────────────────────────────
  ✓ Guard interrupt resolved.
  Plague Marine Fighter's guard is cleared.
──────────────────────────────────────────────────────────────────

  Resuming Intercessor Sergeant's activation.
  2AP remaining.
```

The board status panel refreshes: Fighter's `[ON GUARD 🔒]` tag is removed (see §7).

Michael's activation continues normally with 2AP:

```
Select an action:
    Reposition   (1AP)
    Dash         (1AP)
    ...
  > Shoot        (1AP)
    ...
```

---

### Part C — Guard Skip

Solomon declines the interrupt. The guard state is preserved.

```
──────────────────────────────────────────────────────────────────
  🔒 GUARD INTERRUPT OPPORTUNITY
──────────────────────────────────────────────────────────────────

  Plague Marine Fighter is on guard.
  Michael's Intercessor Sergeant just performed: Dash

  Solomon — interrupt the Sergeant's activation?

    Interrupt
  > Skip  (guard remains active)

> Skip

  Skipped. Fighter remains On Guard.

  Resuming Intercessor Sergeant's activation. 2AP remaining.
```

The board status still shows `[ON GUARD 🔒]` on the Fighter. The next action Michael's Sergeant
takes will trigger another interrupt check.

---

### Part D — Guard Auto-Cleared: Enemy Enters Control Range

After a Charge or Reposition, the app's guard check asks whether the acting enemy is now within 6″
of the guard operative.

```
──────────────────────────────────────────────────────────────────
  🔒 GUARD INTERRUPT CHECK
──────────────────────────────────────────────────────────────────

  Plague Marine Fighter (Solomon) is On Guard.

  Is Fighter now within 6″ of Intercessor Sergeant? (Y/N)
> Y

  ⚠ Enemy entered control range — Plague Marine Fighter is now
    Engaged. Guard automatically cleared.

  Resuming Intercessor Sergeant's activation. 1AP remaining.
```

`IsOnGuard` is set to `false`. No interrupt is offered. No sub-flow runs.

---

## 7. Status Display

When an operative is On Guard, the board status panel appends the `[ON GUARD 🔒]` tag after the
order badge:

```
╔══ Board Status ══════════════════════════════════════════════════╗
║ SOLOMON — Plague Marines                                         ║
║                                                                  ║
║   1. Plague Marine Champion    [15/15W] [Engage]                 ║
║   2. Plague Marine Fighter     [14/14W] [Engage] [ON GUARD 🔒]  ║ ← guarding
║                                                                  ║
║ MICHAEL — Angels of Death                                        ║
║                                                                  ║
║   1. Intercessor Sergeant      [12/15W] [Engage]                 ║
╚══════════════════════════════════════════════════════════════════╝
```

After the guard is cleared (interrupt resolved, engaged, order changed, or TP start), the tag is
removed:

```
║   2. Plague Marine Fighter     [14/14W] [Engage]                 ║
```

### Spectre.Console rendering details

| Element | Component | Style |
|---|---|---|
| `[ON GUARD 🔒]` badge | `Markup` inline in the status table | `[bold cyan]` |
| Guard interrupt header | `Rule` with title | `[bold cyan]🔒 GUARD INTERRUPT[/]` |
| Guard auto-cleared notice | `Markup` | `[yellow]⚠ … guard automatically cleared.[/]` |
| Guard resolved notice | `Markup` | `[green]✓ … guard is cleared.[/]` |
| Skip notice | `Markup` | `[dim]Skipped. Fighter remains On Guard.[/]` |
| Interrupt action selection | `SelectionPrompt<GuardActionChoice>` | Standard prompt |
| Engage check prompt | `ConfirmationPrompt` | "Is Fighter now within 6″ of [enemy]? (Y/N)" |

---

## 8. Edge Cases

### 1. Multiple Operatives on Guard

If more than one friendly operative is On Guard when an enemy action completes, the app prompts
for each eligible guard in sequence. Each guard independently receives the interrupt prompt and can
choose to FIGHT, SHOOT, or Skip. If Guard A interrupts and clears, the loop continues to Guard B
(if eligible — it must still pass the engagement check). The order of prompting follows the order
guards were registered in `GetEligibleGuards`, which returns them in the same iteration order as
`friendlyStates` (typically insertion order from the roster).

```
  🔒 GUARD INTERRUPT CHECK

  Two operatives are On Guard:

  ─── Guard 1 of 2: Plague Marine Fighter ──────────────────────────
  Solomon — interrupt Michael's Dash? > Interrupt → SHOOT → [resolves]
  ✓ Fighter's guard cleared.

  ─── Guard 2 of 2: Plague Marine Warrior ──────────────────────────
  Solomon — interrupt Michael's Dash? > Skip
  Skipped. Warrior remains On Guard.
```

### 2. Guard Skip — Does It Persist?

Yes. Skipping the interrupt does **not** clear `IsOnGuard`. The operative remains On Guard for the
next enemy action in this Turning Point. This is intentional: the guarding player may be waiting
for a more valuable action (e.g. a Shoot rather than a Reposition) to interrupt.

### 3. Guard Operative Becomes Incapacitated

If the guard operative reaches 0 wounds (e.g., from an earlier enemy Shoot action that triggered
a different guard interrupt, or from a direct attack while On Guard), `IsIncapacitated` is set to
`true`. `GetEligibleGuards` excludes incapacitated operatives, so the guard does not fire. The
`IsOnGuard` flag is cleared explicitly at the same time `IsIncapacitated` is set to `true`, to
avoid stale state.

### 4. Enemy Moves Out of LOS Mid-Action

Guard fires after visible enemy actions only. The app does not track visibility geometrically; the
player confirms visibility at the interrupt prompt. If Solomon selects **Skip** because the enemy
moved behind a wall during the action, the guard is preserved for future visible actions.

### 5. Guard on Turning Point Start

At the transition to a new Turning Point, `ClearAllGuards` is called for all operatives on both
teams before any activations begin. This applies even if the guard operative has not yet activated
in the current TP. No prompt is shown — the clear is automatic and silent (the board status simply
drops the `[ON GUARD 🔒]` tags on next render).

### 6. Guard Operative's Order Changes

If any rule or ploy forces the guard operative from Engage to Conceal, `IsOnGuard` is set to `false`
immediately when `Order` is updated. The status display reflects this on next render. The app's
order-change handler must always include:

```csharp
if (state.Order == Order.Conceal)
    state.IsOnGuard = false;
```

### 7. Guard During a Counteract Activation

Counteract activations follow the same rules as normal activations. If the enemy performs a
Counteract action (single 1AP action), a friendly guard operative can interrupt that action
exactly as it would interrupt a normal action. The interrupt check is called after the Counteract
action is recorded.

### 8. Can a Counteract Activation Trigger Guard?

Yes. A friendly operative performing a Counteract action could trigger a guard interrupt from an
*opponent* that is On Guard. The flow is identical: the opponent's guard check fires after the
Counteract action, and the opponent's guard operative may interrupt. `IsGuardInterrupt = true` will
be set on the resulting interrupt activation, regardless of whether the action being interrupted
was a Counteract or a normal activation action.

### 9. Guard Fires into FIGHT but Enemy Not in Control Range

If the guarding player selects **FIGHT** but the enemy operative is not within 6″ (melee/control
range), the Fight action is not valid. The orchestrator checks this after the player selects FIGHT:

```
  Fighter selects FIGHT vs Intercessor Sergeant.

  ⚠ Cannot Fight — Intercessor Sergeant is not within control range (6″).
     Only SHOOT is available, or you may Skip.

  > SHOOT
    Skip  (guard remains active)
```

If only FIGHT would have been valid (e.g. the guard operative has no ranged weapon and the enemy
is out of control range), the interrupt cannot be used:

```
  ⚠ Fighter cannot interrupt:
     – FIGHT: enemy not in control range.
     – SHOOT: Fighter has no ranged weapon.
  Guard remains active for future actions.
```

The guard state is preserved in both cases.

---

## 9. Persistence Changes

### New SQLite columns

```sql
-- game_operative_states table
ALTER TABLE game_operative_states
    ADD COLUMN is_on_guard INTEGER NOT NULL DEFAULT 0;

-- activations table
ALTER TABLE activations
    ADD COLUMN is_guard_interrupt INTEGER NOT NULL DEFAULT 0;
```

### New action type

The `actions.type` column gains a new valid string value: `'Guard'`.

No additional columns are needed for Guard actions themselves — the `APCost` is always 1, and all
weapon/dice/damage columns remain `NULL` (Guard is not a combat action).

### Schema migration

Bump `schema_version` from the current value to the next version. Apply the following migration:

```sql
-- Migration: guard action support
-- Applies after existing migrations.

BEGIN TRANSACTION;

ALTER TABLE game_operative_states
    ADD COLUMN is_on_guard INTEGER NOT NULL DEFAULT 0;

ALTER TABLE activations
    ADD COLUMN is_guard_interrupt INTEGER NOT NULL DEFAULT 0;

-- No DDL change needed for actions.type — it is a TEXT column storing enum names.
-- The new value 'Guard' is valid immediately.

UPDATE schema_migrations
SET version = version + 1,
    applied_at = CURRENT_TIMESTAMP
WHERE id = 1;

COMMIT;
```

### `GameOperativeState` C# mapping

```csharp
// Dapper / EF mapping note:
// is_on_guard  → IsOnGuard  (bool, mapped from INTEGER 0/1)
```

### `Activation` C# mapping

```csharp
// is_guard_interrupt  → IsGuardInterrupt  (bool, mapped from INTEGER 0/1)
```

---

## 10. xUnit Tests

Test conventions, framework dependencies, and snapshot guidance are defined in the main spec:
`wiki/specs/kill-team-game-tracking/spec.md` — see the **Testing** section. All tests below belong
in `KillTeamAgent.Tests` and follow the naming convention `MethodName_Scenario_ExpectedResult`.

```csharp
public class GuardResolutionServiceTests
{
    private readonly GuardResolutionService _sut = new();

    [Fact]
    public void GetEligibleGuards_ReturnsOnlyOnGuardOperatives()
    {
        // Arrange: three states — one IsOnGuard=true, two IsOnGuard=false.
        // Act: call GetEligibleGuards.
        // Assert: returns exactly the one OnGuard operative.
    }

    [Fact]
    public void GetEligibleGuards_ExcludesIncapacitatedOperatives()
    {
        // Arrange: one state with IsOnGuard=true AND IsIncapacitated=true.
        // Act: call GetEligibleGuards.
        // Assert: returns empty list — incapacitated operatives are ineligible even if OnGuard.
    }

    [Fact]
    public void IsGuardStillValid_ReturnsFalseWhenEnemyInControlRange()
    {
        // Arrange: guard state (IsOnGuard=true), enemyIsInControlRange=true.
        // Act: call IsGuardStillValid.
        // Assert: returns false — guard is no longer valid; operative is now Engaged.
    }

    [Fact]
    public void IsGuardStillValid_ReturnsTrueWhenEnemyOutsideControlRange()
    {
        // Arrange: guard state (IsOnGuard=true), enemyIsInControlRange=false.
        // Act: call IsGuardStillValid.
        // Assert: returns true — operative is not Engaged; guard is still valid.
    }

    [Fact]
    public void ClearAllGuards_ClearsAllOnGuardFlags()
    {
        // Arrange: three states, all IsOnGuard=true.
        // Act: call ClearAllGuards.
        // Assert: all returned states have IsOnGuard=false; the original list is not mutated.
    }
}

public class GuardPlayLoopTests
{
    [Fact]
    public void PlayLoop_GuardInterrupt_CreatesIsGuardInterruptActivation()
    {
        // Arrange: Plague Marine Fighter IsOnGuard=true; Intercessor Sergeant performs Dash.
        //          GuardInterruptOrchestrator configured to return Interrupt → SHOOT.
        // Act: invoke CheckAndRunInterrupts (or the play loop integration method) with the Dash action.
        // Assert: a new Activation is created with IsGuardInterrupt=true and ActionType=Shoot.
    }

    [Fact]
    public void PlayLoop_GuardAutoCleared_WhenEnemyEntersControlRange()
    {
        // Arrange: Fighter IsOnGuard=true; player responds Y to "enemy within 6″" prompt.
        // Act: invoke CheckAndRunInterrupts.
        // Assert: Fighter.IsOnGuard=false; no interrupt activation is created.
    }

    [Fact]
    public void PlayLoop_GuardSkip_DoesNotClearGuardState()
    {
        // Arrange: Fighter IsOnGuard=true; player responds to engagement check (N), then selects Skip.
        // Act: invoke CheckAndRunInterrupts.
        // Assert: Fighter.IsOnGuard remains true; no interrupt activation created.
    }
}
```

### Additional recommended tests

Beyond the 8 stubs above, the following cases are high-value and should be added to the test plan:

- `GuardResolutionService_ClearAllGuards_ReturnsUpdatedStateObjects` — verify the returned list
  items are the same objects (or new copies, depending on implementation) with the flag cleared,
  not unrelated instances.
- `PlayLoop_MultipleGuards_EachPromptedIndependently` — two operatives On Guard; first interrupts,
  second skips; verify first's flag cleared and second's preserved.
- `PlayLoop_GuardTakenOnConcealOrder_ActionRefused` — verify Guard action is refused when
  operative is on Conceal order (pre-condition gate in the action handler).
- `PlayLoop_GuardTakenInControlRange_ActionRefused` — player responds Y to "enemy within 6″" at
  Guard-take time; verify action is refused without spending AP.
- `PlayLoop_GuardClearedOnTurningPointStart_BeforeActivations` — TP transition calls
  `ClearAllGuards`; verify all flags reset before the first activation of the new TP.
- `PlayLoop_GuardFight_EnemyNotInControlRange_FightRefused_GuardPreserved` — player selects FIGHT
  but enemy is out of range; verify Fight is refused and `IsOnGuard` remains `true`.

---

## Open Questions

1. **Guard interrupt on the guard operative's own team's activation** — The rules state the interrupt
   fires when a *visible enemy* operative performs an action. Confirm that "enemy" is strictly the
   opposing team, not any non-friendly operative (irrelevant in standard 2-player, relevant in
   multi-player modes if ever supported).

2. **Multiple guards, one target: ordering of interrupts** — If two guards interrupt the same
   enemy action and both choose FIGHT, the second guard may find the enemy incapacitated after the
   first guard's Fight resolves. The second guard's FIGHT must still be offered (the rules do not
   cancel queued interrupts on incapacitation), but the Fight pre-condition check in
   `FightSessionOrchestrator` will handle the incapacitated target gracefully.

3. **IsGuardInterrupt activations and AP count** — A guard interrupt activation contains exactly
   one action (FIGHT or SHOOT, both 1AP). Should `APL` on the guard interrupt `Activation` be set
   to 1 (actual spent) or to the operative's normal APL (for consistency with other activations)?
   Recommend: set `APL = 1` to accurately reflect guard interrupt activations in session statistics.

4. **Guard action in the action history display** — Guard actions have no combat output and no
   target. The narrative display should show "Guard (On Guard)" to distinguish it from a no-op.
   Confirm the wording with the spec owner.

5. **Narrative note on guard interrupt** — Should the post-interrupt sub-flow prompt for a
   narrative note? The existing Shoot and Fight orchestrators always prompt. For guard interrupts,
   this is likely desirable (flavour: "The Fighter's boltgun barked as the enemy dashed past"). No
   change needed — the sub-orchestrators handle this already.
