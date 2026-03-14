# Spike: SQLite Schema DDL + Migrations

**Status**: Draft  
**Author**: Spike  
**Date**: 2025-07  
**Area**: Kill Team Game Tracking — SQLite Schema

---

## 1. Introduction

Before writing a single repository class, the team needs an agreed-upon schema: exact table names,
column types, constraints, foreign-key topology, and a versioning strategy that will not break
existing databases when future features add columns or tables. This spike captures all of that so an
implementing developer has zero ambiguity.

**What this spike covers:**

- The complete DDL for all ten domain tables (`schema_version`, `players`, `kill_teams`,
  `operatives`, `weapons`, `games`, `turning_points`, `activations`, `actions`,
  `game_operative_states`).
- The migration versioning strategy and the `DatabaseInitialiser` class that applies it at startup.
- Repository interfaces that expose typed access to each table.
- The `TestDbBuilder` helper used in every xUnit test that touches the database.
- Ten test stubs covering creation, idempotency, uniqueness, and round-trip persistence.
- An ASCII ER diagram of the full schema.
- Implementation notes for embedding SQL in C#, connection lifecycle, and known SQLite limitations.

This document is the source of truth for the schema. All other spikes that reference persistence
(`spike-shoot-ui.md`, `spike-fight-ui.md`, `spike-guard-action.md`) defer to this document for
column names and types.

---

## 2. Migration Strategy

### 2.1 `schema_version` Table

A single-row table tracks the current schema version:

```sql
CREATE TABLE IF NOT EXISTS schema_version (
    version INTEGER NOT NULL
);
```

On first run (empty database), this table is missing. `DatabaseInitialiser` treats a missing table
as version `0`. After every successfully applied migration the version row is updated.

### 2.2 Startup Sequence

1. Open a `SqliteConnection` to the configured database path (or `:memory:` for tests).
2. Issue `PRAGMA foreign_keys = ON` immediately after opening.
3. Read the current version: `SELECT version FROM schema_version LIMIT 1`. If the table does not
   exist (or no rows), the current version is `0`.
4. For each known migration whose number is **greater than** the current version, in ascending
   order:
   a. Execute the migration SQL inside a transaction.
   b. Upsert the version row (`DELETE FROM schema_version; INSERT INTO schema_version VALUES (?)`).
   c. Commit the transaction.
5. If any migration throws, the transaction is rolled back, and the app exits immediately with a
   message: `"Migration {version:D3} failed: {exception.Message}"`. No partial state is left.

### 2.3 Migration Registration Pattern

Migrations are `const string` fields on a static `Migrations` class, numbered with a three-digit
suffix:

```csharp
internal static class Migrations
{
    internal static readonly IReadOnlyList<(int Version, string Sql)> All = new[]
    {
        (1, Migration_001),
        (2, Migration_002),
    };

    private const string Migration_001 = """
        -- Initial schema
        CREATE TABLE IF NOT EXISTS schema_version ( ... );
        CREATE TABLE IF NOT EXISTS players ( ... );
        -- ... etc.
        """;

    private const string Migration_002 = """
        -- Add ploy_uses table
        CREATE TABLE IF NOT EXISTS ploy_uses ( ... );
        """;
}
```

Using C# raw string literals (available since C# 11 / .NET 7) keeps the SQL readable without escape
noise. An embedded resource (`*.sql` file) is an equally valid alternative for very large
migrations.

### 2.4 No-Rollback Policy

There are no `Down` migrations. If a migration fails:

- The transaction is rolled back automatically, leaving the database at the previous version.
- The process exits with a non-zero exit code and a human-readable message.
- A developer must fix the migration SQL and re-deploy.

This matches the complexity budget: Kill Team tracking is a single-user local app and not a
multi-tenant service. The simplicity of one-direction migrations outweighs the risk.

### 2.5 `DatabaseInitialiser` Class Skeleton

```csharp
public class DatabaseInitialiser
{
    private readonly string _connectionString;

    public DatabaseInitialiser(string connectionString)
    {
        _connectionString = connectionString;
    }

    /// <summary>
    /// Called once at app startup. Opens the database, applies all pending migrations
    /// in version order, and closes the connection.
    /// </summary>
    public void Initialise()
    {
        using var conn = new SqliteConnection(_connectionString);
        conn.Open();

        using var pragma = conn.CreateCommand();
        pragma.CommandText = "PRAGMA foreign_keys = ON";
        pragma.ExecuteNonQuery();

        var currentVersion = GetCurrentVersion(conn);

        foreach (var (version, sql) in Migrations.All.Where(m => m.Version > currentVersion))
        {
            ApplyMigration(conn, version, sql);
        }
    }

    private int GetCurrentVersion(SqliteConnection conn)
    {
        // Check if schema_version table exists first.
        using var check = conn.CreateCommand();
        check.CommandText =
            "SELECT name FROM sqlite_master WHERE type='table' AND name='schema_version'";
        var exists = check.ExecuteScalar() is not null;
        if (!exists) return 0;

        using var read = conn.CreateCommand();
        read.CommandText = "SELECT version FROM schema_version LIMIT 1";
        var result = read.ExecuteScalar();
        return result is null ? 0 : Convert.ToInt32(result);
    }

    private void ApplyMigration(SqliteConnection conn, int version, string sql)
    {
        using var tx = conn.BeginTransaction();
        try
        {
            using var cmd = conn.CreateCommand();
            cmd.Transaction = tx;
            cmd.CommandText = sql;
            cmd.ExecuteNonQuery();

            using var ver = conn.CreateCommand();
            ver.Transaction = tx;
            ver.CommandText = "DELETE FROM schema_version; INSERT INTO schema_version VALUES (@v)";
            ver.Parameters.AddWithValue("@v", version);
            ver.ExecuteNonQuery();

            tx.Commit();
        }
        catch (Exception ex)
        {
            tx.Rollback();
            throw new InvalidOperationException(
                $"Migration {version:D3} failed: {ex.Message}", ex);
        }
    }
}
```

