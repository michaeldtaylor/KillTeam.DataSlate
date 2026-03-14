# AI Review: Kill Team Game Tracking Spec

**Reviewed:** 2026-03-12 (all five spec/spike documents read in full)
**Reviewer:** GitHub Copilot CLI (AI review agent)
**Documents read:**
- `spec.md` (~800 lines)
- `spike-schema-ddl.md` (~1125 lines)
- `spike-firefight-loop.md` (~1163 lines)
- `spike-strategy-phase.md` (~975 lines)
- `spike-blast-torrent.md` (~1182 lines)

---

## 1. Summary

Kill Team Game Tracking is a .NET 10 interactive CLI application for recording a complete Kill Team \(v3.0\) game session from start to finish. Players import faction rosters from JSON files, register named players via `player add`, then start games pairing players to teams. Each game runs through four Turning Points, each consisting of a Strategy Phase (initiative roll, CP gain, ploy/Strategic Gambit recording) followed by a Firefight Phase (alternating operative activations with Shoot, Fight, Guard, Move, and Other actions). The app calculates all combat resolution — hit classification, save allocation, damage, and special-rule effects — from player-entered dice results, persisting every detail to SQLite. Post-game features include narrative annotation of any recorded action and win/loss statistics per player and per team. The tech stack is .NET 10, Spectre.Console for interactive CLI prompts, `Microsoft.Data.Sqlite` (no ORM), `System.Text.Json`, and xUnit + FluentAssertions + Verify.Xunit for testing. The domain is modelled as a schema-versioned SQLite database with ten tables in Migration 001, growing to thirteen tables by Migration 003.

---

## 2. Coverage Dashboard

| Story ID | Title | Size | Workstream | Quality Gates? | Spike Referenced? |
|---|---|---|---|---|---|
| US-001 | Import Roster from JSON | L (8 ACs) | backend | Y | N |
| US-002 | Start a New Game Session | L (7 ACs) | backend | Y | N |
| US-003 | Play Through a Game Session | L (17 ACs) | frontend | Y | Y (blast-torrent, reroll-mechanics, fight-ui, shoot-ui, guard-action) |
| US-004 | Annotate Actions with Narrative Fluff | M (5 ACs) | frontend | Y | N |
| US-005 | View Game History and Stats | M (5 ACs) | frontend | Y | N |
| US-006 | SQLite Persistence Layer | L (7 ACs) | backend | Y | Y (schema-ddl) |
| US-007 | Manage Players | M (5 ACs) | backend | Y | N |

**Size key:** S = 1–2 ACs, M = 3–5 ACs, L = 6+ ACs.
**Spike referenced:** US-003 references five spikes by name in its Technical Considerations; US-006 references `spike-schema-ddl.md`. The other stories have no explicit spike references.

---

## 3. Story-by-Story Issues

### US-001: Import a Roster from JSON

1. 🟡 **AC-5 (idempotency) — kill-team name match is case-sensitive, but this is not stated.** `IKillTeamRepository.UpsertAsync` is documented as "matched by name, case-sensitive" (spike-schema-ddl §5.2). The `kill_teams` DDL has no `COLLATE NOCASE` on the `name` column. Importing "Angels of Death" then "angels of death" will create two records. This contradicts the spirit of AC-5 and will silently break re-import idempotency if the JSON files differ in capitalisation between imports. Either add `COLLATE NOCASE` to `kill_teams.name` or document the case-sensitive contract explicitly.

2. 🟡 **AC-7 — behaviour on unknown JSON fields is unspecified.** `System.Text.Json` by default throws on unexpected properties when deserialising with a typed model. If a roster file has a `playerName` field (which the spec says to ignore), the deserialiser will fail unless `JsonSerializerOptions.UnknownTypeHandling` or `[JsonExtensionData]` is configured. AC-7 says only "clear error messages for malformed JSON or missing required fields" — it does not say "ignore unknown fields". An implementer may inadvertently treat extra fields as a validation error rather than ignoring them.

3. 🟢 **AC-6 — summary format has a minor ambiguity.** The example is `"Imported 'Angels of Death' — 6 operatives, 14 weapons"`. It is not stated whether "14 weapons" is the total weapon entries across all operatives or distinct weapon types. Not blocking but worth confirming.

---

### US-002: Start a New Game Session

