# Spike: US-008 — Simulate Combat Encounter

**Author:** Spike research  
**Date:** 2026-03-14  
**Status:** Updated after human review  
**Related:** spec.md US-008, spike-weapon-rules.md, CombatResolutionService.cs

---

## 1. Overview

This spike investigates the design of a standalone `simulate` CLI command that lets players test fight and shoot mechanics without needing a persisted game session. The command is fully ephemeral — no data is written to SQLite.

The `simulate` command is the first in a broader **test mode** family of commands (see Decision 2). Its primary purpose is player-vs-AI testing: the player selects an operative from their own kill team roster; the AI (or the player acting as the opponent) selects an opposing operative from a different roster. This lets the player experience the full fight/shoot UX flow — dice rolls, re-rolls, Strike/Block decisions — as if playing against a human, without needing an active game session.

Both fight and shoot encounters are available within a single `simulate` session. After each encounter resolves, the player chooses what to do next (fight again, shoot, or exit) without re-selecting operatives.

---

## 2. Decision Log

### Decision 1 — Operative / Weapon Input Mode

**Question:** Should users enter operative and weapon stats ad-hoc at runtime, or select from an already-imported roster?

**Decision:** **Always roster-based. Ad-hoc stat entry is removed.** Kill teams already imported into the database (via `import-kill-teams`, which scans `DataSlate:RosterFolder`) are listed directly — no flag required.

**Selection flow:**
1. `IKillTeamRepository.GetAllAsync()` → load all imported kill teams. If none exist, print `"[red]No rosters found. Run import-kill-teams first.[/]"` and exit.
2. `SelectionPrompt`: "Your kill team:" → lists all imported kill teams by name
3. `SelectionPrompt`: "Your operative:" → lists operatives in the chosen kill team (Spectre.Console `SelectionPrompt` has built-in type-to-filter search — no custom autocomplete needed)
4. `SelectionPrompt`: "Opponent kill team (AI):" → player picks the kill team the AI will play
5. `SelectionPrompt`: "Opponent operative (AI):" → player explicitly picks which operative the AI controls

Weapons are already on the operatives (loaded from the roster) — no manual weapon stat entry. Weapon selection (which weapon to use for each encounter) follows the same `SelectionPrompt` pattern already used in `FightSessionOrchestrator` and `ShootSessionOrchestrator`.

**Rationale:**
- Roster-based against real imported data produces the most meaningful test scenarios — the player tests actual weapons from their real kill teams.
- There are a finite number of imported rosters; listing them all is the simplest and clearest UX.
- The player explicitly chooses the AI's operative — this gives full control over the matchup and avoids surprise selections.
- `SelectionPrompt`'s built-in search handles autocomplete; no custom implementation needed.
- Removing ad-hoc entry eliminates a large class of input validation complexity.

---

### Decision 2 — Simulate UX Style

**Question:** Guided Spectre.Console sub-menu (like `PlayCommand`) or single-command args?

**Decision:** **Guided Spectre.Console interactive sub-menu.**

**Rationale:**
- Consistent with the rest of the CLI (all game commands are prompt-driven).
- Reuses existing `SelectionPrompt` patterns already established in `ShootSessionOrchestrator` and `FightSessionOrchestrator`.
- The `simulate` command is the **first of a broader "test mode" family**. Future test commands might cover: strategy phase testing, counteract testing, guard testing, blast/torrent multi-target testing. The entry point for this family could eventually be `dataslate test` with subcommands. For now, the command remains `dataslate simulate`, but the design should be mindful that it is one of potentially several test commands rather than a one-off utility. This is noted in Technical Considerations (§7).

---

### Decision 3 — Session Type

**Question:** Fight only, Shoot only, or both with a choice prompt?

**Decision:** **Both fight and shoot are available within a single `simulate` session.** After operative selection, the session enters a loop. After each encounter resolves, the app prompts:

```
What next?
  ❯ Fight again
    Shoot
    Change operatives
    Done
```