---

## 3. Migration 001 — Initial Schema

This is the complete DDL for the initial schema. All tables are created in a single migration so
that on a fresh database the schema is fully consistent after a single transaction.

> **Note on `PRAGMA foreign_keys`**: This pragma is set per-connection at open time by the
> application code. It is **not** included in the migration SQL, because `PRAGMA` statements inside
> a transaction have no effect in SQLite.

```sql
-- ─────────────────────────────────────────────────────────────────────────────
-- Migration 001: Initial schema
-- ─────────────────────────────────────────────────────────────────────────────

-- Version tracking (single row, managed by DatabaseInitialiser)
CREATE TABLE IF NOT EXISTS schema_version (
    version INTEGER NOT NULL
);

-- ─── Static domain data ─────────────────────────────────────────────────────

CREATE TABLE IF NOT EXISTS players (
    id   TEXT PRIMARY KEY,
    name TEXT NOT NULL COLLATE NOCASE,
    CONSTRAINT uq_players_name UNIQUE (name)
);

CREATE TABLE IF NOT EXISTS kill_teams (
    id      TEXT PRIMARY KEY,
    name    TEXT NOT NULL,
    faction TEXT NOT NULL
);

CREATE TABLE IF NOT EXISTS operatives (
    id             TEXT PRIMARY KEY,
    kill_team_id   TEXT NOT NULL REFERENCES kill_teams (id) ON DELETE CASCADE,
    name           TEXT NOT NULL,
    operative_type TEXT NOT NULL,
    move           INTEGER NOT NULL DEFAULT 0,
    apl            INTEGER NOT NULL DEFAULT 0,
    wounds         INTEGER NOT NULL DEFAULT 0,
    save           INTEGER NOT NULL DEFAULT 0,
    equipment_json TEXT NOT NULL DEFAULT '[]'
    -- equipment stored as a JSON array of strings, e.g. '["Frag grenades x2"]'
);

CREATE TABLE IF NOT EXISTS weapons (
    id            TEXT PRIMARY KEY,
    operative_id  TEXT NOT NULL REFERENCES operatives (id) ON DELETE CASCADE,
    name          TEXT NOT NULL,
    type          TEXT NOT NULL CHECK (type IN ('Ranged', 'Melee')),
    atk           INTEGER NOT NULL DEFAULT 0,
    hit           INTEGER NOT NULL DEFAULT 0,
    normal_dmg    INTEGER NOT NULL DEFAULT 0,
    critical_dmg  INTEGER NOT NULL DEFAULT 0,
    special_rules TEXT NOT NULL DEFAULT ''
);

-- ─── Game session data ───────────────────────────────────────────────────────

CREATE TABLE IF NOT EXISTS games (
    id                    TEXT PRIMARY KEY,
    played_at             TEXT NOT NULL,
    -- ISO 8601 UTC, e.g. '2025-07-01T10:00:00Z'
    mission_name          TEXT,
    team_a_id             TEXT NOT NULL REFERENCES kill_teams (id),
    team_b_id             TEXT NOT NULL REFERENCES kill_teams (id),
    player_a_id           TEXT NOT NULL REFERENCES players (id),
    player_b_id           TEXT NOT NULL REFERENCES players (id),
    status                TEXT NOT NULL DEFAULT 'InProgress'
                              CHECK (status IN ('InProgress', 'Completed')),
    cp_team_a          INTEGER NOT NULL DEFAULT 2,
    cp_team_b          INTEGER NOT NULL DEFAULT 2,
    winner_team_id        TEXT REFERENCES kill_teams (id),
    victory_points_team_a INTEGER NOT NULL DEFAULT 0,
    victory_points_team_b INTEGER NOT NULL DEFAULT 0
);

CREATE TABLE IF NOT EXISTS turning_points (
    id                       TEXT PRIMARY KEY,
    game_id                  TEXT NOT NULL REFERENCES games (id) ON DELETE CASCADE,
    number                   INTEGER NOT NULL CHECK (number BETWEEN 1 AND 4),
    team_with_initiative_id  TEXT NOT NULL REFERENCES kill_teams (id),
    cp_team_a                INTEGER NOT NULL DEFAULT 0,
    cp_team_b                INTEGER NOT NULL DEFAULT 0,
    is_strategy_phase_complete  INTEGER NOT NULL DEFAULT 0
);

CREATE TABLE IF NOT EXISTS activations (
    id                 TEXT PRIMARY KEY,
    turning_point_id   TEXT NOT NULL REFERENCES turning_points (id) ON DELETE CASCADE,
    sequence_number    INTEGER NOT NULL,
    operative_id       TEXT NOT NULL REFERENCES operatives (id),
    team_id            TEXT NOT NULL REFERENCES kill_teams (id),
    order_selected     TEXT NOT NULL CHECK (order_selected IN ('Engage', 'Conceal')),
    is_counteract      INTEGER NOT NULL DEFAULT 0,
    -- 1 = this was a Counteract activation (opponent activated twice in a row)
    is_guard_interrupt INTEGER NOT NULL DEFAULT 0,
    -- 1 = this activation was injected by a Guard interrupt, not normal alternation
    narrative_note     TEXT
);

CREATE TABLE IF NOT EXISTS actions (
    id                    TEXT PRIMARY KEY,
    activation_id         TEXT NOT NULL REFERENCES activations (id) ON DELETE CASCADE,
    type                  TEXT NOT NULL CHECK (type IN (
                              'Reposition', 'Dash', 'FallBack', 'Charge',
                              'Shoot', 'Fight', 'Guard', 'Other'
                          )),
    ap_cost               INTEGER NOT NULL DEFAULT 0,
    target_operative_id   TEXT REFERENCES operatives (id),
    weapon_id             TEXT REFERENCES weapons (id),
    attacker_dice         TEXT,
    -- nullable; JSON array of ints, e.g. '[6,4,3,1]'
    defender_dice         TEXT,
    -- nullable; JSON array of ints
    target_in_cover       INTEGER,
    -- nullable; 0=no cover, 1=in cover
    is_obscured           INTEGER,
    -- nullable; 0=visible, 1=obscured (from Shoot spike)
    normal_hits           INTEGER NOT NULL DEFAULT 0,
    critical_hits         INTEGER NOT NULL DEFAULT 0,
    blocks                INTEGER NOT NULL DEFAULT 0,
    normal_damage_dealt   INTEGER NOT NULL DEFAULT 0,
    critical_damage_dealt INTEGER NOT NULL DEFAULT 0,
    self_damage_dealt     INTEGER NOT NULL DEFAULT 0,
    -- damage dealt to the attacker (e.g. Hot special rule)
    stun_applied          INTEGER NOT NULL DEFAULT 0,
    -- 1 = Stun token applied to target (crit retained from Shoot spike)
    caused_incapacitation INTEGER NOT NULL DEFAULT 0,
    -- 1 = target was incapacitated by this action
    narrative_note        TEXT
);

-- ─── Per-game operative state ────────────────────────────────────────────────

CREATE TABLE IF NOT EXISTS game_operative_states (
    id                                   TEXT PRIMARY KEY,
    game_id                              TEXT NOT NULL REFERENCES games (id) ON DELETE CASCADE,
    operative_id                         TEXT NOT NULL REFERENCES operatives (id),
    current_wounds                       INTEGER NOT NULL,
    "order"                              TEXT NOT NULL DEFAULT 'Conceal'
                                             CHECK ("order" IN ('Engage', 'Conceal')),
    is_ready                             INTEGER NOT NULL DEFAULT 1,
    is_on_guard                          INTEGER NOT NULL DEFAULT 0,
    is_incapacitated                     INTEGER NOT NULL DEFAULT 0,
    has_used_counteract_this_turning_point INTEGER NOT NULL DEFAULT 0,
    apl_modifier                         INTEGER NOT NULL DEFAULT 0,
    -- negative = Stun penalty; positive = buff (e.g. from ploy)
    CONSTRAINT uq_game_operative_states UNIQUE (game_id, operative_id)
);

-- ─── Indexes ─────────────────────────────────────────────────────────────────

-- Activations are frequently fetched in sequence order within a turning point.
CREATE INDEX IF NOT EXISTS idx_activations_turning_point
    ON activations (turning_point_id, sequence_number);

-- Actions are always fetched by activation.
CREATE INDEX IF NOT EXISTS idx_actions_activation
    ON actions (activation_id);

-- Operative states are always looked up by game; the UNIQUE constraint already
-- creates an index, but an explicit composite index improves query planning.
CREATE INDEX IF NOT EXISTS idx_game_operative_states_game
    ON game_operative_states (game_id, operative_id);
```