1. 🔴 **AC-5 — `CpTeamA`/`CpTeamB` columns are missing from the `games` DDL in Migration 001.** The spec domain model (spec.md lines 77–79) defines `Game.CpTeamA` and `Game.CpTeamB` as live mutable CP counters. AC-5 says "each team starts with 2 CP". However, Migration 001 in `spike-schema-ddl.md` (§3) has no `cp_team_a` / `cp_team_b` columns on the `games` table. The strategy phase spike (§5.3) acknowledges this as "Open Question #2" and proposes adding them via `ALTER TABLE` in Migration 002. If an implementer follows Migration 001 alone, the `new-game` command has no column in which to store the starting CP — this will cause a runtime crash on first `new-game` invocation.

2. 🟡 **AC-7 — "cannot start if fewer than two rosters imported" is not reflected in listed unit tests.** AC-7 unit tests say: "happy path, single-roster-imported error, no-players-registered error." A single-roster-imported scenario is one specific case of "fewer than two rosters". There is no test listed for the zero-roster case or for two operatives on the same team being selected as both sides.

3. 🟢 **No guard against selecting the same team for both Team A and Team B.** Not prohibited by any AC, but an oversight that will create a trivially unplayable game where every operative is simultaneously on both sides.

---

### US-003: Play Through a Game Session

1. 🔴 **AC-6 — CP re-roll scope in Blast/Torrent is undefined.** AC-6 says "after attack dice entry and weapon re-rolls, app offers attacker 'Spend 1CP to re-roll one attack die?' if CP ≥ 1; same offered to defender after defence dice entry." In the Blast/Torrent flow (spike-blast-torrent §3.2, Step 8d), the defender CP re-roll fires once **per target** inside the target loop. The attacker CP re-roll must fire **once** on the shared pool (before the per-target loop). This asymmetry — attacker re-roll 1× total, defender re-roll 1× per target — is never stated in US-003 or in the blast spike. An implementer could reasonably offer the attacker CP re-roll per target (which would be wrong) or omit the defender re-roll for additional targets.

2. 🔴 **Resume logic references a `status` column on `TurningPoint` that does not exist.** spike-firefight-loop §11.8 pseudocode says: `GetCurrentAsync` loads "the current TurningPoint record where `Status == InProgress`". No `status` column appears on the `turning_points` DDL in spike-schema-ddl §3. The column does not exist in Migration 001 or 002. The intended resume mechanic must be implemented differently (e.g. "last TurningPoint by number for the game") but this is never specified, leaving the resume path completely ambiguous.

3. 🔴 **`RerollOrchestrator` and `HasBeenRerolled` flag have no persistence — cannot safely resume mid-re-roll.** AC-6 says "a die cannot be re-rolled a second time." The design uses an in-memory `RollableDie.HasBeenRerolled` flag (spec.md tech considerations). Only the final int[] results are persisted. If the app crashes after a Balanced re-roll but before the CP re-roll step, re-launch will re-offer both re-rolls against the already-re-rolled dice. No AC or test stub covers this crash-recovery invariant.

4. 🟡 **AC-3 — Fight action: no melee weapon edge case not surfaced.** AC-3 says "both players select their melee weapon" when Fight is chosen. spike-firefight-loop §9 correctly says Fight requires "≥1 melee weapon". But AC-3 has no clause for what happens when the attacker or the defender has zero melee weapons. If it's filtered from the menu (attacker side), that's handled — but the defender's weapon selection during Fight is not gated by the action menu. This needs explicit specification.

5. 🟡 **AC-13 (Counteract) — eligibility when an operative is on Guard is unspecified.** AC-13 eligibility requires Engage order and `HasUsedCounteractThisTurningPoint == false`. An operative that chose Engage order and then spent an AP on Guard has both `Order == Engage` and `IsOnGuard == true`. The spike counteract filter (spike-firefight-loop §7) does not exclude on-Guard operatives. This is likely intentional (a guard can counteract) but is not explicitly stated, which will cause confusion.

6. 🟡 **AC-8 — in-cover save only mentioned for Shoot; Fight cover is undefined.** AC-8 says the in-cover save applies unconditionally. The spec technical considerations clarify "In-cover save: **unconditionally** retain 1 defence die as a normal success before rolling the rest." In Kill Team V3.0, in-cover saves apply to both Shoot and Fight actions, but AC-8 is only under the Shoot bullet (AC-3 Fight description has no cover clause). If the Fight orchestrator omits the cover auto-save, it will be a silent rules bug.

