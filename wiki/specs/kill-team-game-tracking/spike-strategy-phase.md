# Spike: Strategy Phase

**Status**: Draft  
**Author**: Spike  
**Date**: 2025-07  
**Area**: Kill Team Game Tracking — Strategy Phase

---

## 1. Introduction

The Strategy Phase is the mandatory preamble to every Turning Point's Firefight Phase. It determines
who acts first, awards Command Points to both sides, and gives each player the opportunity to spend
CP on Strategic Gambits (ploys) before any operative moves or shoots. Despite being shorter than the
Firefight Phase, the Strategy Phase involves several branching paths (TP1 vs TP2–4 CP rules,
tie-roll re-rolls, 0CP players who cannot spend ploys) and must be resumable if the app restarts
mid-session.

This spike defines the service-layer design, the Spectre.Console CLI transcript, the domain model
changes, the persistence strategy, and the xUnit test stubs so that a developer can implement the
feature from this document alone.

**Technology stack**: .NET 10, Spectre.Console, SQLite via Microsoft.Data.Sqlite, xUnit.

**Worked example players** (used throughout this document):

| Player | Kill Team | Starting CP |
|---|---|---|
| Michael | Angels of Death | 2 |
| Solomon | Plague Marines | 2 |

---

## 2. Rules Recap

### 2.1 Phase Structure

The Strategy Phase occurs **at the start of each Turning Point**, before the Firefight Phase. It
consists of three sequential steps, always performed in order:

```
Turning Point N begins
  └─ Strategy Phase
       ├─ Step 1: Initiative roll
       ├─ Step 2: CP gain
       └─ Step 3: Strategic Gambits (ploy spending)
  └─ Firefight Phase
```

### 2.2 Step 1 — Initiative

> Both players roll 1D6. The player with the higher result wins initiative for this Turning Point.
> Ties are re-rolled (keep re-rolling until the results differ). The initiative player activates
> first in the Firefight Phase.

### 2.3 Step 2 — CP Gain

CP is awarded depending on which Turning Point is being played:

| Turning Point | Initiative Team | Non-Initiative Team |
|---|---|---|
| TP 1 | +1 CP | +1 CP |
| TP 2 | +1 CP | +2 CP |
| TP 3 | +1 CP | +2 CP |
| TP 4 | +1 CP | +2 CP |

**TP 1 note**: Both teams start the game with 2 CP (set at game creation, US-002). After the TP 1
CP gain, each team has 3 CP entering the Firefight Phase.

**TP 2–4 note**: The non-initiative team receives an extra CP to offset the tactical disadvantage of
activating second. This is the standard "catch-up" mechanism in v3.0.

### 2.4 Step 3 — Strategic Gambits

> Each player may spend 1 CP to play a Strategic Gambit (ploy) with a name and effect. Players may
> play multiple ploys (each costs 1 CP and is resolved one at a time). A player with 0 CP cannot
> play ploys. A player may pass (play no ploys) at any point.

**App policy**: ploys are recorded as free-text (name + optional description) and are **not**
mechanically enforced by the app. They are narrative records only. The app:

1. Deducts 1 CP from the player's total per ploy played.
2. Inserts a row into `ploy_uses` for each ploy.
3. Does **not** validate ploy names against a known list.
4. Does **not** apply ploy effects to game state.

The ploy-entry loop continues for each player until they pass or reach 0 CP.

---

## 3. Service Design

### 3.1 Overview

The Strategy Phase uses the same two-layer pattern as the Fight and Firefight Phase spikes:

- **`StrategyPhaseOrchestrator`** — stateful; owns the Spectre.Console interaction loop. Calls
  `StrategyPhaseService` for all rule calculations and repositories for persistence.
- **`StrategyPhaseService`** — stateless; contains all CP calculation logic. No I/O.

### 3.2 `StrategyPhaseService`