**"Change operatives"** restarts the operative selection flow (steps 2–5 in Decision 1) without exiting the command. The player can pick new kill teams or swap individual operatives, then continue with new encounters. This allows testing different matchups within a single session without restarting the CLI.

The player explicitly chooses which operative the AI controls at setup (Decision 1 step 4–5). On "Change operatives" the same explicit selection is shown again.

**Rationale:**
- Both fight and shoot are equally important for mechanics testing; switching freely without restarting is essential for iterating on matchups.
- "Change operatives" is the key workflow: play a fight, see the result, swap to a different operative or kill team, fight again — rapid iteration without CLI restarts.
- Wound state resets to full for each new encounter (the `InMemoryGameOperativeStateRepository` is re-initialised per encounter, not per session).

---

### Decision 4 — Persistence

**Question:** Should simulate sessions be saved to SQLite?

**Decision:** **Fully ephemeral — nothing written to SQLite.** *(Confirmed REJECTED by user review.)*

**Rationale:**
- Simulate's purpose is to get a feel for the UI interaction flow, not to generate game history. Persisting results would pollute game history and stats.
- Users expect simulate to be low-commitment — they should be able to run it repeatedly while experimenting and never contaminate real data.
- If future iterations want a "sandbox game log" feature, it can be gated behind an explicit `--save` flag.

---

### Decision 5 — Reuse of Existing Orchestrators

**Question:** Can `FightSessionOrchestrator` and `ShootSessionOrchestrator` be reused as-is, or do they need modification?

**Decision:** **Reuse the existing orchestrators unchanged via in-memory repository implementations and synthetic domain objects.**

**Approach:**

The existing orchestrators require DB-backed entities and repository calls. Rather than duplicating or modifying them, the simulate command satisfies their dependencies by providing:

1. **`InMemoryGameOperativeStateRepository : IGameOperativeStateRepository`** — stores wound/incapacitation state in a `Dictionary<Guid, GameOperativeState>` keyed by state ID. All `UpdateWoundsAsync`, `SetIncapacitatedAsync`, `SetReadyAsync`, `SetCounteractUsedAsync`, etc. calls operate on this in-memory dictionary. `GetByGameAsync` returns the two simulated operative states. Nothing touches SQLite.

2. **`InMemoryActionRepository : IActionRepository`** — no-op implementation. `CreateAsync` returns the action unchanged (no DB write). `UpdateNarrativeAsync` and `GetByActivationAsync` are no-ops / return empty. Narrative notes are silently discarded (not needed in simulation).

3. **Synthetic `Game` object** — constructed directly from the domain model with `Guid.NewGuid()` IDs and no DB backing:
   ```csharp
   var syntheticGame = new Game
   {
       Id = Guid.NewGuid(),
       TeamAId = playerKillTeam.Id,
       TeamBId = opponentKillTeam.Id,
       PlayerAId = Guid.NewGuid(), // synthetic, unused
       PlayerBId = Guid.NewGuid(), // synthetic, unused
       CpTeamA = 0,  // no CP in simulation
       CpTeamB = 0
   };
   ```

4. **Synthetic `TurningPoint`** — `new TurningPoint { Id = Guid.NewGuid(), GameId = syntheticGame.Id, Number = 1 }`.

5. **Synthetic `Activation`** — `new Activation { Id = Guid.NewGuid(), TurningPointId = syntheticTp.Id, OperativeId = playerOperative.Id }`.

These synthetic objects and in-memory repositories are constructed in `SimulateCommand` and passed directly to the existing orchestrators at call time, bypassing DI for the repository parameters (the orchestrators already accept them as constructor/method parameters).

**CP re-rolls:** Setting `CpTeamA = 0` and `CpTeamB = 0` on the synthetic `Game` means the existing `RerollOrchestrator` CP re-roll logic will find 0 CP and naturally skip the CP re-roll prompt. No changes to `RerollOrchestrator` are needed for this. The `ApplyWeaponRerollsOnlyAsync` method is still useful as a cleaner interface that makes the no-CP intent explicit, but is optional.