7. 🟡 **AC-11 (injured stat penalty) — `Move -2"` is a display and resolution effect, but `Move` is not stored on `GameOperativeState`; only `MoveModifier` is implied by context.** The spec says "app automatically applies Move -2" to displayed stat." There is no `MoveModifier` column on `game_operative_states`. The app would have to compute `effective Move = operatives.move - 2` at display time when injured. This is fine for display but the combat resolution for Guard (which checks movement distance) and Counteract (movement cap of 2") needs to know the effective Move stat at runtime. No field tracks this; the service must derive it. This implicit derivation is not called out in any AC.

8. 🟡 **AC-17 — resume described but no explicit test stub for mid-TP resume.** The firefight loop spike §11.8 provides pseudocode for resume, but the xUnit test stubs (spike-firefight-loop §13) do not include a test for resume state reconstruction. The test stubs cover counteract, guard, TP-end reset, and game-end, but not resume.

9. 🟢 **`play <game-id>` on a Completed game — behaviour undefined.** AC-1 says the command works; AC-17 says the app "can resume an in-progress game." It is not stated whether `play` on an already-completed game should error, display a read-only view, or re-open it.

10. 🟢 **`Distance moved` for movement actions is recorded but not validated.** The action loop records `distance` for movement actions (spike-firefight-loop §5 pseudocode) but no range check is applied. An entry of `999"` would be recorded without error.

---

### US-004: Annotate Actions with Narrative Fluff

1. 🟢 **AC-2 — list of activations for Blast/Torrent actions may need special display.** For a Blast action, the action has multiple targets in `action_blast_targets`. The annotation flow only mentions drilling into an action; it doesn't specify whether to show per-blast-target sub-entries. Not blocking for the annotation feature itself, but the display will be incomplete for multi-target actions.

2. 🟢 **No AC specifies what happens when `annotate` is called on a game ID that does not exist.** Should show an error. Trivial to add but currently absent.

---

### US-005: View Game History and Stats

1. 🟡 **AC-3 — `stats --team <name>` kill count misses Blast/Torrent incapacitations.** Technical Considerations say "Total kills: COUNT(actions WHERE CausedIncapacitation = 1 AND action.teamId = this team)". However, for Blast/Torrent actions, additional-target incapacitations are stored in `action_blast_targets.caused_incapacitation`, not in `actions.caused_incapacitation` (which stores only the primary target result). The query as written will under-count kills for any team that used Blast/Torrent weapons. The fix is to UNION with `action_blast_targets.caused_incapacitation`.

2. 🟡 **AC-3 — `stats --team <name>` "most-used weapon" counts one row per Blast/Torrent shot regardless of targets hit.** A single Blast action fires against 3 operatives but inserts one `actions` row. The weapon-use count query (`GROUP BY weaponId, COUNT(*)`) treats a Blast-3-targets as equivalent to a single-target Shoot. This is arguably a design choice but should be explicitly documented.

3. 🟡 **AC-1 — `history` table doesn't mention showing ploys or Turning Point count.** Minor omission in the display spec, but the game log doesn't surface Strategy Phase data at all in the history view.

4. 🟢 **AC-4 — `view-game` log display does not reference `action_blast_targets`.** The turn-by-turn log for a game with Blast/Torrent weapons should show per-target damage. The current AC doesn't describe this; an implementer will produce an incomplete log.

---

### US-006: SQLite Persistence Layer

1. 🔴 **AC-4 — table list omits `ploy_uses` and `action_blast_targets`.** AC-4 lists 9 tables: "players, kill_teams, operatives, weapons, games, game_operative_states, turning_points, activations, actions". It does not include `ploy_uses` (Migration 002, needed by US-003 Strategy Phase) or `action_blast_targets` (Migration 003, needed by US-003 Blast/Torrent). An implementer treating US-006 as the definitive persistence contract will not implement these tables.

2. 🔴 **`is_strategy_phase_complete` is in the spec domain model but absent from Migration 001 DDL.** spec.md line 86 lists `IsStrategyPhaseComplete (bool, default false)` on `TurningPoint`. Migration 001 in spike-schema-ddl §3 does not include this column. It is only added via `ALTER TABLE` in Migration 002 (spike-strategy-phase §5.2). An implementer building Migration 001 from the spec domain model will include the column; an implementer building it from spike-schema-ddl §3 will not. The divergence means `TestDbBuilder` will fail if it seeds a TurningPoint with this field before Migration 002 has run.