```csharp
public class StrategyPhaseService
{
    /// <summary>
    /// Calculates CP gained by each team at the start of the given turning point.
    /// Returns (initiativeTeamGain, nonInitiativeTeamGain).
    /// </summary>
    public (int InitiativeTeamGain, int NonInitiativeTeamGain) CalculateCpGain(int turningPointNumber)
    {
        return turningPointNumber == 1
            ? (1, 1)
            : (1, 2);
    }

    /// <summary>
    /// Applies CP gain to a game's current CP totals and returns the updated values.
    /// initiativeIsTeamA: true if Team A has initiative this turning point.
    /// </summary>
    public (int NewCpTeamA, int NewCpTeamB) ApplyCpGain(
        int currentCpTeamA,
        int currentCpTeamB,
        int turningPointNumber,
        bool initiativeIsTeamA)
    {
        var (initGain, nonInitGain) = CalculateCpGain(turningPointNumber);

        var cpA = currentCpTeamA + (initiativeIsTeamA ? initGain : nonInitGain);
        var cpB = currentCpTeamB + (initiativeIsTeamA ? nonInitGain : initGain);

        return (cpA, cpB);
    }

    /// <summary>
    /// Returns true if the player is permitted to spend a ploy (i.e. they have at least 1 CP).
    /// </summary>
    public bool CanSpendPloy(int currentCp) => currentCp >= 1;
}
```

### 3.3 `StrategyPhaseOrchestrator`

Stateful; drives the full Strategy Phase UI loop. Injected with `StrategyPhaseService`,
`IGameRepository`, `ITurningPointRepository`, `IPloyRepository`, and `IAnsiConsole`.

```csharp
public class StrategyPhaseOrchestrator
{
    public StrategyPhaseOrchestrator(
        StrategyPhaseService service,
        IGameRepository gameRepository,
        ITurningPointRepository turningPointRepository,
        IPloyRepository ployRepository,
        IAnsiConsole console);

    /// <summary>
    /// Entry point — runs the full Strategy Phase for the given turning point.
    /// Returns the completed StrategyPhaseResult for use by the Firefight Phase.
    /// </summary>
    public Task<StrategyPhaseResult> RunAsync(
        Game game,
        TurningPoint turningPoint,
        KillTeam teamA,
        KillTeam teamB,
        CancellationToken ct = default);

    // --- Private orchestration methods ---

    /// <summary>
    /// Step 1: Prompts both players for their D6 roll. Re-prompts on a tie.
    /// Returns the team that won initiative.
    /// </summary>
    private Task<KillTeam> RunInitiativeStepAsync(
        KillTeam teamA, KillTeam teamB, CancellationToken ct);

    /// <summary>
    /// Step 2: Calculates CP gain, updates Game.CpTeamA / CpTeamB, persists the Game record,
    /// stores the CP snapshot on the TurningPoint record, and returns the updated Game.
    /// </summary>
    private Task<Game> RunCpGainStepAsync(
        Game game,
        TurningPoint turningPoint,
        KillTeam initiativeTeam,
        CancellationToken ct);

    /// <summary>
    /// Step 3: Ploy entry loop for one player. Loops until the player passes or reaches 0 CP.
    /// Deducts CP from the game record and inserts ploy_uses rows for each ploy played.
    /// Returns updated CP total for that team.
    /// </summary>
    private Task<int> RunPloyLoopAsync(
        Game game,
        TurningPoint turningPoint,
        KillTeam team,
        bool isTeamA,
        int currentCp,
        CancellationToken ct);
}
```

### 3.4 Supporting Types

```csharp
/// <summary>
/// Returned by StrategyPhaseOrchestrator.RunAsync(). Passed into the Firefight Phase.
/// </summary>
public record StrategyPhaseResult(
    Guid InitiativeTeamId,
    int FinalCpTeamA,
    int FinalCpTeamB,
    IReadOnlyList<PloyUse> PloyUses
);

/// <summary>
/// In-memory representation of a ploy played during the Strategy Phase.
/// Corresponds to one row in ploy_uses.
/// </summary>
public record PloyUse(
    Guid Id,
    Guid TurningPointId,
    Guid TeamId,
    string PloyName,
    int CpCost,
    string? Description
);
```

---

## 4. CLI Interaction Transcript

**Scenario**: Michael (Angels of Death) and Solomon (Plague Marines) at the start of **Turning
Point 2**. CP before strategy phase: Michael 2 CP, Solomon 1 CP.

Michael rolled 3, Solomon rolled 5. Solomon wins initiative.

Michael plays "Tactical Redeployment" (1 CP). Solomon passes.

---

### Step 1 — Initiative

