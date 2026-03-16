# Spec: Combat Event Stream

Last updated: 2026-07-15

## Introduction

Refactor the combat orchestrators (Fight, Shoot, Blast/Torrent) to emit structured events during execution rather than writing directly to the console. A renderer consumes the event stream to produce console output, enabling:

- Clear labelling of actions by participant (`[You]` / `[AI]`)
- Decoupled presentation from game logic
- Persistent event log for encounter replay and history
- Foundation for a future AI narrative layer (separate spec)
- Easier testing — assert on events, not console output

Currently, orchestrators interleave game logic with `console.MarkupLine()` calls. This makes it impossible to distinguish who performed an action, replay an encounter, or swap the rendering strategy.

## Goals

- Emit a structured event for every meaningful combat step (target selection, weapon choice, dice rolls, rerolls, strikes, blocks, damage, incapacitation)
- Render all combat output from events rather than inline `console.MarkupLine()` calls
- Label output with participant context (`[You]` / `[AI]`) in simulate mode
- Persist events to SQLite for encounter replay (optional, alongside in-memory rendering)
- Cover all three combat types: Fight, Shoot, Blast/Torrent
- Maintain existing game logic and resolution services unchanged

## Non-Goals

- AI-generated narrative text (separate spec)
- Changes to dice resolution logic (`FightResolutionService`, `CombatResolutionService`)
- Changes to the reroll mechanics logic
- UI beyond CLI (no web/GUI replay viewer)
- Changes to the strategy phase or firefight phase orchestration (but see TC-7 for integration notes)
- Replay command implementation (persistence enables it; the command is future work)
- Refactoring `SimulateSessionOrchestrator`'s own output (matchup tables, session headers, mode messages) to events — these remain direct console writes

## User Stories

### US-001: Define Combat Event Types (Domain)

**Description:** As a developer, I need a set of event record types that capture every combat step so orchestrators can emit structured data instead of writing to the console.

**Workstream:** `backend-dotnet`

**Agent routing hint:** Requires .NET 10 record types in Domain project. No external dependencies.

**Acceptance Criteria:**
- [ ] Create `CombatEvent` base record with `Timestamp`, `EncounterId`, `SequenceNumber`, `Participant` (string — team ID, mapped to display label by renderer)
- [ ] Define event types covering the full combat lifecycle (see FR-1 for complete list)
- [ ] Events are immutable records — no mutable state
- [ ] `CombatEventStream` class collects events in order, exposes `IReadOnlyList<CombatEvent>`, supports `OnEventEmitted` callback for incremental rendering
- [ ] Each event carries enough context for rendering without looking up external state (operative names, weapon names, values — not just IDs)
- [ ] All existing tests pass unchanged

**Quality Gates:**
```
dotnet build KillTeam.DataSlate.Domain/KillTeam.DataSlate.Domain.csproj
dotnet test KillTeam.DataSlate.Tests --filter "FullyQualifiedName~Domain"
```

**Technical Considerations:**
- Place in `KillTeam.DataSlate.Domain/Events/`
- Use C# records for immutability: `public sealed record TargetSelectedEvent(...) : CombatEvent`
- `Participant` is `string` (team ID), not an enum — the renderer maps team ID → display label (`[You]`/`[AI]`/player name)
- `CombatEventStream` is the collection wrapper — passed into orchestrators, appended to during execution

---

### US-002: Refactor FightSessionOrchestrator to Emit Events

**Description:** As a developer, I need the fight orchestrator to emit events instead of writing to the console so combat output can be rendered from the event stream.

**Workstream:** `backend-dotnet`

**Agent routing hint:** Requires .NET 10, Spectre.Console knowledge, familiarity with FightSessionOrchestrator fight loop.

**Acceptance Criteria:**
- [ ] FightSessionOrchestrator accepts a `CombatEventStream` parameter
- [ ] Every `console.MarkupLine()` call in FightSessionOrchestrator is replaced with an event emission
- [ ] Events emitted: `TargetSelectedEvent`, `WeaponSelectedEvent` (attacker + defender), `FightAssistSetEvent`, `DiceRolledEvent` (attack + defence), `DiceClassifiedEvent`, `ShockAppliedEvent`, `StrikePerformedEvent`, `BlockPerformedEvent`, `WoundsUpdatedEvent`, `IncapacitationEvent`, `FightPoolStateEvent`, `FightResolvedEvent`
- [ ] Error/warning paths emit `CombatWarningEvent`: no valid fight targets, no melee weapons, target not found
- [ ] `WeaponSelectedEvent` carries injured status when applicable (effective Hit threshold)
- [ ] Interactive prompts (weapon selection, dice entry, action selection) remain as direct console calls — only output/display is event-driven
- [ ] `CombatEventRenderer` renders fight events to console with same visual quality as current output
- [ ] `DisplayFightPools` table rendering moves to the renderer
- [ ] All existing fight-related tests pass unchanged
- [ ] New unit tests verify correct events are emitted for a fight sequence