### 3.1 Column-Type Rationale

| Concern | Choice | Reason |
|---|---|---|
| Primary keys | `TEXT` (GUID string) | Matches the domain model; GUIDs are generated in C# before insert, enabling optimistic concurrency without round-trips. |
| Datetimes | `TEXT` (ISO 8601 UTC) | SQLite has no native datetime type; ISO 8601 strings sort correctly and are unambiguous. |
| Booleans (mandatory) | `INTEGER NOT NULL DEFAULT 0` | SQLite has no BOOLEAN type. `0`/`1` integers are idiomatic. |
| Booleans (nullable) | `INTEGER` (no NOT NULL, no DEFAULT) | `NULL` represents "not applicable / unknown". E.g. `target_in_cover` is NULL for a Fight action. |
| JSON blobs | `TEXT` | `attacker_dice`, `defender_dice`, `equipment_json` are small arrays. Storing as JSON TEXT avoids a join table and is sufficient for the query patterns in this app. |
| Enum-like columns | `TEXT` + `CHECK` | Self-documenting and enforced at the DB layer, preventing stale enums slipping in via migrations. |

### 3.2 Foreign-Key Cascade Summary

| Child table | Parent | On Delete |
|---|---|---|
| `operatives` | `kill_teams` | CASCADE — deleting a team removes all its operatives and their weapons. |
| `weapons` | `operatives` | CASCADE — deleting an operative removes its weapons. |
| `turning_points` | `games` | CASCADE |
| `activations` | `turning_points` | CASCADE |
| `actions` | `activations` | CASCADE |
| `game_operative_states` | `games` | CASCADE |
| `game_operative_states` | `operatives` | **No cascade** — operative is a static record; the game state row is cleared by the game cascade. |
| `games` | `kill_teams`, `players`, `winner_team_id` | **No cascade** — teams and players are static; games reference them. Deleting a team does not retroactively delete game history. |

---

## 4. Migration 002 — (Reserved / Example)