**Why not null-object injection or duplicate orchestrators:**
- Null-object injection introduces silent behaviour changes in production code.
- Duplicating orchestrators (`SimulateFightOrchestrator`, `SimulateShootOrchestrator`) means duplicating substantial UX logic that must stay in sync with the production orchestrators — a maintenance burden and divergence risk.
- In-memory repos + synthetic objects is the approach that maximises reuse and keeps the existing orchestrators as the single source of truth for combat UX.

**Re-rolls in simulation:** Weapon re-rolls (Balanced, Ceaseless, Relentless) are fully supported via the existing `RerollOrchestrator`. CP re-rolls are suppressed by the 0 CP on the synthetic game. The `ApplyWeaponRerollsOnlyAsync` method on `RerollOrchestrator` is a nice-to-have for a cleaner API surface but is not required.

---

## 3. Proposed Command Signature

```
dataslate simulate
```

No flags. Operative selection is always roster-based and always interactive. The session type (Fight/Shoot) is chosen within the session loop, not as a command-line flag.

**Examples:**
```bash
dataslate simulate
```

---

## 4. Proposed Flow Diagram

```
dataslate simulate
│
├─── OPERATIVE SELECTION (runs at start and on "Change operatives")
│     ├─ IKillTeamRepository.GetAllAsync() → if empty: error + exit
│     ├─ SelectionPrompt: "Your kill team:" → all imported teams
│     ├─ SelectionPrompt: "Your operative:" → operatives in chosen team (type-to-filter)
│     ├─ SelectionPrompt: "Opponent kill team (AI):" → all imported teams
│     └─ SelectionPrompt: "Opponent operative (AI):" → player explicitly picks AI's operative
│
└─── SESSION LOOP (repeats until "Done")
      │
      ├─── SelectionPrompt: "What next?"
      │     ├─ Fight
      │     ├─ Shoot
      │     ├─ Change operatives → re-runs OPERATIVE SELECTION, then back to loop
      │     └─ Done → exit
      │
      ├─── [FIGHT selected]
      │     ├─ SelectionPrompt: your operative's weapon
      │     ├─ SelectionPrompt: opponent operative's weapon
      │     ├─ Show attacker/defender stats summary
      │     ├─ Fight assist prompt (0-2)
      │     ├─ "Roll for me" / "Enter manually" → attacker dice
      │     ├─ Apply weapon re-rolls (Balanced/Ceaseless/Relentless) if applicable
      │     │   (CP re-roll suppressed — synthetic game has 0 CP)
      │     ├─ "Roll for me" / "Enter manually" → defender dice (if has melee weapon)
      │     ├─ Apply Shock (if applicable)
      │     ├─ ALTERNATING LOOP:
      │     │   ├─ Display pools table (wounds / remaining dice)
      │     │   ├─ [AI Advisor "?" option at each turn — US-009]
      │     │   ├─ SelectionPrompt: Strike / Block
      │     │   └─ Apply action → update InMemoryGameOperativeStateRepository
      │     └─ Show final result: attacker damage dealt, defender damage dealt, incapacitations
      │
      └─── [SHOOT selected]
            ├─ SelectionPrompt: your operative's weapon
            ├─ Cover / Obscured prompt
            ├─ Fight assist prompt (0-2)
            ├─ "Roll for me" / "Enter manually" → attack dice
            ├─ Apply weapon re-rolls (Balanced/Ceaseless/Relentless) if applicable
            │   (CP re-roll suppressed — synthetic game has 0 CP)
            ├─ Defence dice count prompt + roll/enter
            ├─ Resolve via CombatResolutionService.ResolveShoot(...)
            ├─ [AI Advisor "?" option after result — US-009]
            └─ Display result table (unblocked crits/normals, damage, Hot self-damage, Stun)
```

---

## 5. Data Flow: Roster Loading and Synthetic Domain Objects

Operatives and weapons are loaded from imported rosters via `IKillTeamRepository.GetWithOperativesAsync(...)` — no new repository needed.