3. 🟡 **`IPloyRepository` and `ITurningPointRepository` (extended) are not referenced in US-006.** spike-strategy-phase §5.4 and §5.5 define these interfaces. US-006 only mentions the 8 repository interfaces implied by the 9 tables in AC-4. An implementer of US-006 will not know to define these.

4. 🟡 **`IActivationRepository` is referenced in `FirefightPhaseOrchestrator` (spike-firefight-loop §3) but not defined in spike-schema-ddl §5.** The constructor injects `IActivationRepository activationRepo` but no interface definition is provided. This is a gap in the spike that must be filled before implementation.

5. 🟡 **`IBlastTargetRepository` (or equivalent) is not defined anywhere.** spike-blast-torrent §6 introduces `action_blast_targets` and says the orchestrator "persists one `action_blast_targets` row per target", but provides no repository interface for this table.

6. 🟡 **Two incompatible versions of `TestDbBuilder.WithTurningPoint` exist.** spike-schema-ddl §6 defines `WithTurningPoint(Guid id, Guid gameId, int number)` — no `strategyPhaseComplete` parameter. spike-strategy-phase §10.3 defines `WithTurningPoint(Guid id, Guid gameId, int number, bool strategyPhaseComplete = false)` — an overload with the new column. If both spikes are implemented independently, the resulting class will have a method conflict or a gap.

---

### US-007: Manage Players

1. 🟡 **AC-3 — game-history protection check is not in `IPlayerRepository.DeleteAsync`.** The interface (spike-schema-ddl §5.1) defines `DeleteAsync(Guid id)` as "No-op if not found." The protection logic ("Cannot delete 'Michael' — they have 3 recorded games") must live in the command/service layer, but this is never specified in either the US or the spike. Without an explicit note, an implementer may put the check in the repository (wrong layer) or omit it entirely.

2. 🟢 **AC-1 — `player add` trims whitespace but does not specify maximum name length.** A 10,000-character player name will be stored successfully. Not blocking but a practical bound would be useful.

---

## 4. Domain Model Gaps

| Field / Table | Where it appears | In spec.md domain model? | Impact if absent |
|---|---|---|---|
| `ploy_uses` table | spike-strategy-phase §5.1; spike-schema-ddl §4 (stub) | **No** — not listed in spec.md domain model | Strategy Phase ploy recording is impossible; CP deduction on ploy use has no target table |
| `action_blast_targets` table | spike-blast-torrent §6.1 (Migration 003) | **No** — not listed | Blast/Torrent multi-target damage results cannot be persisted; kills and damage stats for these weapons will be wrong |
| `is_strategy_phase_complete` on `TurningPoint` | spike-strategy-phase §5.2; set in Migration 002 | **YES** — listed as `IsStrategyPhaseComplete (bool, default false)` | Column present in domain model but **absent from Migration 001 DDL** — implementers who follow the DDL spike will miss it |
| `cp_team_a` / `cp_team_b` on `games` | spike-strategy-phase §5.3; confirmed by spec domain model lines 77–79 | **YES** — listed on `Game` entity | **Absent from Migration 001 DDL** — `new-game` command cannot store starting CP; game crashes on first play |
| `reroll_used` / re-roll state | spike-reroll-mechanics.md (not reviewed here); in-memory `RollableDie.HasBeenRerolled` flag only | **No column in any table** | Cannot audit re-roll history; cannot safely resume an app restart that occurs between weapon re-rolls and CP re-rolls |
| `is_on_guard` on `GameOperativeState` | spike-schema-ddl §3; spec.md domain model line 118 | **YES** — listed as `IsOnGuard (bool)` | Already present in both DDL and domain model — **no gap** |
| Concede / scooped state on `Game` | Not in any spike or DDL | **No** | No built-in concede mechanism; a game can only end via TP4 VP prompt or full incapacitation of one side. If a player surrenders the app has no way to record it |
| `description` column on `ploy_uses` | spike-strategy-phase §5.1 (full DDL) | N/A — `ploy_uses` itself not in domain model | The stub DDL in spike-schema-ddl §4 omits `description TEXT NULL`; the full DDL in the strategy spike adds it. These two DDL fragments are inconsistent — Migration 002 would need to use the full version |
| `IBlastTargetRepository` interface | Implied by spike-blast-torrent §5.4 | **No repository interface defined anywhere** | No typed persistence path for `action_blast_targets`; blast orchestrator cannot persist per-target results cleanly |
| `IActivationRepository` interface | Referenced in spike-firefight-loop §3 constructor | **No interface definition in any spike** | Constructor injection will fail at DI registration time |
| `ITurningPointRepository` | spike-strategy-phase §5.5 defines extended methods | Partial — base form implied; extended methods absent from US-006 | Strategy Phase orchestrator cannot call `CompleteStrategyPhaseAsync` or `IsStrategyPhaseCompleteAsync` without this interface |