The following illustrates how a future migration is registered. This migration adds a `ploy_uses`
table to track which Command Phase ploys each team used each Turning Point.

```csharp
private const string Migration_002 = """
    CREATE TABLE IF NOT EXISTS ploy_uses (
        id               TEXT PRIMARY KEY,
        turning_point_id TEXT NOT NULL REFERENCES turning_points (id) ON DELETE CASCADE,
        team_id          TEXT NOT NULL REFERENCES kill_teams (id),
        ploy_name        TEXT NOT NULL,
        description      TEXT NULL,
        cp_cost          INTEGER NOT NULL DEFAULT 1
    );

    CREATE INDEX IF NOT EXISTS idx_ploy_uses_turning_point
        ON ploy_uses (turning_point_id, team_id);
    """;
```

And it is registered alongside `Migration_001` in `Migrations.All`:

```csharp
internal static readonly IReadOnlyList<(int Version, string Sql)> All = new[]
{
    (1, Migration_001),
    (2, Migration_002),   // ← append; never change the version number of an existing entry
};
```

**Rule**: once a migration is committed and released, its `Version` number and SQL content are
frozen. If a change is needed, add a new migration — never edit an existing one.

---

## 5. Repository Interfaces (Domain Project)

These interfaces live in `KillTeamAgent.Domain`. Implementations are in `KillTeamAgent.Console`
(or a future `KillTeamAgent.Infrastructure` project). Each implementation receives a
`SqliteConnection` (or a connection factory) via constructor injection.

All async methods use `CancellationToken` defaulting to `default` for ergonomic call sites in the
Spectre.Console command loop.

### 5.1 `IPlayerRepository`

```csharp
public interface IPlayerRepository
{
    /// <summary>Persists a new player. Throws if name already exists (UNIQUE constraint).</summary>
    Task AddAsync(Player player, CancellationToken ct = default);

    /// <summary>Returns all players ordered by name.</summary>
    Task<IReadOnlyList<Player>> GetAllAsync(CancellationToken ct = default);

    /// <summary>Returns the player with the given name (case-insensitive), or null if not found.</summary>
    Task<Player?> FindByNameAsync(string name, CancellationToken ct = default);

    /// <summary>Deletes the player with the given id. No-op if not found.</summary>
    Task DeleteAsync(Guid id, CancellationToken ct = default);
}
```

### 5.2 `IKillTeamRepository`

```csharp
public interface IKillTeamRepository
{
    /// <summary>
    /// Inserts the team if it does not already exist (matched by name, case-sensitive).
    /// If the team exists, updates its faction. Returns the persisted team (with its id).
    /// Operatives on the team are upserted in the same call.
    /// </summary>
    Task<KillTeam> UpsertAsync(KillTeam team, CancellationToken ct = default);

    /// <summary>Returns all kill teams with their operatives and weapons populated.</summary>
    Task<IReadOnlyList<KillTeam>> GetAllAsync(CancellationToken ct = default);

    /// <summary>Returns the team matching the given name, or null if not found.</summary>
    Task<KillTeam?> FindByNameAsync(string name, CancellationToken ct = default);
}
```

### 5.3 `IGameRepository`

```csharp
public interface IGameRepository
{
    /// <summary>Inserts a new game record with status 'InProgress'.</summary>
    Task<Game> CreateAsync(Game game, CancellationToken ct = default);

    /// <summary>Returns the full game record for the given id, or null if not found.</summary>
    Task<Game?> GetByIdAsync(Guid id, CancellationToken ct = default);

    /// <summary>Updates the status, winner_team_id, and victory point totals of an existing game.</summary>
    Task UpdateStatusAsync(Guid id, string status, Guid? winnerTeamId,
        int vpTeamA, int vpTeamB, CancellationToken ct = default);

    /// <summary>Returns all completed games ordered by played_at descending.</summary>
    Task<IReadOnlyList<Game>> GetCompletedAsync(CancellationToken ct = default);
}
```

### 5.4 `IGameOperativeStateRepository`

```csharp
public interface IGameOperativeStateRepository
{
    /// <summary>
    /// Creates one GameOperativeState row per operative across both teams.
    /// Sets current_wounds to the operative's full wounds value.
    /// Sets order to 'Conceal', is_ready = 1, all flags = 0, apl_modifier = 0.
    /// </summary>
    Task InitialiseForGameAsync(Guid gameId, IEnumerable<Operative> allOperatives,
        CancellationToken ct = default);

    /// <summary>Returns all operative states for the given game.</summary>
    Task<IReadOnlyList<GameOperativeState>> GetByGameAsync(Guid gameId,
        CancellationToken ct = default);

    /// <summary>Persists changes to a single operative state.</summary>
    Task UpdateAsync(GameOperativeState state, CancellationToken ct = default);

    /// <summary>
    /// Persists changes to multiple operative states in a single transaction.
    /// Used at Turning Point boundaries (reset is_ready, clear is_on_guard, etc.).
    /// </summary>
    Task UpdateManyAsync(IEnumerable<GameOperativeState> states,
        CancellationToken ct = default);
}
```

### 5.5 `IActionRepository`

```csharp
public interface IActionRepository
{
    /// <summary>Persists a new action record.</summary>
    Task AddAsync(KillTeamAction action, CancellationToken ct = default);

    /// <summary>Returns all actions for the given activation, ordered by their insert order.</summary>
    Task<IReadOnlyList<KillTeamAction>> GetByActivationAsync(Guid activationId,
        CancellationToken ct = default);
}
```

> **Note**: The domain model class is named `KillTeamAction` (not `Action`) to avoid collision with
> `System.Action`.