To reuse the existing orchestrators, `SimulateCommand` constructs synthetic domain objects and in-memory repositories before calling them:

```csharp
// Load rosters
var playerTeam    = await killTeamRepo.GetWithOperativesAsync(playerTeamId);
var opponentTeam  = await killTeamRepo.GetWithOperativesAsync(opponentTeamId);

// Synthetic domain objects — no DB writes ever occur for these
var syntheticGame = new Game
{
    Id       = Guid.NewGuid(),
    TeamAId  = playerTeam.Id,
    TeamBId  = opponentTeam.Id,
    PlayerAId = Guid.NewGuid(), // synthetic, not used for display
    PlayerBId = Guid.NewGuid(),
    CpTeamA  = 0,  // 0 CP suppresses CP re-roll prompts in RerollOrchestrator
    CpTeamB  = 0
};

var syntheticTp = new TurningPoint
{
    Id         = Guid.NewGuid(),
    GameId     = syntheticGame.Id,
    Number     = 1
};

var syntheticActivation = new Activation
{
    Id              = Guid.NewGuid(),
    TurningPointId  = syntheticTp.Id,
    OperativeId     = playerOperative.Id
};

// In-memory repositories — satisfy IGameOperativeStateRepository / IActionRepository
// without touching SQLite
var stateRepo  = new InMemoryGameOperativeStateRepository();
var actionRepo = new InMemoryActionRepository();

// Seed initial operative states
await stateRepo.CreateAsync(new GameOperativeState
{
    Id           = Guid.NewGuid(),
    GameId       = syntheticGame.Id,
    OperativeId  = playerOperative.Id,
    CurrentWounds = playerOperative.Wounds,
    Order        = Order.Conceal,
    IsReady      = true
});
await stateRepo.CreateAsync(new GameOperativeState
{
    Id           = Guid.NewGuid(),
    GameId       = syntheticGame.Id,
    OperativeId  = opponentOperative.Id,
    CurrentWounds = opponentOperative.Wounds,
    Order        = Order.Conceal,
    IsReady      = true
});

// Pass to existing orchestrators — they work unchanged
await fightOrchestrator.RunAsync(syntheticGame, syntheticTp, syntheticActivation,
    playerOperative, opponentOperative, stateRepo, actionRepo);
```

`SpecialRuleParser.Parse(...)` is already called by the orchestrators on the loaded weapon's `SpecialRules` string — no new parsing logic needed.

---

## 6. In-Memory State Tracking

`InMemoryGameOperativeStateRepository` implements `IGameOperativeStateRepository` using an in-memory `Dictionary<Guid, GameOperativeState>` keyed by state ID. All wound and incapacitation updates from the existing orchestrators write to this dictionary — not to SQLite.

```csharp
public class InMemoryGameOperativeStateRepository : IGameOperativeStateRepository
{
    private readonly Dictionary<Guid, GameOperativeState> _states = new();

    public Task CreateAsync(GameOperativeState state)
    {
        _states[state.Id] = state;
        return Task.CompletedTask;
    }

    public Task<IEnumerable<GameOperativeState>> GetByGameAsync(Guid gameId)
        => Task.FromResult(_states.Values.Where(s => s.GameId == gameId));

    public Task UpdateWoundsAsync(Guid id, int currentWounds)
    {
        if (_states.TryGetValue(id, out var s)) s.CurrentWounds = currentWounds;
        return Task.CompletedTask;
    }

    public Task SetIncapacitatedAsync(Guid id, bool isIncapacitated)
    {
        if (_states.TryGetValue(id, out var s)) s.IsIncapacitated = isIncapacitated;
        return Task.CompletedTask;
    }

    // UpdateOrderAsync, UpdateGuardAsync, SetAplModifierAsync,
    // SetReadyAsync, SetCounteractUsedAsync — same pattern
}
```

`InMemoryActionRepository` is a no-op implementation — `CreateAsync` returns the action unchanged and all other methods are empty. This satisfies the existing orchestrators' `IActionRepository` dependency without writing anything.

