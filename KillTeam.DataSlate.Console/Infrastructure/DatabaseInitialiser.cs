using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Configuration;

namespace KillTeam.DataSlate.Console.Infrastructure;

public class DatabaseInitialiser
{
    private readonly string _connectionString;

    public DatabaseInitialiser(IConfiguration config)
    {
        var path = config["DataSlate:DatabasePath"] ?? "./data/kill-team.db";
        _connectionString = $"Data Source={path}";

        var dir = Path.GetDirectoryName(path);

        if (!string.IsNullOrEmpty(dir))
        {
            Directory.CreateDirectory(dir);
        }
    }

    public async Task InitialiseAsync()
    {
        await using var conn = new SqliteConnection(_connectionString);
        await conn.OpenAsync();

        using var pragma = conn.CreateCommand();
        pragma.CommandText = "PRAGMA foreign_keys = ON";
        pragma.ExecuteNonQuery();

        var currentVersion = await GetCurrentVersionAsync(conn);

        foreach (var (version, sql) in Migrations.All.Where(m => m.Version > currentVersion))
        {
            await ApplyMigrationAsync(conn, version, sql);
        }
    }

    public static void ApplyAllMigrations(SqliteConnection conn)
    {
        var currentVersion = GetCurrentVersionAsync(conn).GetAwaiter().GetResult();
        foreach (var (version, sql) in Migrations.All.Where(m => m.Version > currentVersion))
        {
            ApplyMigrationAsync(conn, version, sql).GetAwaiter().GetResult();
        }
    }

    private static async Task<int> GetCurrentVersionAsync(SqliteConnection conn)
    {
        using var check = conn.CreateCommand();
        check.CommandText = "SELECT name FROM sqlite_master WHERE type='table' AND name='schema_version'";
        var exists = await check.ExecuteScalarAsync() is not null;
        if (!exists)
        {
            return 0;
        }

        using var read = conn.CreateCommand();
        read.CommandText = "SELECT version FROM schema_version LIMIT 1";
        var result = await read.ExecuteScalarAsync();
        return result is null ? 0 : Convert.ToInt32(result);
    }

    private static async Task ApplyMigrationAsync(SqliteConnection conn, int version, string sql)
    {
        using var tx = conn.BeginTransaction();
        try
        {
            using var cmd = conn.CreateCommand();
            cmd.Transaction = tx;
            cmd.CommandText = sql;
            await cmd.ExecuteNonQueryAsync();

            using var ver = conn.CreateCommand();
            ver.Transaction = tx;
            ver.CommandText = "DELETE FROM schema_version; INSERT INTO schema_version VALUES (@v)";
            ver.Parameters.AddWithValue("@v", version);
            await ver.ExecuteNonQueryAsync();

            tx.Commit();
        }
        catch (Exception ex)
        {
            tx.Rollback();
            throw new InvalidOperationException($"Migration {version:D3} failed: {ex.Message}", ex);
        }
    }
}

internal static class Migrations
{
    internal static readonly IReadOnlyList<(int Version, string Sql)> All =
    [
        (1, Migration_001),
        (2, Migration_002),
        (3, Migration_003),
        (4, Migration_004),
    ];