---

## 5. Cross-Spike Consistency

### 5.1 `TurningPoint.Status` — referenced but never defined
- **Method/context:** spike-firefight-loop §11.8, resume pseudocode: `GetCurrentAsync` loads TP "where `Status == InProgress`"
- **Spec says:** No `status` column on `turning_points` in any spike or domain model
- **Spike says:** spike-schema-ddl Migration 001 has no `status` column; the column simply doesn't exist
- **Severity:** 🔴 **Critical** — the resume entry-point code as written cannot be implemented against the actual schema. The column must be added or the resume query must use a different predicate (e.g. "last TP by number" or "TP where `is_strategy_phase_complete = 1` but game is still InProgress")

### 5.2 CP board-header reads stale data after Firefight Phase CP re-rolls
- **Method/context:** spike-firefight-loop §10 (board state display): "CP display reads from `TurningPoint.CpTeamA` / `TurningPoint.CpTeamB`"
- **Spec says:** CP re-roll spends deduct 1 CP from the team's pool (US-003 AC-6), updating `Game.cp_team_a/b`
- **Spike says:** spike-firefight-loop §12 persistence table: `TurningPoint.CpTeamA/B` "are set during the Strategy Phase and are read-only during the Firefight Phase"
- **Divergence:** If the header reads from TP fields (snapshot) but CP is consumed from Game fields (live), the displayed CP will be wrong after any CP re-roll during combat. A player spending 1 CP on a re-roll will still see the pre-spend value in the header.
- **Severity:** 🟡 **Needs attention** — fix by reading CP from `Game.cp_team_a/b` for the live display, keeping the TP snapshot only for post-game review

### 5.3 `TestDbBuilder.WithTurningPoint` — two incompatible signatures
- **Method/context:** `TestDbBuilder.WithTurningPoint`
- **spike-schema-ddl §6 says:** `WithTurningPoint(Guid id, Guid gameId, int number)` — 3 parameters, no `is_strategy_phase_complete`
- **spike-strategy-phase §10.3 says:** `WithTurningPoint(Guid id, Guid gameId, int number, bool strategyPhaseComplete = false)` — 4 parameters
- **Severity:** 🟡 **Needs attention** — implementer must reconcile; the 3-param version will insert `0` for `is_strategy_phase_complete` (via SQLite DEFAULT), which is correct but only after Migration 002 has run; before Migration 002 the column doesn't exist and the INSERT will fail if the column is referenced

### 5.4 `PloyUse.Description` — column absent in `spike-schema-ddl` Migration 002 stub
- **Method/context:** `ploy_uses` DDL
- **spike-schema-ddl §4 says:** `ploy_uses` has columns `id`, `turning_point_id`, `team_id`, `ploy_name`, `cp_cost` — **no description**
- **spike-strategy-phase §5.1 says:** Full DDL adds `description TEXT NULL`
- **Severity:** 🟡 **Needs attention** — `PloyUse` C# record (spike-strategy-phase §3.4) includes a `Description` property; the stub DDL will fail to persist it. The authoritative DDL is in the strategy spike; the schema-ddl stub must be updated.

### 5.5 `BlastTargetInput.OperativeId` typed as `string` vs project-wide `Guid`
- **Method/context:** spike-blast-torrent §5.1 `BlastTargetInput` record
- **Spec/other spikes say:** All domain IDs use `Guid` (`Player.Id`, `KillTeam.Id`, `Operative.Id`, `Game.Id`, etc.)
- **Blast spike says:** `OperativeId` is `string` in both `BlastTargetInput` and `TargetResolutionResult`
- **Severity:** 🟡 **Needs attention** — passing `string` to a code-base that uses `Guid.Parse` everywhere will produce compile errors or require explicit conversion in `BlastResolutionService`