**Quality Gates:**
```
dotnet build KillTeam.DataSlate.Console/KillTeam.DataSlate.Console.csproj
dotnet test KillTeam.DataSlate.Tests --verbosity quiet
```

**Technical Considerations:**
- The fight loop (`while atkPool.Remaining.Count > 0 || defPool.Remaining.Count > 0`) stays in the orchestrator — it emits `StrikePerformedEvent` / `BlockPerformedEvent` per iteration
- `FormatFightAction` and `DisplayFightPools` logic moves to `CombatEventRenderer`
- After the loop, emit `FightResolvedEvent` with summary totals
- Keep `GameAction` persistence — it's the summary record; events are the detailed log

---

### US-003: Refactor ShootSessionOrchestrator to Emit Events

**Description:** As a developer, I need the shoot orchestrator to emit events so ranged combat output flows through the event stream.

**Workstream:** `backend-dotnet`

**Agent routing hint:** Requires .NET 10, Spectre.Console, familiarity with ShootSessionOrchestrator and CombatResolutionService.

**Acceptance Criteria:**
- [ ] ShootSessionOrchestrator accepts a `CombatEventStream` parameter
- [ ] Events emitted: `TargetSelectedEvent`, `WeaponSelectedEvent`, `CoverStatusEvent`, `CoverSaveNotificationEvent`, `FightAssistSetEvent`, `DiceRolledEvent` (attack + defence), `ShootResolvedEvent` (hits/blocks/damage breakdown), `WoundsUpdatedEvent`, `IncapacitationEvent` (with cause — fight/shoot/self-damage), `StunAppliedEvent`, `HotDamageEvent`
- [ ] Error/warning paths emit `CombatWarningEvent`: no valid targets, no ranged weapons, target not found
- [ ] Interactive prompts remain as direct console calls
- [ ] `CombatEventRenderer` renders shoot events with same visual quality
- [ ] Existing tests pass; new tests verify event emission

**Quality Gates:**
```
dotnet build KillTeam.DataSlate.Console/KillTeam.DataSlate.Console.csproj
dotnet test KillTeam.DataSlate.Tests --verbosity quiet
```

**Technical Considerations:**
- The shoot result table (Unblocked Crits, Normals, Total Damage) becomes a `ShootResolvedEvent`
- Cover/obscured status is captured in `CoverStatusEvent`; the "+1 cover save auto-add" message becomes `CoverSaveNotificationEvent`
- Stun and Hot effects each get their own event type
- `IncapacitationEvent` must distinguish target-incap from self-incap (HOT self-damage) via the `Cause` field
- Reroll events (from `RerollOrchestrator`) are a stretch goal — can be added later

---

### US-004: Refactor BlastTorrentSessionOrchestrator to Emit Events

**Description:** As a developer, I need the blast/torrent orchestrator to emit events so multi-target ranged combat flows through the event stream.

**Workstream:** `backend-dotnet`

**Agent routing hint:** Requires .NET 10, Spectre.Console, familiarity with BlastTorrentSessionOrchestrator.

**Acceptance Criteria:**
- [ ] BlastTorrentSessionOrchestrator accepts a `CombatEventStream` parameter
- [ ] Events emitted: `MultiTargetDeclaredEvent`, `TargetSelectedEvent` (per target), `TargetResolutionStartEvent` (per-target header), `CoverStatusEvent` (per target), `DiceRolledEvent` (shared attack + per-target defence), `ShootResolvedEvent` (per target), `WoundsUpdatedEvent` (per target), `IncapacitationEvent` (per target)
- [ ] Friendly fire warning is an event (`FriendlyFireWarningEvent`)
- [ ] Friendly fire abort (user says no) is an event (`CombatAbortedEvent`)
- [ ] Existing tests pass; new tests verify event emission

**Quality Gates:**
```
dotnet build KillTeam.DataSlate.Console/KillTeam.DataSlate.Console.csproj
dotnet test KillTeam.DataSlate.Tests --verbosity quiet
```