### 5.6 `IActivationRepository`

```csharp
public interface IActivationRepository
{
    Task<Activation> CreateAsync(Activation activation);
    Task<IEnumerable<Activation>> GetByTurningPointAsync(Guid turningPointId);
    Task UpdateNarrativeAsync(Guid activationId, string? note);
}
```

> Used by `FirefightPhaseOrchestrator` (see spike-firefight-loop §3).

### 5.7 `IBlastTargetRepository`

```csharp
public interface IBlastTargetRepository
{
    Task CreateAsync(BlastTarget target);
    Task<IEnumerable<BlastTarget>> GetByActionIdAsync(Guid actionId);
}
```

> `BlastTarget` maps to the `action_blast_targets` table (Migration 003, defined in spike-blast-torrent.md §5). Used by `BlastResolutionService`.

---

## 6. TestDbBuilder

`TestDbBuilder` lives in `KillTeamAgent.Tests` and is the single entry point for constructing a
populated in-memory database in every test that needs persistence.

```csharp
/// <summary>
/// Fluent builder that creates an isolated in-memory SQLite database,
/// applies the full schema (all migrations), and seeds test fixtures.
/// One builder instance = one isolated database connection.
/// </summary>
public sealed class TestDbBuilder : IDisposable
{
    private readonly SqliteConnection _conn;

    private TestDbBuilder()
    {
        _conn = new SqliteConnection("Data Source=:memory:");
        _conn.Open();

        using var pragma = _conn.CreateCommand();
        pragma.CommandText = "PRAGMA foreign_keys = ON";
        pragma.ExecuteNonQuery();

        // Apply all migrations using the real DatabaseInitialiser logic.
        var initialiser = new DatabaseInitialiser("Data Source=:memory:");
        // We bypass the connection-string path and pass the open connection directly
        // via an internal overload used only in tests.
        DatabaseInitialiser.ApplyAllMigrations(_conn);
    }

    /// <summary>Creates an in-memory database with the full schema applied.</summary>
    public static TestDbBuilder Create() => new();

    /// <summary>The open connection to pass into repository constructors.</summary>
    public SqliteConnection Connection => _conn;

    public void Dispose() => _conn.Dispose();

    // ── Seed helpers — each returns `this` for fluent chaining ───────────────

    public TestDbBuilder WithPlayer(Guid id, string name)
    {
        Exec("INSERT INTO players (id, name) VALUES (@id, @name)",
            ("@id", id.ToString()), ("@name", name));
        return this;
    }

    public TestDbBuilder WithKillTeam(Guid id, string name, string faction)
    {
        Exec("INSERT INTO kill_teams (id, name, faction) VALUES (@id, @name, @faction)",
            ("@id", id.ToString()), ("@name", name), ("@faction", faction));
        return this;
    }

    public TestDbBuilder WithOperative(Guid id, Guid teamId, string name,
        int wounds, int save, int apl, int move)
    {
        Exec("""
            INSERT INTO operatives
                (id, kill_team_id, name, operative_type, move, apl, wounds, save)
            VALUES (@id, @teamId, @name, @name, @move, @apl, @wounds, @save)
            """,
            ("@id", id.ToString()), ("@teamId", teamId.ToString()), ("@name", name),
            ("@move", move), ("@apl", apl), ("@wounds", wounds), ("@save", save));
        return this;
    }

    public TestDbBuilder WithWeapon(Guid id, Guid operativeId, string name,
        string type, int atk, int hit, int normalDmg, int critDmg,
        string specialRules = "")
    {
        Exec("""
            INSERT INTO weapons
                (id, operative_id, name, type, atk, hit, normal_dmg, critical_dmg, special_rules)
            VALUES (@id, @opId, @name, @type, @atk, @hit, @nd, @cd, @sr)
            """,
            ("@id", id.ToString()), ("@opId", operativeId.ToString()), ("@name", name),
            ("@type", type), ("@atk", atk), ("@hit", hit), ("@nd", normalDmg),
            ("@cd", critDmg), ("@sr", specialRules));
        return this;
    }

    public TestDbBuilder WithGame(Guid id, Guid teamAId, Guid teamBId,
        Guid playerAId, Guid playerBId, string status = "InProgress")
    {
        Exec("""
            INSERT INTO games
                (id, played_at, team_a_id, team_b_id, player_a_id, player_b_id, status)
            VALUES (@id, @at, @ta, @tb, @pa, @pb, @st)
            """,
            ("@id", id.ToString()), ("@at", DateTime.UtcNow.ToString("o")),
            ("@ta", teamAId.ToString()), ("@tb", teamBId.ToString()),
            ("@pa", playerAId.ToString()), ("@pb", playerBId.ToString()),
            ("@st", status));
        return this;
    }

    public TestDbBuilder WithTurningPoint(Guid id, Guid gameId, int number, bool strategyPhaseComplete = false)
    {
        // team_with_initiative_id intentionally omitted — use a separate WithKillTeam call
        // and pass the real id when needed. Here we reuse the game's team_a_id for brevity.
        Exec("""
            INSERT INTO turning_points
                (id, game_id, number, team_with_initiative_id, is_strategy_phase_complete)
            SELECT @id, @gid, @num, team_a_id, @spc FROM games WHERE id = @gid
            """,
            ("@id", id.ToString()), ("@gid", gameId.ToString()), ("@num", number),
            ("@spc", strategyPhaseComplete ? 1 : 0));
        return this;
    }

    public TestDbBuilder WithActivation(Guid id, Guid turningPointId, int seq,
        Guid operativeId, Guid teamId, string order = "Engage")
    {
        Exec("""
            INSERT INTO activations
                (id, turning_point_id, sequence_number, operative_id, team_id, order_selected)
            VALUES (@id, @tpid, @seq, @opid, @tid, @ord)
            """,
            ("@id", id.ToString()), ("@tpid", turningPointId.ToString()), ("@seq", seq),
            ("@opid", operativeId.ToString()), ("@tid", teamId.ToString()), ("@ord", order));
        return this;
    }

    public TestDbBuilder WithGameOperativeState(Guid id, Guid gameId,
        Guid operativeId, int currentWounds, string order = "Engage")
    {
        Exec("""
            INSERT INTO game_operative_states
                (id, game_id, operative_id, current_wounds, "order")
            VALUES (@id, @gid, @opid, @cw, @ord)
            """,
            ("@id", id.ToString()), ("@gid", gameId.ToString()),
            ("@opid", operativeId.ToString()), ("@cw", currentWounds), ("@ord", order));
        return this;
    }

    // ── Internal helper ───────────────────────────────────────────────────────

    private void Exec(string sql, params (string Name, object Value)[] parameters)
    {
        using var cmd = _conn.CreateCommand();
        cmd.CommandText = sql;
        foreach (var (name, value) in parameters)
            cmd.Parameters.AddWithValue(name, value);
        cmd.ExecuteNonQuery();
    }
}
```