### 5.6 `BuildSyntheticAttackDice` re-classifies shared pool — potential for rule double-application
- **Method/context:** spike-blast-torrent §5.3 `BlastResolutionService.ResolveTarget`
- **Problem:** Synthetic dice are built as `normals × hitThreshold` and `crits × 6`. These are then passed to `CombatResolutionService.ResolveShoot`, which re-applies Severe/Rending/Punishing/Lethal during classification. Since the shared pool was already classified in `ClassifyAttackPool` (which also passes through `ResolveShoot`), these rules fire twice — once in `ClassifyAttackPool`, once in each `ResolveTarget` call.
- **Spike says:** The spike instructs stripping `Blast`/`Torrent` rules from the `filteredCtx` but does NOT strip `Severe`, `Rending`, `Punishing`, or `Lethal` from per-target calls.
- **Severity:** 🔴 **Critical** — Severe, Rending, Punishing, and Lethal will be applied once globally (correct) and then again per target (incorrect), producing wrong hit counts for every additional target after the first.

### 5.7 Ploy order in Strategy Phase — non-initiative player first (transcript) vs unspecified (US-003 AC-2)
- **Method/context:** spike-strategy-phase §4 transcript: non-initiative player enters ploys first
- **US-003 AC-2 says:** "allows recording ploy use (name + CP cost, free text)" — no player order specified
- **spike-strategy-phase Open Question #1:** explicitly flags this as unresolved
- **Severity:** 🟡 **Needs attention** — if the rule is non-initiative player first, the orchestrator loop must be ordered accordingly; if wrong it affects every game transcript

### 5.8 `IGameRepository.UpdateStatusAsync` vs `IGameRepository.UpdateCpAsync` — interface completeness
- **Method/context:** spike-schema-ddl §5.3 defines `UpdateStatusAsync` (status + VP + winner); spike-strategy-phase §5.6 defines `UpdateCpAsync` (CP only)
- **Problem:** These are complementary but defined in different spikes. An implementer following only spike-schema-ddl will produce an `IGameRepository` without `UpdateCpAsync`. US-006 references spike-schema-ddl but not spike-strategy-phase.
- **Severity:** 🟡 **Needs attention** — aggregate `IGameRepository` interface definition must be sourced from both spikes

---

## 6. Error Case Coverage

| Error Path | Status | Citation / Gap |
|---|---|---|
| **Invalid / malformed JSON payload** | ✅ Covered | US-001 AC-7: "Clear error messages for malformed JSON"; AC-8 unit test: "invalid JSON" listed |
| **Duplicate player registration** | ✅ Covered | US-007 AC-1: unique name constraint; spike-schema-ddl §7 test: `PlayerRepository_Add_DuplicateName_Throws` |
| **Resuming a game mid-Turning Point** | ⚠ Partially covered | spike-firefight-loop §11.8 describes pseudocode; US-003 AC-17 states it is possible. **Gap**: no xUnit test stub exercises resume. Also, the `Status == InProgress` predicate used in the pseudocode references a non-existent column (see §5.1 above). |
| **Offering a re-roll when CP = 0** | ⚠ Implicitly handled, not tested | US-003 AC-6: "if CP ≥ 1" condition prevents the offer. spike-strategy-phase §8.2 tests `CanSpendPloy(0)` for the Strategy Phase. **Gap**: no AC or test stub covers the Firefight Phase CP re-roll when CP = 0. |
| **Activating an already-activated operative** | ⚠ Prevented by UI, not explicitly tested | `GetReadyOps` filters `IsReady == true`, so expended operatives never appear in the selection menu. **Gap**: no AC or test asserts that an already-expended operative cannot be selected. |
| **Shooting with a Limited weapon after uses exhausted** | ❌ Not covered | `Limited x` weapon rule is listed in the special rules table as "Usage counter" enforcement tier. No AC, no test stub, no service method, and no column in any table tracks remaining uses of a Limited weapon. |
| **Spending more CP than available** | ⚠ Implicitly handled, not tested | Both the CP re-roll guard (`if CP ≥ 1`) and the ploy guard (`CanSpendPloy`) prevent spending. **Gap**: no AC or test explicitly covers "what happens if a code path bypasses the guard and attempts a CP decrement at 0." |