No wound tracking variables in the orchestrators need to change — they already call `stateRepository.UpdateWoundsAsync(...)` which now routes to the dictionary.

---

## 7. Technical Considerations

### 7.1 New Classes

| Class | Location | Responsibility |
|---|---|---|
| `SimulateCommand` | `Console\Commands\` | Entry point; roster selection; constructs synthetic domain objects and in-memory repos; drives session loop |
| `SimulateCommand.Settings` | (nested) | No flags — kept for Spectre.Console `Command<Settings>` convention |
| `InMemoryGameOperativeStateRepository` | `Console\Infrastructure\Repositories\` | Implements `IGameOperativeStateRepository` with `Dictionary<Guid, GameOperativeState>`; no SQLite dependency |
| `InMemoryActionRepository` | `Console\Infrastructure\Repositories\` | Implements `IActionRepository` as a no-op; satisfies `IActionRepository` without writing to DB |

### 7.2 Reused Classes (unchanged)

| Class | How reused |
|---|---|
| `FightSessionOrchestrator` | Called directly with synthetic `Game`, `TurningPoint`, `Activation`, and in-memory repos |
| `ShootSessionOrchestrator` | Called directly with synthetic `Game`, `TurningPoint`, `Activation`, and in-memory repos |
| `RerollOrchestrator` | CP re-roll suppressed by `CpTeamA/B = 0` on synthetic `Game`; weapon re-rolls work unchanged |
| `CombatResolutionService`, `FightResolutionService` | Injected via DI as normal |
| `IKillTeamRepository` | Used to load both kill teams with their operatives |

### 7.3 Modified Classes

| Class | Change |
|---|---|
| `Program.cs` | Register `SimulateCommand`, `InMemoryGameOperativeStateRepository`, `InMemoryActionRepository` in DI; add `cfg.AddCommand<SimulateCommand>("simulate")` |
| `RerollOrchestrator` | *(Optional)* Add `ApplyWeaponRerollsOnlyAsync(int[] dice, List<WeaponSpecialRule> rules, string operativeName)` — weapon re-rolls without CP step and without `gameId`/`isTeamA` params. Not required if 0-CP suppression is sufficient, but provides a cleaner API surface. |

### 7.4 DI Changes

```csharp
// Program.cs additions — in-memory repos registered as transient (new instance per simulate session)
services.AddTransient<InMemoryGameOperativeStateRepository>();
services.AddTransient<InMemoryActionRepository>();

// Command registration
cfg.AddCommand<SimulateCommand>("simulate")
   .WithDescription("Simulate a fight or shoot encounter (no game required).");
```

`SimulateCommand` resolves `InMemoryGameOperativeStateRepository` and `InMemoryActionRepository` from DI (or constructs them directly with `new`) and passes them to the existing orchestrators per session.

### 7.5 Test Mode Family (Future)

The `simulate` command is the first member of a planned "test mode" family. Future test commands might include:
- `dataslate test counteract` — test counteract timing and APL interactions
- `dataslate test guard` — test guard state and shoot eligibility
- `dataslate test blast` — test blast/torrent multi-target resolution

When more test commands exist, consider grouping under a `dataslate test` parent command with subcommands. For now, `dataslate simulate` remains the entry point for combat testing.

### 7.6 Blast/Torrent Handling

If a selected weapon has Blast or Torrent rules, display a clear error and prompt the user to pick a different weapon:

```
[yellow]⚠ Blast and Torrent weapons are not supported in simulate mode.
   Please select a single-target weapon.[/]