### 6.1 Usage Example

```csharp
var playerId  = Guid.NewGuid();
var teamId    = Guid.NewGuid();
var opId      = Guid.NewGuid();
var gameId    = Guid.NewGuid();

using var db = TestDbBuilder.Create()
    .WithPlayer(playerId, "Michael")
    .WithKillTeam(teamId, "Angels of Death", "Adeptus Astartes")
    .WithOperative(opId, teamId, "Assault Intercessor", wounds: 13, save: 3, apl: 3, move: 3)
    .WithGame(gameId, teamId, teamId, playerId, playerId);

var repo = new GameOperativeStateRepository(db.Connection);
// ... act and assert
```

The builder owns the `SqliteConnection` lifetime. Tests that need to assert on raw SQL can access
`db.Connection` directly.

---

## 7. xUnit Tests

All tests are in `KillTeamAgent.Tests`. Naming follows `ClassName_Scenario_ExpectedResult`.

```csharp
// ─── DatabaseInitialiser ─────────────────────────────────────────────────────

[Fact]
public void DatabaseInitialiser_Initialise_CreatesAllTables()
{
    // Arrange
    using var db = TestDbBuilder.Create();

    // Act — schema already applied by TestDbBuilder; query sqlite_master directly.
    string[] expectedTables =
    [
        "schema_version", "players", "kill_teams", "operatives", "weapons",
        "games", "turning_points", "activations", "actions", "game_operative_states"
    ];

    // Assert
    // For each name in expectedTables:
    //   SELECT name FROM sqlite_master WHERE type='table' AND name=?
    //   → result must be non-null (table exists).
}

[Fact]
public void DatabaseInitialiser_Initialise_IsIdempotent()
{
    // Arrange
    using var conn = new SqliteConnection("Data Source=:memory:");
    conn.Open();
    var initialiser = new DatabaseInitialiser("Data Source=:memory:");

    // Act
    DatabaseInitialiser.ApplyAllMigrations(conn); // first call
    DatabaseInitialiser.ApplyAllMigrations(conn); // second call — must not throw

    // Assert
    // SELECT version FROM schema_version → equals 1 (or highest migration number).
    // No exception was thrown.
}

// ─── PlayerRepository ────────────────────────────────────────────────────────

[Fact]
public async Task PlayerRepository_Add_PersistsPlayer()
{
    // Arrange
    using var db = TestDbBuilder.Create();
    var repo = new PlayerRepository(db.Connection);
    var player = new Player(Guid.NewGuid(), "Michael");

    // Act
    await repo.AddAsync(player);

    // Assert
    var found = await repo.FindByNameAsync("Michael");
    // found must not be null
    // found.Id must equal player.Id
    // found.Name must equal "Michael"
}

[Fact]
public async Task PlayerRepository_Add_DuplicateName_Throws()
{
    // Arrange
    using var db = TestDbBuilder.Create();
    var repo = new PlayerRepository(db.Connection);
    await repo.AddAsync(new Player(Guid.NewGuid(), "Solomon"));

    // Act + Assert
    // Adding a second player named "Solomon" (or "solomon" — COLLATE NOCASE)
    // must throw an exception (SqliteException with SQLITE_CONSTRAINT error code,
    // or a domain-specific DuplicatePlayerException wrapping it).
    await Assert.ThrowsAsync<SqliteException>(
        () => repo.AddAsync(new Player(Guid.NewGuid(), "Solomon")));
}

// ─── KillTeamRepository ──────────────────────────────────────────────────────

[Fact]
public async Task KillTeamRepository_Upsert_CreatesOnFirstCall()
{
    // Arrange
    using var db = TestDbBuilder.Create();
    var repo = new KillTeamRepository(db.Connection);
    var team = new KillTeam(Guid.NewGuid(), "Angels of Death", "Adeptus Astartes", []);

    // Act
    await repo.UpsertAsync(team);

    // Assert
    // SELECT COUNT(*) FROM kill_teams → 1
    // FindByNameAsync("Angels of Death") → non-null with correct faction
}

[Fact]
public async Task KillTeamRepository_Upsert_UpdatesOnSecondCall()
{
    // Arrange
    using var db = TestDbBuilder.Create();
    var repo = new KillTeamRepository(db.Connection);
    var team = new KillTeam(Guid.NewGuid(), "Angels of Death", "Adeptus Astartes", []);
    await repo.UpsertAsync(team);

    // Act — same name, updated faction
    var updated = team with { Faction = "Space Marines" };
    await repo.UpsertAsync(updated);

    // Assert
    // SELECT COUNT(*) FROM kill_teams → still 1 (no duplicate row)
    // FindByNameAsync("Angels of Death").Faction → "Space Marines"
}

// ─── GameRepository ──────────────────────────────────────────────────────────

[Fact]
public async Task GameRepository_Create_PersistsGame()
{
    // Arrange
    using var db = TestDbBuilder.Create()
        .WithPlayer(Guid.NewGuid(), "Michael")
        .WithPlayer(Guid.NewGuid(), "Solomon")
        .WithKillTeam(/* ... */);
    var repo = new GameRepository(db.Connection);
    var game = new Game(/* ... */) { Status = "InProgress" };

    // Act
    var created = await repo.CreateAsync(game);

    // Assert
    var found = await repo.GetByIdAsync(created.Id);
    // found must not be null
    // found.Status must equal "InProgress"
    // found.PlayedAt must be within a few seconds of now (UTC)
}

// ─── GameOperativeStateRepository ────────────────────────────────────────────

[Fact]
public async Task GameOperativeStateRepository_InitialiseForGame_CreatesStatePerOperative()
{
    // Arrange — one game, one team, three operatives all with 13 wounds
    using var db = TestDbBuilder.Create()
        /* .WithPlayer, .WithKillTeam, .WithOperative (×3), .WithGame */;
    var repo = new GameOperativeStateRepository(db.Connection);

    // Act
    await repo.InitialiseForGameAsync(gameId, threeOperatives);

    // Assert
    var states = await repo.GetByGameAsync(gameId);
    // states.Count must equal 3
    // each state.CurrentWounds must equal 13
    // each state.IsReady must be true
    // each state.IsOnGuard must be false
    // each state.IsIncapacitated must be false
    // each state.AplModifier must be 0
}

// ─── ActionRepository ────────────────────────────────────────────────────────

[Fact]
public async Task ActionRepository_Add_PersistsShootAction()
{
    // Arrange
    using var db = TestDbBuilder.Create()
        /* .WithPlayer, .WithKillTeam, .WithOperative (shooter + target),
           .WithWeapon, .WithGame, .WithTurningPoint, .WithActivation */;
    var repo = new ActionRepository(db.Connection);

    var action = new KillTeamAction
    {
        Id                  = Guid.NewGuid(),
        ActivationId        = activationId,
        Type                = "Shoot",
        ApCost              = 1,
        TargetOperativeId   = targetOpId,
        WeaponId            = weaponId,
        AttackerDice        = "[6,4,3,1]",
        DefenderDice        = "[5,2]",
        TargetInCover       = false,
        IsObscured          = false,
        NormalHits          = 1,
        CriticalHits        = 1,
        Blocks              = 1,
        NormalDamageDealt   = 3,
        CriticalDamageDealt = 4,
        SelfDamageDealt     = 0,
        StunApplied         = true,
        CausedIncapacitation = false,
    };

    // Act
    await repo.AddAsync(action);

    // Assert
    var results = await repo.GetByActivationAsync(activationId);
    var saved   = results.Single();
    // saved.AttackerDice must equal "[6,4,3,1]"
    // saved.DefenderDice must equal "[5,2]"
    // saved.CriticalHits must equal 1
    // saved.StunApplied must be true
    // saved.IsObscured must be false
    // saved.SelfDamageDealt must equal 0
}

// ─── TestDbBuilder ───────────────────────────────────────────────────────────

[Fact]
public void TestDbBuilder_WithPlayer_SeedsCorrectly()
{
    // Arrange
    var playerId = Guid.NewGuid();

    // Act
    using var db = TestDbBuilder.Create()
        .WithPlayer(playerId, "Michael");

    // Assert — query the raw connection directly, bypassing the repository
    using var cmd = db.Connection.CreateCommand();
    cmd.CommandText = "SELECT id, name FROM players WHERE id = @id";
    cmd.Parameters.AddWithValue("@id", playerId.ToString());
    using var reader = cmd.ExecuteReader();
    // reader.Read() must return true
    // reader.GetString(0) must equal playerId.ToString()
    // reader.GetString(1) must equal "Michael"
}
```