**Technical Considerations:**
- Blast/Torrent shares attack dice across targets — `DiceRolledEvent` emitted once for attack, once per target for defence
- Each target's resolution is a separate `ShootResolvedEvent` with the target's name
- The "This will affect N friendly operative(s)" warning becomes `FriendlyFireWarningEvent`
- Per-target header separators (the `Rule` rendering) become `TargetResolutionStartEvent` with target name and wounds

---

### US-005: Combat Event Renderer with Participant Labels

**Description:** As a player, I want combat output prefixed with `[You]` / `[AI]` labels so I can clearly follow who is doing what during simulate mode.

**Workstream:** `backend-dotnet`

**Agent routing hint:** Requires .NET 10, Spectre.Console markup rendering.

**Acceptance Criteria:**
- [ ] `CombatEventRenderer` class consumes `CombatEvent` list and renders to `IAnsiConsole`
- [ ] In simulate mode, output lines are prefixed with `[You]` or `[AI]` based on the event's participant
- [ ] In full game mode (firefight phase), participant labels use player names instead
- [ ] Dice rolls display as: `[You] Rolled: 6, 6, 2, 3, 2`
- [ ] Weapon selection displays as: `[You] Auto-selected melee weapon: Power Fist (Attack: 5 | ...)`
- [ ] Target selection displays as: `[You] Target: Plague Marine Champion (Wounds: 15/15)`
- [ ] Fight pool tables and shoot result tables render from events with same visual quality
- [ ] `DisplayEncounterSummary` renders from `FightResolvedEvent` / `ShootResolvedEvent` (currently asymmetric — fight uses `DisplayEncounterSummary` while shoot uses inline MarkupLine; both must use renderer)
- [ ] Events can be rendered incrementally (one at a time during the fight loop) or as a batch (after resolution)

**Quality Gates:**
```
dotnet build KillTeam.DataSlate.Console/KillTeam.DataSlate.Console.csproj
dotnet test KillTeam.DataSlate.Tests --verbosity quiet
```

**Technical Considerations:**
- Renderer receives participant mapping (which team ID = "You", which = "AI") from SimulateSessionOrchestrator
- For firefight phase, mapping comes from game participants
- Use pattern matching on event types: `switch (evt) { case TargetSelectedEvent t => ..., case DiceRolledEvent d => ... }`
- Green values for stats: `Attack: [green]5[/]` pattern continues

---

### US-006: Persist Combat Events to SQLite

**Description:** As a developer, I need combat events persisted to SQLite so encounters can be replayed or reviewed later.

**Workstream:** `backend-dotnet`

**Agent routing hint:** Requires .NET 10, Microsoft.Data.Sqlite, schema migration.

**Acceptance Criteria:**
- [ ] New `combat_events` table: `id, encounter_id, sequence_number, event_type, event_data_json, created_at`
- [ ] `encounter_id` is a separate GUID generated per encounter session (not tied to `GameAction.Id`)
- [ ] `ICombatEventRepository` with `SaveAsync(encounterId, IReadOnlyList<CombatEvent>)` and `GetByEncounterAsync(encounterId)`
- [ ] Events serialised as JSON in `event_data_json` column (using `System.Text.Json`)
- [ ] Schema migration added to `DatabaseInitialiser`
- [ ] In-memory implementation for simulate mode (no persistence by default)
- [ ] Persistence is opt-in: `--save-events` flag or similar mechanism
- [ ] Roundtrip test: emit events → persist → load → verify identical

**Quality Gates:**
```
dotnet build KillTeam.DataSlate.Console/KillTeam.DataSlate.Console.csproj
dotnet test KillTeam.DataSlate.Tests --verbosity quiet
```

**Technical Considerations:**
- JSON serialisation with `System.Text.Json` using discriminated union pattern for event types
- `encounter_id` is a standalone GUID — not linked to `GameAction.Id`. In real games, the encounter can reference a `GameAction`; in simulate mode, it is transient
- Keep existing `GameAction` table — it's the summary; `combat_events` is the detailed log
- Consider a `JsonDerivedType` attribute on `CombatEvent` for polymorphic deserialisation

---

### US-007: Wire Event Stream into SimulateSessionOrchestrator

**Description:** As a player using simulate mode, I want all combat output to flow through the event stream with `[You]` / `[AI]` labels.

**Workstream:** `backend-dotnet`

**Agent routing hint:** Requires .NET 10, familiarity with SimulateSessionOrchestrator.