```

### 7.7 Session Loop and Operative Reset

The session loop (Fight again / Shoot / Done) preserves the same two operatives for the entire session. Wound state **resets to full wounds** at the start of each encounter (a new pair of `GameOperativeState` entries is seeded in `InMemoryGameOperativeStateRepository` with `CurrentWounds = operative.Wounds`). This ensures each fight/shoot encounter starts from a clean state.

---

## 8. Acceptance Criteria (BDD Style)

**AC-008-01: Roster-based operative selection**
```
Given the user runs `dataslate simulate`
When the command starts
Then the app shows a SelectionPrompt listing all imported kill teams for "Your kill team"
And a second SelectionPrompt listing all operatives in the chosen team (type to filter)
And a third SelectionPrompt for "Opponent kill team"
And a fourth SelectionPrompt listing all operatives in the opponent team (type to filter)
And no ad-hoc stat entry prompts are shown
```

**AC-008-02: Kill team not found**
```
Given no kill teams have been imported
When the user runs `dataslate simulate`
Then the app prints "[red]No kill teams found. Import a roster first with import-kill-teams.[/]"
And exits with code 1
```

**AC-008-03: Player vs AI fight simulation**
```
Given two operatives have been selected (one from each roster)
When the user selects "Fight" in the session loop
Then the app runs the full alternating Strike/Block loop using FightSessionOrchestrator
And the player controls Strike/Block decisions for their operative
And the AI Advisor (US-009) is available via "? Ask AI Advisor" at each turn
And wound state is tracked in InMemoryGameOperativeStateRepository (no SQLite writes)
And the final damage totals and incapacitation status are displayed
```

**AC-008-04: Shoot simulation in the same session**
```
Given a fight simulation has completed
When the session loop prompt appears
And the user selects "Shoot"
Then the app runs the full shoot resolution using ShootSessionOrchestrator
With the same two operatives already selected
And nothing is written to the SQLite database
```

**AC-008-05: Session loop continues until Done**
```
Given a simulation (fight or shoot) has completed
When the session loop prompt "What next?" appears
And the user selects "Fight again" or "Shoot"
Then the app re-runs the selected encounter with the same operatives
And wound state resets to full wounds for the new encounter
When the user selects "Done"
Then the app returns to the command line
```

**AC-008-06: Blast/Torrent weapon guard**
```
Given the user selects a weapon with the Blast or Torrent special rule during simulation
Then the app displays "[yellow]⚠ Blast/Torrent weapons are not supported in simulate — please select a single-target weapon.[/]"
And returns the user to the weapon selection prompt
```

**AC-008-07: Weapon re-rolls enforced (no CP re-roll)**
```
Given the attacker's weapon has the Balanced special rule
When the attacker's attack dice are entered
Then the app prompts "Balanced: re-roll one attack die? [Y/N]"
And applies the re-roll if Y
And does NOT prompt for a CP re-roll (synthetic game has CpTeamA = 0)
```

**AC-008-08: No data written to SQLite**
```
Given a complete simulate session (operative selection + at least one fight + one shoot)
When the session ends
Then zero rows have been inserted or updated in game_operative_states, actions, activations, turning_points, or games
```

---

## 9. Open Questions

1. **Should simulate support injured state (wounds < half)?** The fight UX already shows injured penalties on weapon labels in the real game. In simulate, `CurrentWounds` starts at full — should the user be able to manually set a starting wound level lower than max to test injured-state interactions? **Recommendation:** no for initial implementation; add a `--wounded` flag or a "starting wounds" prompt in a follow-up.

2. **Should simulate track cumulative stats across "simulate again" runs?** E.g. "Over 10 simulations: avg damage 7.2, max 12, min 3". This would be valuable for statistical testing of weapon effectiveness. **Recommendation:** defer — implement as a `--runs N` batch mode in a future story.

3. **Should `SimulateShootOrchestrator` be merged with `SimulateFightOrchestrator` into a single `SimulateSessionOrchestrator` that selects the sub-flow?** Keeping them separate follows the same separation as the production orchestrators. **Recommendation:** keep separate — single responsibility principle.

4. **Should the simulate command appear in `player list` stats output?** No — simulate is explicitly ephemeral and produces no game history. Stats remain unaffected.

5. **Can the `--roster` flag accept a team name substring (fuzzy match) or must it be exact?** The existing roster commands use case-insensitive exact match. Recommend case-insensitive exact for consistency, with a "Did you mean X?" hint if no exact match but one LIKE match is found.