---

## 8. Schema Diagram (ASCII)

```
players
  │
  ├─(player_a_id, player_b_id)──────────────────────────┐
  │                                                      ▼
  │                                                   games
  │                                                      │
kill_teams                                               │
  │                                                      │
  ├─(id)──────(team_a_id, team_b_id, winner_team_id)─────┘
  │                                                      │
  └──< operatives                                        └──< turning_points
           │                                                       │
           └──< weapons                                            └──< activations
                                                                           │
                                                                           └──< actions
                                                                                  │
                                                                                  ├─(target_operative_id)──> operatives
                                                                                  └─(weapon_id)──────────> weapons

games ──────────────────────────────────< game_operative_states >──── operatives
```

**Key:**
- `──<` one-to-many (parent ──< children)
- `>──` many-to-one (FK reference)
- `ON DELETE CASCADE` applies on all `──<` relationships except `games → kill_teams/players` and
  `game_operative_states → operatives` (those are reference-only FKs with no cascade).

---

## 9. Implementation Notes

### 9.1 Embedding Migration SQL as C# Constants

Use C# 11 raw string literals for clean, escape-free SQL:

```csharp
private const string Migration_001 = """
    CREATE TABLE IF NOT EXISTS players (
        id   TEXT PRIMARY KEY,
        name TEXT NOT NULL COLLATE NOCASE,
        CONSTRAINT uq_players_name UNIQUE (name)
    );
    """;
```

