# Kill Team DataSlate

An interactive CLI app for tracking Kill Team (KT24 V3.0) game sessions — recording every operative activation, dice roll, and damage result to SQLite.

---

## Getting Started

1. **Add players** — register the people playing:
   ```
   killteam player add "Michael"
   killteam player add "Solomon"
   ```

2. **Import rosters** — load kill team JSON files:
   ```
   killteam import-kill-teams ./rosters/angels-of-death.json
   killteam import-kill-teams ./rosters/          # scans entire folder
   ```

3. **Start a new game** — interactive prompts select players and teams:
   ```
   killteam new-game
   ```

4. **Play through Turning Points** — resumes automatically if interrupted:
   ```
   killteam play <game-id>
   ```

5. **Review history and stats**:
   ```
   killteam history
   killteam stats
   killteam view-game <game-id>
   ```

---

## Command Reference

### Player Management

| Command | Description |
|---------|-------------|
| `player add <name>` | Register a new player |
| `player list` | List all players with win/loss stats |
| `player delete <name>` | Delete a player (blocked if they have recorded games) |

### Roster Import

| Command | Description |
|---------|-------------|
| `import-kill-teams <path>` | Import a kill team from a JSON file or scan a folder for all `.json` files |

Roster files use the standard Kill Team JSON format. Key fields:
- `save`: `"3+"` (string) or `3` (int) — save threshold
- `dmg`: `"3/4"` — normal damage / critical damage
- `hit`: `"3+"` — hit threshold
- `special_rules`: array of strings, e.g. `["Piercing 1", "Lethal 5+", "Balanced"]`

Re-importing a roster by the same team name updates the existing record.

### Game Setup

| Command | Description |
|---------|-------------|
| `new-game` | Start a new game — interactive prompts for players, kill teams, and mission name |

### Playing a Game

| Command | Description |
|---------|-------------|
| `play <game-id>` | Play (or resume) a game by ID. Runs all 4 Turning Points interactively. |

The play command walks through:
- **Strategy Phase**: initiative roll, CP gains, ploy recording
- **Firefight Phase**: operative activation loop with full Shoot / Fight / Guard / movement actions

The game state is fully persisted to SQLite after every action. Kill the app at any point and resume with `play <game-id>`.

### Annotation

| Command | Description |
|---------|-------------|
| `annotate <game-id>` | Add or edit narrative notes on activations or individual actions |

### History & Statistics

| Command | Description |
|---------|-------------|
| `history` | List all completed games |
| `history --player <name>` | Filter games by player name |
| `stats` | Per-player win/loss statistics |
| `stats --team <name>` | Per-team stats: games, wins, kills, most-used weapon |
| `stats --player <name>` | Per-player stats filtered by name |
| `view-game <game-id>` | Full game detail: all TPs, activations, actions, dice, damage, narrative notes |

---

## Configuration

Settings are in `appsettings.json` under the `DataSlate` section:

| Key | Default | Description |
|-----|---------|-------------|
| `DataSlate:DatabasePath` | `./data/kill-team.db` | Path to the SQLite database file |
| `DataSlate:RosterFolder` | `./rosters/` | Default folder scanned by `import-kill-teams` when given a directory |

Example `appsettings.json`:
```json
{
  "DataSlate": {
    "DatabasePath": "./data/kill-team.db",
    "RosterFolder": "./rosters/"
  }
}
```

The database is created automatically on first run. If it is missing or corrupt, delete `data/kill-team.db` — it will be recreated on the next run (**all data will be lost**).

---

## Technical Notes

- **.NET 9**, Spectre.Console.Cli, SQLite (`Microsoft.Data.Sqlite`)
- Schema auto-migrates on startup via `DatabaseInitialiser` (4 migrations)
- All dice, wounds, CP, and actions are persisted in real time
- Re-rolls (Balanced / Ceaseless / Relentless / CP) tracked per-die with `HasBeenRerolled` guard
- Blast/Torrent multi-target weapons use a dedicated `action_blast_targets` table