```
╔══════════════════════════════════════════════════════════════╗
║                  🎲  STRATEGY PHASE  🎲                       ║
║                     Turning Point 2                           ║
╚══════════════════════════════════════════════════════════════╝

──────────────────────────────────────────────────────────────
  Step 1 of 3 — Initiative Roll
──────────────────────────────────────────────────────────────

Both players roll 1D6. Higher result wins initiative.

Michael (Angels of Death) — enter your roll (1–6):
> 3

Solomon (Plague Marines) — enter your roll (1–6):
> 5

  Michael rolled 3.  Solomon rolled 5.

  🎯 Solomon (Plague Marines) wins initiative!
     Solomon activates first in the Firefight Phase.
```

*(Tie example — shown for illustration; does not occur in the main transcript):*

```
  Michael rolled 4.  Solomon rolled 4.

  ⚠ Tie! Both players re-roll.

Michael (Angels of Death) — enter your roll (1–6):
> 2

Solomon (Plague Marines) — enter your roll (1–6):
> 6

  Michael rolled 2.  Solomon rolled 6.

  🎯 Solomon (Plague Marines) wins initiative!
```

---

### Step 2 — CP Gain

```
──────────────────────────────────────────────────────────────
  Step 2 of 3 — Command Points
──────────────────────────────────────────────────────────────

  Turning Point 2: non-initiative team gains +2 CP; initiative team gains +1 CP.

  Before:  Michael (Angels of Death) [2CP]   Solomon (Plague Marines) [1CP]

  Solomon  (initiative)      +1 CP  →  2 CP
  Michael  (non-initiative)  +2 CP  →  4 CP

  After:   Michael (Angels of Death) [4CP]   Solomon (Plague Marines) [2CP]
```

---

### Step 3 — Strategic Gambits

Players are prompted in order: **non-initiative player first**, then the initiative player. This
mirrors typical tabletop convention — the initiative player seeing the opponent's ploys before
deciding their own.

```
──────────────────────────────────────────────────────────────
  Step 3 of 3 — Strategic Gambits (Ploys)
──────────────────────────────────────────────────────────────

  Michael (Angels of Death) [4CP] — play a Strategic Gambit? (costs 1 CP each)

  > Play a Strategic Gambit (1 CP)
    Pass — no ploys this turn

> Play a Strategic Gambit (1 CP)

  Ploy name:
> Tactical Redeployment

  Description (optional — press Enter to skip):
> Allows one operative to change their order token before activating.

  ✓ "Tactical Redeployment" recorded.  Michael [3CP]

  Michael (Angels of Death) [3CP] — play another Strategic Gambit?

  > Play a Strategic Gambit (1 CP)
    Pass — no more ploys

> Pass — no more ploys

──────────────────────────────────────────────────────────────

  Solomon (Plague Marines) [2CP] — play a Strategic Gambit? (costs 1 CP each)

  > Play a Strategic Gambit (1 CP)
    Pass — no ploys this turn

> Pass — no ploys this turn
```

---

### Strategy Phase Summary

```
──────────────────────────────────────────────────────────────
  Strategy Phase Complete — Turning Point 2
──────────────────────────────────────────────────────────────

  Initiative:  Solomon (Plague Marines)

  CP Summary:
    Michael (Angels of Death)   [3CP]
    Solomon (Plague Marines)    [2CP]

  Ploys played:
    Michael: "Tactical Redeployment"  (1 CP)
    Solomon: (none)

  Press Enter to begin the Firefight Phase.
```

---

## 5. Domain Changes

### 5.1 `ploy_uses` Table (Migration 002)

The `ploy_uses` table is introduced in **Migration 002**, already stubbed in `spike-schema-ddl.md`
(section 4). The full DDL for Migration 002 is:

```sql
-- ─────────────────────────────────────────────────────────────────────────────
-- Migration 002: Strategy Phase — ploy tracking
-- ─────────────────────────────────────────────────────────────────────────────

CREATE TABLE IF NOT EXISTS ploy_uses (
    id               TEXT    PRIMARY KEY,
    turning_point_id TEXT    NOT NULL REFERENCES turning_points (id) ON DELETE CASCADE,
    team_id          TEXT    NOT NULL REFERENCES kill_teams (id),
    ploy_name        TEXT    NOT NULL,
    cp_cost          INTEGER NOT NULL DEFAULT 1,
    description      TEXT    NULL
    -- description is optional free text; NULL if player skipped the field
);

CREATE INDEX IF NOT EXISTS idx_ploy_uses_turning_point
    ON ploy_uses (turning_point_id, team_id);
```

**Changes vs. the stub in `spike-schema-ddl.md`**: the `description TEXT NULL` column is added. The
stub in Migration 002 used only `ploy_name` and `cp_cost`; this spike adds `description` for
narrative context.