---

## 7. Recommended Actions

### 🔴 Must fix before implementation

**R-01: Add `cp_team_a` / `cp_team_b` to `games` table in Migration 001 DDL**
- **Affects:** US-002 AC-5, US-006 AC-4, spike-schema-ddl §3
- `new-game` cannot store starting 2 CP per team without these columns. Resolve Open Question #2 from spike-strategy-phase by adding both columns to Migration 001 with `DEFAULT 2`, eliminating the need for the `ALTER TABLE` stub in Migration 002.

**R-02: Add `is_strategy_phase_complete` to `turning_points` in Migration 001 DDL**
- **Affects:** US-006 AC-4, spec.md domain model line 86, spike-schema-ddl §3
- The column is in the spec domain model but absent from Migration 001. Adding it to Migration 001 removes the schema divergence and makes `TestDbBuilder` consistent from the start. Update `spike-schema-ddl §3` DDL block accordingly.

**R-03: Define the resume predicate for "current TurningPoint" without a non-existent `status` column**
- **Affects:** US-003 AC-17, spike-firefight-loop §11.8
- Replace the pseudocode `where Status == InProgress` with a concrete query such as: the `turning_points` row with the maximum `number` for the given `game_id` that has `is_strategy_phase_complete = 1` but whose game still has `status = 'InProgress'`. Document the corrected predicate in spike-firefight-loop.

**R-04: Fix double-application of Severe/Rending/Punishing/Lethal in `BlastResolutionService.ResolveTarget`**
- **Affects:** US-003 AC-12, spike-blast-torrent §5.3
- `ClassifyAttackPool` already applies these rules once. `ResolveTarget` must NOT pass them to `CombatResolutionService` again. Strip all attack-pool-modifying rules (Severe, Rending, Punishing, Lethal, Accurate) from `filteredCtx.WeaponRules` before calling `_combat.ResolveShoot`, in addition to stripping Blast/Torrent. Alternatively, expose a `ClassifyOnly` method on `CombatResolutionService` that returns the classified pool without resolving saves.

**R-05: Define the attacker-once / defender-per-target CP re-roll rule for Blast/Torrent in US-003 or spike-blast-torrent**
- **Affects:** US-003 AC-6, spike-blast-torrent §3.2
- Add a sentence to US-003 AC-6 (or to spike-blast-torrent §3.2 Step 8d): "Attacker CP re-roll is offered **once** on the shared attack pool (between step 7 and step 8). Defender CP re-roll is offered **per target** inside the step 8 loop."

**R-06: Define `IActivationRepository` and `IBlastTargetRepository` interfaces**
- **Affects:** US-006 AC-4, spike-firefight-loop §3, spike-blast-torrent §6
- `IActivationRepository` is injected into `FirefightPhaseOrchestrator` but never defined. `IBlastTargetRepository` is implied by blast persistence but also undefined. Add both interface definitions to spike-schema-ddl §5.

---

### 🟡 Should address

**R-07: Resolve `TestDbBuilder.WithTurningPoint` signature conflict**
- **Affects:** US-006, spike-schema-ddl §6 vs spike-strategy-phase §10.3
- Adopt the 4-parameter version from spike-strategy-phase (with `bool strategyPhaseComplete = false`) as the single canonical definition and update spike-schema-ddl §6 to match.

**R-08: Merge `ploy_uses` full DDL into spike-schema-ddl Migration 002 stub**
- **Affects:** US-006, spike-schema-ddl §4, spike-strategy-phase §5.1
- The stub in spike-schema-ddl §4 omits `description TEXT NULL`. Update the stub to be identical to the full DDL in spike-strategy-phase §5.1 so Migration 002 is unambiguous.

**R-09: Update US-006 AC-4 to list all 11 tables (including ploy_uses and action_blast_targets)**
- **Affects:** US-006 AC-4
- Currently lists 9 tables. Add `ploy_uses` and `action_blast_targets` with notes pointing to Migration 002 and Migration 003 respectively.