**Acceptance Criteria:**
- [ ] SimulateSessionOrchestrator creates `CombatEventStream` per encounter
- [ ] Passes stream to Fight/Shoot/Blast orchestrators
- [ ] Passes participant mapping (`playerTeam.Id → "You"`, `aiTeam.Id → "AI"`) to renderer
- [ ] `CombatEventRenderer` renders events incrementally during the fight loop
- [ ] All simulate mode output now comes from events (no remaining direct `console.MarkupLine()` in combat orchestrators)
- [ ] SimulateSessionOrchestrator's own output (matchup tables, mode header, session messages) remains as direct console writes — only combat orchestrator output moves to events
- [ ] `DisplayEncounterSummary` renders from `FightResolvedEvent` / `ShootResolvedEvent`

**Quality Gates:**
```
dotnet build KillTeam.DataSlate.Console/KillTeam.DataSlate.Console.csproj
dotnet test KillTeam.DataSlate.Tests --verbosity quiet
```

**Technical Considerations:**
- The fight loop needs incremental rendering: after each Strike/Block, render the new event immediately
- `DisplayFightPools` is called inside the loop — renderer must handle this as a "state snapshot" event
- SimulateSessionOrchestrator owns the participant mapping; orchestrators just emit team IDs

---

## Functional Requirements

- FR-1: The following combat event types must be defined:
  - FR-1a: `TargetSelectedEvent` — operative targeted, with name and current wounds
  - FR-1b: `WeaponSelectedEvent` — weapon chosen (auto or manual), with full stats; includes injured status (effective Hit threshold) when applicable
  - FR-1c: `CoverStatusEvent` — cover/obscured state for target (shoot only)
  - FR-1d: `FightAssistSetEvent` — friendly allies count applied
  - FR-1e: `DiceRolledEvent` — raw dice values, labelled attacker/defender
  - FR-1f: `DiceClassifiedEvent` — dice classified as CRIT/HIT/MISS with thresholds (new output — replaces implicit pool display)
  - FR-1g: `ShockAppliedEvent` — defender die discarded due to Shock rule
  - FR-1h: `StrikePerformedEvent` — die used, damage dealt, target name
  - FR-1i: `BlockPerformedEvent` — active die and cancelled die
  - FR-1j: `ShootResolvedEvent` — hits/blocks/damage breakdown
  - FR-1k: `WoundsUpdatedEvent` — operative name, new wounds / max wounds (new output — wounds are currently updated silently with no console display)
  - FR-1l: `IncapacitationEvent` — operative name, cause (fight/shoot/self-damage)
  - FR-1m: `StunAppliedEvent` — target name, APL reduction
  - FR-1n: `HotDamageEvent` — attacker name, self-damage amount
  - FR-1o: `MultiTargetDeclaredEvent` — weapon type (Blast/Torrent), target count
  - FR-1p: `FriendlyFireWarningEvent` — count of friendly operatives affected
  - FR-1q: `FightPoolStateEvent` — snapshot of remaining dice pools (for table rendering)
  - FR-1r: `FightResolvedEvent` — summary totals (attacker damage, defender damage, incap flags)
  - FR-1s: `CoverSaveNotificationEvent` — informational: "+1 cover save will be added automatically"
  - FR-1t: `CombatWarningEvent` — error/early-exit paths: no valid targets, no weapons, target not found (with `WarningKind` discriminator)
  - FR-1u: `TargetResolutionStartEvent` — per-target header in Blast/Torrent (target name, wounds)
  - FR-1v: `CombatAbortedEvent` — encounter aborted (e.g. user declines friendly fire confirmation)
  - FR-1w: `EncounterStartedEvent` — bookend: marks beginning of encounter with participant info
  - FR-1x: `EncounterEndedEvent` — bookend: marks end of encounter
  - FR-1y: `RerollPerformedEvent` — (deferred) reroll result from Balanced/Ceaseless/Relentless/CP; defined here to reserve the type but implementation is a follow-up story

- FR-2: Every event must carry participant context (team ID or name) so the renderer can label output
- FR-3: Interactive prompts (selection menus, text input) remain as direct console calls — only output is event-driven
- FR-4: Events must be emitted in real-time during the fight loop (not batched at the end) to support incremental rendering
- FR-5: The `CombatEventRenderer` must produce visually equivalent output to the current direct console writes
- FR-6: Event persistence is optional and does not block rendering
- FR-7: Existing `GameAction` persistence continues alongside events — events supplement, not replace