The migration is registered in `Migrations.All`:

```csharp
internal static readonly IReadOnlyList<(int Version, string Sql)> All = new[]
{
    (1, Migration_001),
    (2, Migration_002),   // Strategy Phase — ploy tracking
};
```

### 5.2 `turning_points` Table — `is_strategy_phase_complete` Column

Migration 002 also adds the resume flag to `turning_points`:

```sql
-- (appended to Migration 002)

ALTER TABLE turning_points
    ADD COLUMN is_strategy_phase_complete INTEGER NOT NULL DEFAULT 0;
```

Full column list for `turning_points` after Migration 002:

| Column | Type | Notes |
|---|---|---|
| `id` | TEXT PK | GUID |
| `game_id` | TEXT FK → games | |
| `number` | INTEGER | CHECK BETWEEN 1 AND 4 |
| `team_with_initiative_id` | TEXT FK → kill_teams | Set at end of Step 1 |
| `cp_team_a` | INTEGER | CP snapshot **after** Step 2 |
| `cp_team_b` | INTEGER | CP snapshot **after** Step 2 |
| `is_strategy_phase_complete` | INTEGER | 0 = not done; 1 = complete |

### 5.3 `games` Table — CP Columns

CP is the live, mutable counter tracked on `games`. The schema already includes `cp_team_a` and
`cp_team_b` as inferred columns; this spike confirms their DDL. If they are absent from Migration
001, they must be added in Migration 002:

```sql
-- (appended to Migration 002, only if not already in Migration 001)

ALTER TABLE games ADD COLUMN cp_team_a INTEGER NOT NULL DEFAULT 2;
ALTER TABLE games ADD COLUMN cp_team_b INTEGER NOT NULL DEFAULT 2;
```

> **Source of truth**: `games.cp_team_a` / `games.cp_team_b` hold the **current** CP for each
> team, updated in real time as ploys are spent. `turning_points.cp_team_a` /
> `turning_points.cp_team_b` hold a **snapshot** taken at the end of Step 2 (after CP gain, before
> ploy spending) — useful for post-game review and resume detection.

### 5.4 `IPloyRepository`

```csharp
public interface IPloyRepository
{
    /// <summary>
    /// Inserts a new ploy_uses row. The ploy's id is set by the caller (GUID).
    /// </summary>
    Task AddAsync(PloyUse ploy, CancellationToken ct = default);

    /// <summary>
    /// Returns all ploys played in the given turning point, ordered by insert time.
    /// </summary>
    Task<IReadOnlyList<PloyUse>> GetByTurningPointAsync(
        Guid turningPointId, CancellationToken ct = default);

    /// <summary>
    /// Returns ploys played by a specific team in the given turning point.
    /// </summary>
    Task<IReadOnlyList<PloyUse>> GetByTurningPointAndTeamAsync(
        Guid turningPointId, Guid teamId, CancellationToken ct = default);
}
```

### 5.5 `ITurningPointRepository` — Additional Methods

The existing `ITurningPointRepository` gains two new methods to support Strategy Phase state:

```csharp
/// <summary>
/// Updates team_with_initiative_id, cp_team_a, cp_team_b, and
/// is_strategy_phase_complete = 1 in a single atomic transaction.
/// Called at the end of the Strategy Phase once all three steps are done.
/// </summary>
Task CompleteStrategyPhaseAsync(
    Guid turningPointId,
    Guid initiativeTeamId,
    int cpSnapshotTeamA,
    int cpSnapshotTeamB,
    CancellationToken ct = default);

/// <summary>
/// Returns true if is_strategy_phase_complete = 1 for the given turning point.
/// Used on resume to determine whether to re-run or skip the Strategy Phase.
/// </summary>
Task<bool> IsStrategyPhaseCompleteAsync(
    Guid turningPointId, CancellationToken ct = default);
```

### 5.6 `IGameRepository` — CP Update Method

The existing `IGameRepository` gains a focused method for CP updates:

```csharp
/// <summary>
/// Updates cp_team_a and cp_team_b on the games row.
/// Called after CP gain (Step 2) and after each ploy spend (Step 3).
/// </summary>
Task UpdateCpAsync(
    Guid gameId,
    int cpTeamA,
    int cpTeamB,
    CancellationToken ct = default);
```

---

## 6. CP Display Format