**R-10: Fix kill-count query in US-005 to include `action_blast_targets.caused_incapacitation`**
- **Affects:** US-005 AC-3 Technical Considerations
- Replace the single-table COUNT with a UNION:
  ```sql
  SELECT COUNT(*) FROM actions a
  JOIN activations act ON act.id = a.activation_id
  WHERE a.caused_incapacitation = 1 AND act.team_id = @teamId
  UNION ALL
  SELECT COUNT(*) FROM action_blast_targets abt
  JOIN actions a2 ON a2.id = abt.action_id
  JOIN activations act2 ON act2.id = a2.activation_id
  WHERE abt.caused_incapacitation = 1 AND act2.team_id = @teamId
  ```

**R-11: Clarify or add COLLATE NOCASE on `kill_teams.name` for idempotent upsert**
- **Affects:** US-001 AC-5, spike-schema-ddl §3
- Either add `COLLATE NOCASE` to `kill_teams.name` (matching players table) or explicitly document "team names are case-sensitive for upsert matching" so the contract is unambiguous.

**R-12: Add `IPloyRepository`, `ITurningPointRepository` (extended methods), and `IGameRepository.UpdateCpAsync` to US-006 technical considerations**
- **Affects:** US-006, spike-strategy-phase §5.4, §5.5, §5.6
- These three repository additions are defined in spike-strategy-phase but not referenced in US-006. Add references so an implementer of US-006 knows to build them.

**R-13: Document `player delete` game-history check in US-007 service layer**
- **Affects:** US-007 AC-3 Technical Considerations
- Specify that the command handler (not the repository) must query `games` for `player_a_id = id OR player_b_id = id` before calling `DeleteAsync`. The repository interface stays clean; the check lives in `PlayerDeleteCommand`.

**R-14: Fix `BlastTargetInput.OperativeId` type from `string` to `Guid`**
- **Affects:** spike-blast-torrent §5.1
- Align with the domain-wide convention. Change `string OperativeId` and add `string OperativeName` as a separate display-only property rather than re-using the ID field for name display.

**R-15: Fix CP board display to read from `Game.cp_team_a/b` (live) not `TurningPoint.cp_team_a/b` (snapshot)**
- **Affects:** spike-firefight-loop §10, §12
- The board header must reflect CP after Firefight Phase spends. Update the display to pull CP from the live Game record. Keep the TP snapshot for post-game review only.

**R-16: Add xUnit test stub for mid-TP resume state reconstruction**
- **Affects:** US-003 AC-17, spike-firefight-loop §13
- Add a test that seeds a game with 2 activations already recorded, then verifies that the orchestrator correctly identifies the current team (initiative or non-initiative based on even/odd activation count) and skips expended operatives.

---

### 🟢 Nice to have

**R-17: Add xUnit test stub for Firefight Phase CP re-roll when CP = 0**
- **Affects:** US-003 AC-6
- Assert that when `game.CpTeamA == 0` the attacker is not offered a re-roll prompt. Prevents a regression if the `>= 1` guard is accidentally removed.

**R-18: Specify behaviour of `play <game-id>` on an already-Completed game**
- **Affects:** US-003 AC-1
- Should show an error ("Game #4 is already completed. Use `view-game` to see the log.") rather than undefined behaviour.

**R-19: Add `Limited x` weapon enforcement**
- **Affects:** US-003 AC-12 (weapon rules enforcement)
- `Limited x` is listed as "Usage counter" tier but has no implementation path. Add a `limited_uses_remaining` column to `game_operative_states` (or a join table), or explicitly defer Limited enforcement to the player and document it as "display only" like Poison/Toxic.

**R-20: Add a concede action**
- **Affects:** FR-3 implicitly
- Currently the only ways to end a game are TP4 completion or full incapacitation. A `concede` action on `play` would set the game to Completed, prompt VP entry, and set the winner. Useful for real games where one side is wiped early or it's obviously over.

**R-21: Clarify `MaxWounds` property name in domain class documentation**
- **Affects:** spec.md domain model, spike-firefight-loop §10 board display
- `operatives.wounds` (the DDL column) is the starting/maximum wounds value. The board display uses `operative.MaxWounds`. Document explicitly that `Operative.Wounds` maps to `MaxWounds` in the display layer to avoid confusion with `GameOperativeState.CurrentWounds`.

**R-22: Document `view-game` display format for Blast/Torrent actions**
- **Affects:** US-005 AC-4
- Specify that a Blast/Torrent action row should expand to show per-target results from `action_blast_targets` (target name, defence dice, damage, incapacitation), matching the Blast Summary panel shown in spike-blast-torrent §4 Phase 7.