## Diagrams

```mermaid
flowchart LR
    subgraph Orchestrator
        F[FightSession]
        S[ShootSession]
        B[BlastTorrent]
    end

    subgraph EventStream
        ES[CombatEventStream]
    end

    subgraph Rendering
        R[CombatEventRenderer]
        C[IAnsiConsole]
    end

    subgraph Persistence
        ER[ICombatEventRepository]
        DB[(SQLite)]
    end

    F -->|emit| ES
    S -->|emit| ES
    B -->|emit| ES
    ES -->|consume| R
    R -->|render| C
    ES -->|optional| ER
    ER -->|persist| DB
```

```mermaid
sequenceDiagram
    participant Sim as SimulateOrchestrator
    participant Fight as FightOrchestrator
    participant Stream as CombatEventStream
    participant Renderer as CombatEventRenderer
    participant Console as IAnsiConsole

    Sim->>Stream: Create (encounterId)
    Sim->>Fight: RunAsync(..., stream)
    Fight->>Stream: emit TargetSelectedEvent
    Stream->>Renderer: render immediately
    Renderer->>Console: [You] Target: Plague Champion
    Fight->>Stream: emit WeaponSelectedEvent
    Stream->>Renderer: render immediately
    Renderer->>Console: [You] Auto-selected: Power Fist
    Fight->>Stream: emit DiceRolledEvent
    Stream->>Renderer: render immediately
    Renderer->>Console: [You] Rolled: 6, 6, 2, 3, 2

    loop Fight Loop
        Fight->>Stream: emit FightPoolStateEvent
        Stream->>Renderer: render table
        Fight->>Console: SelectionPrompt (user picks action)
        Fight->>Stream: emit StrikePerformedEvent
        Stream->>Renderer: render immediately
        Renderer->>Console: [You] STRIKE — rolled 6 (CRIT) — 7 damage
    end

    Fight->>Stream: emit FightResolvedEvent
    Stream->>Renderer: render summary
```

## Technical Considerations

- **TC-1 Incremental rendering pattern:** The `CombatEventStream` should support an `OnEventEmitted` callback (or similar) so the renderer can process events as they are emitted, not just at the end. This is critical for the fight loop where the player needs to see updated pools between actions.
- **TC-2 Event serialisation:** Use `System.Text.Json` with `[JsonDerivedType]` attributes on the base `CombatEvent` type for polymorphic serialisation/deserialisation.
- **TC-3 No changes to resolution services:** `FightResolutionService` and `CombatResolutionService` remain pure — they take inputs and return results. The orchestrator wraps their calls with event emission.
- **TC-4 RerollOrchestrator prerequisite:** The `RerollOrchestrator` currently uses **static `AnsiConsole`** (not injected `IAnsiConsole`). This must be refactored to accept `IAnsiConsole` via constructor injection BEFORE or DURING the event work. Without this, reroll output (4 output calls: Balanced, Ceaseless, Relentless, CP results) bypasses the event stream entirely. Event types are defined in FR-1y but implementation is deferred.
- **TC-5 Testing strategy:** Test event emission by running orchestrators with a mock console (for prompts) and asserting on the collected `CombatEventStream.Events` list. No need to test console rendering in unit tests.
- **TC-6 New vs replacement events:** `WoundsUpdatedEvent` (FR-1k) and `DiceClassifiedEvent` (FR-1f) are new output — they don't replace existing console calls but add information not previously displayed.
- **TC-7 Firefight/Guard integration:** When combat orchestrators start emitting events, `FirefightPhaseOrchestrator` and `GuardInterruptOrchestrator` (which call `RunAsync()` on fight/shoot orchestrators) must pass through a `CombatEventStream`. Recommend: orchestrators accept an optional `CombatEventStream?` parameter — if null, create a local stream with an auto-rendering callback (backwards compatible, no firefight changes needed). Guard orchestrator's own output (guard cleared, not visible, stands guard) remains as direct console writes.
- **TC-8 SimulateSessionOrchestrator boundary:** Only combat orchestrator output moves to events. `SimulateSessionOrchestrator`'s own output (matchup table at `DisplayMatchups`, session headers, mode messages, error messages) remains as direct `IAnsiConsole` writes. The boundary is: once `RunAsync()` is called on a combat orchestrator, all output flows through events.

## Open Questions

- Should the `--save-events` flag be per-encounter or a global setting in `appsettings.json`?
- When firefight phase orchestrator adopts events, should it create one stream per activation or one per turning point?