CP is shown inline wherever a team or player is named during the Strategy Phase. The format is:

```
PlayerName (TeamName) [NCP]
```

Examples:

```
Michael (Angels of Death) [3CP]
Solomon (Plague Marines)  [2CP]
```

The CP count updates in real time in the ploy loop — after each ploy is confirmed the revised CP
total is shown before the "play another?" prompt appears.

**Colour conventions** for CP display:

| CP Value | Colour |
|---|---|
| ≥ 3 CP | Default (white) |
| 2 CP | `[yellow]` — low CP caution |
| 1 CP | `[yellow]` — only one ploy possible |
| 0 CP | `[red]` — cannot play ploys |

Example using Spectre.Console markup:

```csharp
private string FormatCp(int cp) => cp switch
{
    0 => $"[red][{cp}CP][/]",
    1 or 2 => $"[yellow][{cp}CP][/]",
    _ => $"[{cp}CP]"
};

// "Michael (Angels of Death) [3CP]"  — white
// "Solomon (Plague Marines) [yellow][2CP][/]"  — yellow
```

This markup is produced by a private helper on `StrategyPhaseOrchestrator` and reused at every
point where CP is displayed.

**Board-level display**: at the top of the Firefight Phase (after Strategy Phase completes) the
board header should show both teams' CP. The `StrategyPhaseResult` provides `FinalCpTeamA` and
`FinalCpTeamB` for this purpose.

---

## 7. Resume Handling

If the app is closed mid-Strategy-Phase and restarted, the `TurningPointCoordinator` (the outer
controller that orchestrates the TP lifecycle) checks resume state as follows:

### 7.1 Resume Detection Logic

```
On app start → load in-progress game → load current TurningPoint
│
├─ TurningPoint does not exist?
│    → Create new TurningPoint row; run Strategy Phase from beginning.
│
├─ TurningPoint.is_strategy_phase_complete = 0?
│    → Strategy Phase was interrupted; restart it from the beginning.
│      (CP gain and any ploys will be re-entered. The existing TurningPoint row
│       is reused; its initiative and CP snapshot columns are overwritten.)
│
└─ TurningPoint.is_strategy_phase_complete = 1?
     → Strategy Phase already done; skip directly to Firefight Phase.
       Read initiative from TurningPoint.team_with_initiative_id.
       Read current CP from Game.cp_team_a / Game.cp_team_b.
```

### 7.2 Re-Entry Banner

When the Strategy Phase is restarted after an interrupted session, the orchestrator displays a
contextual notice:

```
⚠  Resuming Turning Point 2 — Strategy Phase was not completed.
   CP totals have been reset to their pre-strategy-phase values.
   Please re-enter the initiative rolls and ploys.
```

> **CP reset on restart**: because the Strategy Phase commits CP atomically only at the end of
> Step 3 (or more precisely, CP is updated live and the `is_strategy_phase_complete` flag is set in
> the same transaction), a restart before completion means `Game.cp_team_a` / `cp_team_b` may be
> partially updated. The safest recovery is to reload the CP snapshot from the **previous**
> TurningPoint (or from game start for TP1) and re-run the full Strategy Phase.
>
> **Implementation note**: the `TurningPointCoordinator` stores the pre-strategy-phase CP baseline
> (the `Game.cp_team_a` / `cp_team_b` values at the moment the TurningPoint was created) in the
> TurningPoint's `cp_team_a` / `cp_team_b` columns only **after** committing Step 2. Before that
> commit, those columns hold `0`. On resume, if `is_strategy_phase_complete = 0`, the coordinator
> reads CP from the previous TP's snapshot (or game defaults for TP1) and restores `Game.cp_team_a`
> / `cp_team_b` to that baseline before handing control to `StrategyPhaseOrchestrator`.

### 7.3 Atomicity of `is_strategy_phase_complete`

The flag is set to `1` in the same SQLite transaction that:

1. Writes the final `Game.cp_team_a` / `cp_team_b` (after all ploys).
2. Writes `TurningPoint.cp_team_a` / `cp_team_b` (the snapshot).
3. Writes `TurningPoint.team_with_initiative_id`.

All four writes happen in a single `BeginTransaction` block. If any write fails, the transaction is
rolled back and `is_strategy_phase_complete` remains `0`. The app will re-run the Strategy Phase on
next launch.

---

## 8. Edge Cases

### 8.1 Initiative Tie — Re-Roll Loop