For very large migrations (>200 lines) prefer an embedded resource:

```
KillTeamAgent.Console/
  Infrastructure/
    Migrations/
      Migration_001.sql   ← Build Action: Embedded Resource
```

```csharp
private static string LoadSql(string name)
{
    var assembly = Assembly.GetExecutingAssembly();
    using var stream = assembly.GetManifestResourceStream(
        $"KillTeamAgent.Console.Infrastructure.Migrations.{name}.sql")!;
    using var reader = new StreamReader(stream);
    return reader.ReadToEnd();
}
```

### 9.2 Checking Whether a Table Exists

Use `sqlite_master` (also aliased as `sqlite_schema` in newer SQLite versions):

```csharp
using var cmd = conn.CreateCommand();
cmd.CommandText =
    "SELECT name FROM sqlite_master WHERE type = 'table' AND name = @name";
cmd.Parameters.AddWithValue("@name", "schema_version");
var exists = cmd.ExecuteScalar() is not null;
```

This is the canonical technique used by `DatabaseInitialiser.GetCurrentVersion` and by test
assertions in `DatabaseInitialiser_Initialise_CreatesAllTables`.

### 9.3 DI Registration

`DatabaseInitialiser` is registered as a **singleton** so the startup call happens exactly once:

```csharp
// In Program.cs, before CommandApp.Run(args):
var services = new ServiceCollection();

var dbPath = config["KillTeamAgent:DatabasePath"] ?? "./data/kill-team.db";
var connStr = $"Data Source={dbPath}";

services.AddSingleton(new DatabaseInitialiser(connStr));
// ... other registrations

var provider = services.BuildServiceProvider();

// Run migrations before the command app starts.
provider.GetRequiredService<DatabaseInitialiser>().Initialise();

var app = new CommandApp(new TypeRegistrar(services));
app.Configure(config => { /* register commands */ });
return app.Run(args);
```

### 9.4 Known SQLite Limitation: `ALTER TABLE ADD COLUMN NOT NULL`

SQLite does **not** allow adding a `NOT NULL` column without a `DEFAULT` value to an existing table:

```sql
-- ❌ Will fail on existing tables:
ALTER TABLE games ADD COLUMN round_count INTEGER NOT NULL;

-- ✅ Must supply a DEFAULT:
ALTER TABLE games ADD COLUMN round_count INTEGER NOT NULL DEFAULT 1;
```

Any migration that adds a new non-nullable column to an existing table **must** supply a `DEFAULT`
value. If no sensible default exists, either use a nullable column or rebuild the table (create new,
copy data, drop old, rename — all within one migration transaction).

### 9.5 Connection Lifecycle

**One connection per unit of work** — not a singleton connection shared across the app lifetime.

```csharp
// ✅ Correct — open, use, dispose:
public async Task<Player?> FindByNameAsync(string name, CancellationToken ct = default)
{
    using var conn = new SqliteConnection(_connectionString);
    await conn.OpenAsync(ct);

    using var pragma = conn.CreateCommand();
    pragma.CommandText = "PRAGMA foreign_keys = ON";
    pragma.ExecuteNonQuery();

    using var cmd = conn.CreateCommand();
    cmd.CommandText = "SELECT id, name FROM players WHERE name = @name COLLATE NOCASE";
    cmd.Parameters.AddWithValue("@name", name);
    // ...
}
```

The `PRAGMA foreign_keys = ON` must be issued on **every** connection open, not just at startup.
SQLite's foreign-key enforcement is a per-connection setting and defaults to `OFF` for backwards
compatibility.

For test repositories that share `TestDbBuilder.Connection`, the single connection is kept open for
the duration of the test and `PRAGMA foreign_keys = ON` is set once in `TestDbBuilder`'s
constructor.

### 9.6 The `order` Column Name

`order` is a reserved word in SQL. The `game_operative_states` table wraps it in double-quotes in
the DDL and in all query strings:

```sql
SELECT "order" FROM game_operative_states WHERE id = @id;
INSERT INTO game_operative_states (..., "order") VALUES (..., @order);
```

Alternatively the column could be renamed `operative_order` to avoid the quoting requirement. The
double-quote approach is chosen here to keep the column name aligned with the domain model property
name `Order`.

### 9.7 `TestDbBuilder` and the `DatabaseInitialiser` Internal API

To avoid duplicating migration logic, `TestDbBuilder` calls into `DatabaseInitialiser` via a
package-internal static helper that accepts an already-open `SqliteConnection`:

```csharp
// Internal, test-visible only:
internal static void ApplyAllMigrations(SqliteConnection conn)
{
    // Same logic as Initialise() but uses the provided connection instead of
    // opening a new one from _connectionString.
}
```

Expose via `[assembly: InternalsVisibleTo("KillTeamAgent.Tests")]` in
`KillTeamAgent.Console/KillTeamAgent.Console.csproj` (or the project that hosts
`DatabaseInitialiser`):

```xml
<ItemGroup>
  <AssemblyAttribute Include="System.Runtime.CompilerServices.InternalsVisibleToAttribute">
    <_Parameter1>KillTeamAgent.Tests</_Parameter1>
  </AssemblyAttribute>
</ItemGroup>
```