    private const string Migration_001 = """
        CREATE TABLE IF NOT EXISTS schema_version (
            version INTEGER NOT NULL
        );

        CREATE TABLE IF NOT EXISTS players (
            id   TEXT PRIMARY KEY,
            name TEXT NOT NULL COLLATE NOCASE,
            CONSTRAINT uq_players_name UNIQUE (name)
        );

        CREATE TABLE IF NOT EXISTS teams (
            id      TEXT PRIMARY KEY,
            name    TEXT NOT NULL UNIQUE COLLATE NOCASE,
            faction TEXT NOT NULL
        );

        CREATE TABLE IF NOT EXISTS operatives (
            id          TEXT PRIMARY KEY,
            team_id     TEXT NOT NULL REFERENCES teams (id) ON DELETE CASCADE,
            name        TEXT NOT NULL,
            operative_type TEXT NOT NULL,
            move           INTEGER NOT NULL DEFAULT 0,
            apl            INTEGER NOT NULL DEFAULT 0,
            wounds         INTEGER NOT NULL DEFAULT 0,
            save           INTEGER NOT NULL DEFAULT 0,
            equipment_json TEXT NOT NULL DEFAULT '[]'
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

        CREATE TABLE IF NOT EXISTS games (
            id                          TEXT PRIMARY KEY,
            played_at                   TEXT NOT NULL,
            mission_name                TEXT,
            participant1_team_id        TEXT NOT NULL,
            participant1_team_name      TEXT NOT NULL,
            participant2_team_id        TEXT NOT NULL,
            participant2_team_name      TEXT NOT NULL,
            participant1_player_id      TEXT NOT NULL REFERENCES players (id),
            participant2_player_id      TEXT NOT NULL REFERENCES players (id),
            status                      TEXT NOT NULL DEFAULT 'InProgress'
                                            CHECK (status IN ('InProgress', 'Completed')),
            participant1_command_points INTEGER NOT NULL DEFAULT 2,
            participant2_command_points INTEGER NOT NULL DEFAULT 2,
            winner_team_id              TEXT,
            participant1_victory_points INTEGER NOT NULL DEFAULT 0,
            participant2_victory_points INTEGER NOT NULL DEFAULT 0
        );

        CREATE TABLE IF NOT EXISTS turning_points (
            id                          TEXT PRIMARY KEY,
            game_id                     TEXT NOT NULL REFERENCES games (id) ON DELETE CASCADE,
            number                      INTEGER NOT NULL CHECK (number BETWEEN 1 AND 4),
            team_with_initiative_id     TEXT,
            command_points_participant1 INTEGER NOT NULL DEFAULT 0,
            command_points_participant2 INTEGER NOT NULL DEFAULT 0,
            is_strategy_phase_complete  INTEGER NOT NULL DEFAULT 0
        );

        CREATE TABLE IF NOT EXISTS activations (
            id                 TEXT PRIMARY KEY,
            turning_point_id   TEXT NOT NULL REFERENCES turning_points (id) ON DELETE CASCADE,
            sequence_number    INTEGER NOT NULL,
            operative_id       TEXT NOT NULL REFERENCES operatives (id),
            team_id            TEXT NOT NULL,
            order_selected     TEXT NOT NULL CHECK (order_selected IN ('Engage', 'Conceal')),
            is_counteract      INTEGER NOT NULL DEFAULT 0,
            is_guard_interrupt INTEGER NOT NULL DEFAULT 0,
            narrative_note     TEXT
        );

        CREATE TABLE IF NOT EXISTS actions (
            id                    TEXT PRIMARY KEY,
            activation_id         TEXT NOT NULL REFERENCES activations (id) ON DELETE CASCADE,
            type                  TEXT NOT NULL CHECK (type IN (
                                      'Reposition', 'Dash', 'FallBack', 'Charge',
                                      'Shoot', 'Fight', 'Guard',
                                      'Counteract', 'PickUp', 'UseEquipment', 'UniqueAction',
                                      'Other'
                                  )),
            ap_cost               INTEGER NOT NULL DEFAULT 0,
            target_operative_id   TEXT REFERENCES operatives (id),
            weapon_id             TEXT REFERENCES weapons (id),
            attacker_dice         TEXT,
            defender_dice         TEXT,
            target_in_cover       INTEGER,
            is_obscured           INTEGER,
            normal_hits           INTEGER NOT NULL DEFAULT 0,
            critical_hits         INTEGER NOT NULL DEFAULT 0,
            blocks                INTEGER NOT NULL DEFAULT 0,
            normal_damage_dealt   INTEGER NOT NULL DEFAULT 0,
            critical_damage_dealt INTEGER NOT NULL DEFAULT 0,
            self_damage_dealt     INTEGER NOT NULL DEFAULT 0,
            stun_applied          INTEGER NOT NULL DEFAULT 0,
            caused_incapacitation INTEGER NOT NULL DEFAULT 0,
            narrative_note        TEXT
        );

        CREATE TABLE IF NOT EXISTS game_operative_states (
            id                                     TEXT PRIMARY KEY,
            game_id                                TEXT NOT NULL REFERENCES games (id) ON DELETE CASCADE,
            operative_id                           TEXT NOT NULL REFERENCES operatives (id),
            current_wounds                         INTEGER NOT NULL,
            "order"                                TEXT NOT NULL DEFAULT 'Conceal'
                                                       CHECK ("order" IN ('Engage', 'Conceal')),
            is_ready                               INTEGER NOT NULL DEFAULT 1,
            is_on_guard                            INTEGER NOT NULL DEFAULT 0,
            is_incapacitated                       INTEGER NOT NULL DEFAULT 0,
            has_used_counteract_this_turning_point INTEGER NOT NULL DEFAULT 0,
            apl_modifier                           INTEGER NOT NULL DEFAULT 0,
            CONSTRAINT uq_game_operative_states UNIQUE (game_id, operative_id)
        );

        CREATE INDEX IF NOT EXISTS idx_activations_turning_point
            ON activations (turning_point_id, sequence_number);

        CREATE INDEX IF NOT EXISTS idx_actions_activation
            ON actions (activation_id);

        CREATE INDEX IF NOT EXISTS idx_game_operative_states_game
            ON game_operative_states (game_id, operative_id);
        """;

    private const string Migration_002 = """
        CREATE TABLE IF NOT EXISTS ploy_uses (
            id               TEXT PRIMARY KEY,
            turning_point_id TEXT NOT NULL REFERENCES turning_points (id) ON DELETE CASCADE,
            team_id          TEXT NOT NULL,
            ploy_name        TEXT NOT NULL,
            description      TEXT,
            cp_cost          INTEGER NOT NULL DEFAULT 1
        );

        CREATE INDEX IF NOT EXISTS idx_ploy_uses_turning_point
            ON ploy_uses (turning_point_id, team_id);
        """;

    private const string Migration_003 = """
        CREATE TABLE IF NOT EXISTS action_blast_targets (
            id                    TEXT PRIMARY KEY,
            action_id             TEXT NOT NULL REFERENCES actions (id) ON DELETE CASCADE,
            target_operative_id   TEXT NOT NULL REFERENCES operatives (id),
            operative_name        TEXT NOT NULL DEFAULT '',
            defender_dice         TEXT NOT NULL DEFAULT '[]',
            normal_hits           INTEGER NOT NULL DEFAULT 0,
            critical_hits         INTEGER NOT NULL DEFAULT 0,
            blocks                INTEGER NOT NULL DEFAULT 0,
            normal_damage_dealt   INTEGER NOT NULL DEFAULT 0,
            critical_damage_dealt INTEGER NOT NULL DEFAULT 0,
            caused_incapacitation INTEGER NOT NULL DEFAULT 0
        );

        CREATE INDEX IF NOT EXISTS idx_action_blast_targets_action
            ON action_blast_targets (action_id);
        """;

    private const string Migration_004 = """
        SELECT 1;
        """;
}