When both players roll the same value the tie is displayed and both players are prompted again
immediately. This loops until the results differ.

```
  Michael rolled 3.  Solomon rolled 3.
  ⚠ Tie! Both players re-roll.

Michael (Angels of Death) — enter your roll (1–6):
> 5

Solomon (Plague Marines) — enter your roll (1–6):
> 5

  ⚠ Tie! Both players re-roll.

Michael (Angels of Death) — enter your roll (1–6):
> 2

Solomon (Plague Marines) — enter your roll (1–6):
> 6

  🎯 Solomon (Plague Marines) wins initiative!
```

The loop is unbounded in code — it exits only when results differ. Input validation still rejects
values outside 1–6.

### 8.2 Player Has 0 CP — Cannot Play Ploys

If a player reaches 0 CP during the ploy loop, the "Play a Strategic Gambit" option is removed from
the selection prompt entirely. The prompt collapses to a single informational panel followed by an
automatic pass:

```
  Michael (Angels of Death) [red][0CP][/] — no Command Points remaining.
  Passing automatically.
```

`StrategyPhaseService.CanSpendPloy(0)` returns `false`; the orchestrator checks this before showing
the prompt and skips to the summary if false.

### 8.3 TP1 vs TP2–4 CP Rules

`StrategyPhaseService.CalculateCpGain(1)` returns `(1, 1)`. All other TP numbers return `(1, 2)`.
The orchestrator displays which rule set applies:

- TP1: `"Turning Point 1: both teams gain +1 CP."`
- TP2–4: `"Turning Point N: non-initiative team gains +2 CP; initiative team gains +1 CP."`

No special logic is needed beyond the conditional; the service method encapsulates the branch.

### 8.4 Multiple Ploys in One Turn

A player may play any number of ploys in sequence, spending 1 CP each. The loop prompts "play
another?" after each ploy until the player passes or reaches 0 CP. Each ploy inserts a separate
`ploy_uses` row and decrements `Game.cp_team_a` (or `cp_team_b`) immediately. The game record is
persisted after every CP change to ensure consistency on unexpected exit.

### 8.5 Ploy With No Description

The description field is optional. If the player presses Enter at the description prompt without
typing anything, `description = null` is stored in `ploy_uses`. The confirmation line omits the
description:

```
  ✓ "Rapid Assault" recorded.  Michael [2CP]
```

vs. with description:

```
  ✓ "Rapid Assault" recorded.  Michael [2CP]
     → "Assault Intercessor Grenadier may move before activating."
```

### 8.6 No Ploys Played by Either Team

If both players pass without playing any ploys, `ploy_uses` receives no rows for that turning point.
The summary still shows `(none)` for each team's ploy list. This is valid and expected — particularly
in CP-tight situations.

---

## 9. xUnit Tests

All tests live in `KillTeamAgent.Tests`. Test class: `StrategyPhaseServiceTests` for service-layer
unit tests; `StrategyPhaseIntegrationTests` for repository/persistence tests. Naming follows
`ClassName_Scenario_ExpectedResult`.

```csharp
// ─── StrategyPhaseService — CP calculation ───────────────────────────────────

[Fact]
public void StrategyPhaseService_CalculateCpGain_Tp1_ReturnsBothPlusOne()
{
    // Arrange
    var service = new StrategyPhaseService();

    // Act
    var (initGain, nonInitGain) = service.CalculateCpGain(turningPointNumber: 1);

    // Assert
    // initGain must equal 1
    // nonInitGain must equal 1
}

[Fact]
public void StrategyPhaseService_ApplyCpGain_Tp2_InitiativeTeam_GainsOneCp()
{
    // Arrange
    var service = new StrategyPhaseService();
    // TeamA has initiative; TeamA starts with 1CP, TeamB starts with 2CP.

    // Act
    var (newA, newB) = service.ApplyCpGain(
        currentCpTeamA: 1,
        currentCpTeamB: 2,
        turningPointNumber: 2,
        initiativeIsTeamA: true);

    // Assert
    // newA must equal 2  (1 + 1 initiative gain)
    // newB must equal 4  (2 + 2 non-initiative gain)
}

[Fact]
public void StrategyPhaseService_ApplyCpGain_Tp2_NonInitiativeTeam_GainsTwoCp()
{
    // Arrange
    var service = new StrategyPhaseService();
    // TeamB has initiative; TeamA starts with 2CP, TeamB starts with 1CP.

    // Act
    var (newA, newB) = service.ApplyCpGain(
        currentCpTeamA: 2,
        currentCpTeamB: 1,
        turningPointNumber: 2,
        initiativeIsTeamA: false);

    // Assert
    // newA must equal 4  (2 + 2 non-initiative gain)
    // newB must equal 2  (1 + 1 initiative gain)
}

// ─── PloyRepository — persistence ───────────────────────────────────────────

[Fact]
public async Task PloyRepository_Add_PloysRecordedForTurningPoint()
{
    // Arrange — seed game, team, and turning point via TestDbBuilder.
    using var db = TestDbBuilder.Create()
        .WithPlayer(michaelId, "Michael")
        .WithKillTeam(teamAId, "Angels of Death", "Adeptus Astartes")
        .WithGame(gameId, teamAId, teamBId, michaelId, solomonId)
        .WithTurningPoint(tp2Id, gameId, number: 2);

    var repo = new PloyRepository(db.Connection);
    var ploy = new PloyUse(
        Id:              Guid.NewGuid(),
        TurningPointId:  tp2Id,
        TeamId:          teamAId,
        PloyName:        "Tactical Redeployment",
        CpCost:          1,
        Description:     "Allows one operative to change their order token.");

    // Act
    await repo.AddAsync(ploy);
    var results = await repo.GetByTurningPointAsync(tp2Id);

    // Assert
    // results.Count must equal 1
    // results[0].PloyName must equal "Tactical Redeployment"
    // results[0].CpCost must equal 1
    // results[0].TeamId must equal teamAId
}

// ─── StrategyPhaseService — 0CP guard ────────────────────────────────────────

[Fact]
public void StrategyPhaseService_CanSpendPloy_ZeroCp_ReturnsFalse()
{
    // Arrange
    var service = new StrategyPhaseService();

    // Act
    var canSpend = service.CanSpendPloy(currentCp: 0);

    // Assert
    // canSpend must be false
}

// ─── Resume detection ────────────────────────────────────────────────────────

[Fact]
public async Task TurningPointRepository_IsStrategyPhaseComplete_BeforeCompletion_ReturnsFalse()
{
    // Arrange — seed a TurningPoint with is_strategy_phase_complete = 0 (the default).
    using var db = TestDbBuilder.Create()
        .WithPlayer(michaelId, "Michael")
        .WithKillTeam(teamAId, "Angels of Death", "Adeptus Astartes")
        .WithGame(gameId, teamAId, teamBId, michaelId, solomonId)
        .WithTurningPoint(tp2Id, gameId, number: 2);
        // is_strategy_phase_complete defaults to 0

    var repo = new TurningPointRepository(db.Connection);

    // Act
    var isComplete = await repo.IsStrategyPhaseCompleteAsync(tp2Id);

    // Assert
    // isComplete must be false
}

[Fact]
public async Task TurningPointRepository_CompleteStrategyPhase_SetsFlag()
{
    // Arrange
    using var db = TestDbBuilder.Create()
        .WithPlayer(michaelId, "Michael")
        .WithKillTeam(teamAId, "Angels of Death", "Adeptus Astartes")
        .WithGame(gameId, teamAId, teamBId, michaelId, solomonId)
        .WithTurningPoint(tp2Id, gameId, number: 2);

    var repo = new TurningPointRepository(db.Connection);

    // Act
    await repo.CompleteStrategyPhaseAsync(
        turningPointId:  tp2Id,
        initiativeTeamId: teamBId,   // Solomon won initiative
        cpSnapshotTeamA:  4,
        cpSnapshotTeamB:  2);

    var isComplete = await repo.IsStrategyPhaseCompleteAsync(tp2Id);

    // Assert
    // isComplete must be true
    // SELECT team_with_initiative_id, cp_team_a, cp_team_b FROM turning_points WHERE id = tp2Id
    //   team_with_initiative_id must equal teamBId.ToString()
    //   cp_team_a must equal 4
    //   cp_team_b must equal 2
}
```

---

## 10. Persistence

### 10.1 Write Order

The `StrategyPhaseOrchestrator` performs writes in the following sequence:

| Step | What is written | Where |
|---|---|---|
| Step 1 complete | `TurningPoint.team_with_initiative_id` | `turning_points` (preliminary, single-column UPDATE) |
| Step 2 complete | `Game.cp_team_a`, `Game.cp_team_b` | `games` via `IGameRepository.UpdateCpAsync` |
| Step 3 — each ploy | `PloyUse` row; `Game.cp_team_a` or `cp_team_b` decremented | `ploy_uses` + `games` (two statements, one transaction) |
| Step 3 complete | `TurningPoint.cp_team_a`, `TurningPoint.cp_team_b`, `TurningPoint.is_strategy_phase_complete = 1` | `turning_points` via `ITurningPointRepository.CompleteStrategyPhaseAsync` |

### 10.2 Transaction Boundaries

Each ploy spend (one `ploy_uses` INSERT + one `games` UPDATE) is wrapped in its own transaction.
The final commit that sets `is_strategy_phase_complete = 1` combines the CP snapshot write and the
flag in a single transaction (see Section 7.3).

### 10.3 `TestDbBuilder` Extension

A new `WithTurningPoint` overload supports seeding the Strategy Phase flag:

```csharp
public TestDbBuilder WithTurningPoint(
    Guid id,
    Guid gameId,
    int number,
    bool strategyPhaseComplete = false)
{
    Exec("""
        INSERT INTO turning_points
            (id, game_id, number, team_with_initiative_id, is_strategy_phase_complete)
        SELECT @id, @gid, @num, team_a_id, @spc FROM games WHERE id = @gid
        """,
        ("@id",  id.ToString()),
        ("@gid", gameId.ToString()),
        ("@num", number),
        ("@spc", strategyPhaseComplete ? 1 : 0));
    return this;
}
```

### 10.4 Full Persistence Summary

```
After TP2 Strategy Phase — Michael 3CP, Solomon 2CP, Michael played one ploy:

games:
  cp_team_a = 3        ← 2 (start) + 2 (non-init gain) − 1 (ploy) = 3
  cp_team_b = 2        ← 1 (start) + 1 (init gain) = 2

turning_points (TP2):
  team_with_initiative_id = Solomon's kill_team id
  cp_team_a               = 4   ← snapshot after Step 2, before ploy
  cp_team_b               = 2   ← snapshot after Step 2
  is_strategy_phase_complete = 1

ploy_uses:
  id               = <new GUID>
  turning_point_id = TP2 id
  team_id          = Angels of Death id
  ploy_name        = "Tactical Redeployment"
  cp_cost          = 1
  description      = "Allows one operative to change their order token before activating."
```

---

## Open Questions

1. ~~**Ploy ordering in the ploy loop**: the transcript shows non-initiative player goes first in
   Step 3. Is this the official v3.0 rule, or does the initiative player go first? Confirm and
   update the orchestrator accordingly.~~
   **Resolved**: Non-initiative player records ploys first, then initiative player. This matches the
   transcript in §4 and is now canonical. The `StrategyPhaseOrchestrator` ploy loop must call
   `RunPloyLoopAsync` for the non-initiative team first, i.e. the call order is
   `[nonInitiativeTeam, initiativeTeam]`. The game design rationale: knowing the non-initiative
   player's ploys before committing your own gives initiative a slight advantage, but this order is
   the standard competitive KT convention.

2. **CP column in `games` (Migration 001 vs 002)**: the schema spike (`spike-schema-ddl.md`) does
   not explicitly list `cp_team_a` / `cp_team_b` on the `games` table DDL. Confirm whether these
   columns are added in Migration 001 (alongside the `games` table) or deferred to Migration 002.
   The DDL above assumes Migration 002; move the `ALTER TABLE` statements to Migration 001 if
   preferred.

3. **Initiative column written in Step 1 vs end of phase**: writing `team_with_initiative_id`
   immediately after Step 1 (before Step 2) gives the Firefight Phase something to read on a
   mid-Step-2 crash. However it requires two separate UPDATE statements (once after Step 1, once
   at end of Step 3). An alternative is to defer all writes to the final atomic commit. Decide
   based on how important crash-safe initiative recording is in practice.

4. **CP floor**: can CP go negative? The current design deducts 1 CP per ploy and `CanSpendPloy`
   blocks spending when at 0. Confirm there is no rule or ploy that could cause CP to go below 0.

5. **Ploy effects on game state**: this spike explicitly defers ploy enforcement to the player. If
   a future sprint adds mechanical ploy effects (e.g. modifying `apl_modifier` on an operative),
   the `ploy_uses` table will need a `ploy_type` column and a dispatch mechanism. Flag for backlog.
