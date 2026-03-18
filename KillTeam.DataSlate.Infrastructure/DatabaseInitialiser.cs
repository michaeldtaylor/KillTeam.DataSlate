using KillTeam.DataSlate.Domain;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Options;

namespace KillTeam.DataSlate.Infrastructure;

public class DatabaseInitialiser
{
    private readonly string _connectionString;

    public DatabaseInitialiser(IOptions<DataSlateOptions> options)
    {
        var path = options.Value.DatabasePath;

        _connectionString = $"Data Source={path}";

        var dir = Path.GetDirectoryName(path);

        if (!string.IsNullOrEmpty(dir))
        {
            Directory.CreateDirectory(dir);
        }
    }

    public async Task InitialiseAsync()
    {
        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync();

        await using var pragmaCommand = connection.CreateCommand();

        pragmaCommand.CommandText = "PRAGMA foreign_keys = ON";
        await pragmaCommand.ExecuteNonQueryAsync();

        var currentVersion = await GetCurrentVersionAsync(connection);

        foreach (var (version, sql) in Migrations.All.Where(m => m.Version > currentVersion))
        {
            await ApplyMigrationAsync(connection, version, sql);
        }
    }

    public static void ApplyAllMigrations(SqliteConnection connection)
    {
        var currentVersion = GetCurrentVersionAsync(connection).GetAwaiter().GetResult();

        foreach (var (version, sql) in Migrations.All.Where(m => m.Version > currentVersion))
        {
            ApplyMigrationAsync(connection, version, sql).GetAwaiter().GetResult();
        }
    }

    private static async Task<int> GetCurrentVersionAsync(SqliteConnection connection)
    {
        await using var checkCommand = connection.CreateCommand();

        checkCommand.CommandText = "SELECT name FROM sqlite_master WHERE type='table' AND name='schema_version'";
        var exists = await checkCommand.ExecuteScalarAsync() is not null;

        if (!exists)
        {
            return 0;
        }

        await using var readCommand = connection.CreateCommand();

        readCommand.CommandText = "SELECT version FROM schema_version LIMIT 1";
        var result = await readCommand.ExecuteScalarAsync();

        return result is null ? 0 : Convert.ToInt32(result);
    }

    private static async Task ApplyMigrationAsync(SqliteConnection connection, int version, string sql)
    {
        await using var transaction = connection.BeginTransaction();
        try
        {
            await using var command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText = sql;
            await command.ExecuteNonQueryAsync();

            await using var versionCommand = connection.CreateCommand();
            versionCommand.Transaction = transaction;
            versionCommand.CommandText = "DELETE FROM schema_version; INSERT INTO schema_version VALUES (@v)";
            versionCommand.Parameters.AddWithValue("@v", version);
            await versionCommand.ExecuteNonQueryAsync();

            transaction.Commit();
        }
        catch (Exception ex)
        {
            transaction.Rollback();
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
            id                           TEXT PRIMARY KEY,
            name                         TEXT NOT NULL UNIQUE COLLATE NOCASE,
            faction                      TEXT NOT NULL,
            grand_faction                TEXT NOT NULL DEFAULT '',
            operative_selection_archetype TEXT NOT NULL DEFAULT '',
            operative_selection_text      TEXT NOT NULL DEFAULT '',
            supplementary_info           TEXT NOT NULL DEFAULT ''
        );

        CREATE TABLE IF NOT EXISTS operatives (
            id              TEXT PRIMARY KEY,
            team_id         TEXT NOT NULL REFERENCES teams (id) ON DELETE CASCADE,
            name            TEXT NOT NULL,
            operative_type  TEXT NOT NULL,
            move            INTEGER NOT NULL DEFAULT 0,
            apl             INTEGER NOT NULL DEFAULT 0,
            wounds          INTEGER NOT NULL DEFAULT 0,
            save            INTEGER NOT NULL DEFAULT 0,
            equipment_json  TEXT NOT NULL DEFAULT '[]',
            primary_keyword TEXT NOT NULL DEFAULT '',
            keywords_json   TEXT NOT NULL DEFAULT '[]'
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

        CREATE TABLE IF NOT EXISTS ploy_uses (
            id               TEXT PRIMARY KEY,
            turning_point_id TEXT NOT NULL REFERENCES turning_points (id) ON DELETE CASCADE,
            team_id          TEXT NOT NULL,
            ploy_name        TEXT NOT NULL,
            description      TEXT,
            cp_cost          INTEGER NOT NULL DEFAULT 1
        );

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

        -- Team reference data tables
        CREATE TABLE IF NOT EXISTS faction_rules (
            id         TEXT PRIMARY KEY,
            team_id    TEXT NOT NULL REFERENCES teams (id) ON DELETE CASCADE,
            name       TEXT NOT NULL,
            category   TEXT,
            text       TEXT NOT NULL DEFAULT '',
            sort_order INTEGER NOT NULL DEFAULT 0
        );

        CREATE TABLE IF NOT EXISTS strategy_ploys (
            id         TEXT PRIMARY KEY,
            team_id    TEXT NOT NULL REFERENCES teams (id) ON DELETE CASCADE,
            name       TEXT NOT NULL,
            text       TEXT NOT NULL DEFAULT '',
            sort_order INTEGER NOT NULL DEFAULT 0
        );

        CREATE TABLE IF NOT EXISTS firefight_ploys (
            id         TEXT PRIMARY KEY,
            team_id    TEXT NOT NULL REFERENCES teams (id) ON DELETE CASCADE,
            name       TEXT NOT NULL,
            text       TEXT NOT NULL DEFAULT '',
            sort_order INTEGER NOT NULL DEFAULT 0
        );

        CREATE TABLE IF NOT EXISTS faction_equipment (
            id         TEXT PRIMARY KEY,
            team_id    TEXT NOT NULL REFERENCES teams (id) ON DELETE CASCADE,
            name       TEXT NOT NULL,
            text       TEXT NOT NULL DEFAULT '',
            sort_order INTEGER NOT NULL DEFAULT 0
        );

        CREATE TABLE IF NOT EXISTS universal_equipment (
            id         TEXT PRIMARY KEY,
            team_id    TEXT NOT NULL REFERENCES teams (id) ON DELETE CASCADE,
            name       TEXT NOT NULL,
            text       TEXT NOT NULL DEFAULT '',
            sort_order INTEGER NOT NULL DEFAULT 0
        );

        -- Operative reference data tables
        CREATE TABLE IF NOT EXISTS operative_abilities (
            id           TEXT PRIMARY KEY,
            operative_id TEXT NOT NULL REFERENCES operatives (id) ON DELETE CASCADE,
            name         TEXT NOT NULL,
            text         TEXT NOT NULL DEFAULT '',
            sort_order   INTEGER NOT NULL DEFAULT 0
        );

        CREATE TABLE IF NOT EXISTS operative_special_actions (
            id           TEXT PRIMARY KEY,
            operative_id TEXT NOT NULL REFERENCES operatives (id) ON DELETE CASCADE,
            name         TEXT NOT NULL,
            text         TEXT NOT NULL DEFAULT '',
            ap_cost      INTEGER NOT NULL DEFAULT 1,
            sort_order   INTEGER NOT NULL DEFAULT 0
        );

        CREATE TABLE IF NOT EXISTS operative_special_rules (
            id           TEXT PRIMARY KEY,
            operative_id TEXT NOT NULL REFERENCES operatives (id) ON DELETE CASCADE,
            name         TEXT NOT NULL,
            text         TEXT NOT NULL DEFAULT '',
            sort_order   INTEGER NOT NULL DEFAULT 0
        );

        -- Indexes
        CREATE INDEX IF NOT EXISTS idx_activations_turning_point
            ON activations (turning_point_id, sequence_number);

        CREATE INDEX IF NOT EXISTS idx_actions_activation
            ON actions (activation_id);

        CREATE INDEX IF NOT EXISTS idx_game_operative_states_game
            ON game_operative_states (game_id, operative_id);

        CREATE INDEX IF NOT EXISTS idx_ploy_uses_turning_point
            ON ploy_uses (turning_point_id, team_id);

        CREATE INDEX IF NOT EXISTS idx_action_blast_targets_action
            ON action_blast_targets (action_id);

        CREATE INDEX IF NOT EXISTS idx_faction_rules_team
            ON faction_rules (team_id, sort_order);

        CREATE INDEX IF NOT EXISTS idx_strategy_ploys_team
            ON strategy_ploys (team_id, sort_order);

        CREATE INDEX IF NOT EXISTS idx_firefight_ploys_team
            ON firefight_ploys (team_id, sort_order);

        CREATE INDEX IF NOT EXISTS idx_faction_equipment_team
            ON faction_equipment (team_id, sort_order);

        CREATE INDEX IF NOT EXISTS idx_universal_equipment_team
            ON universal_equipment (team_id, sort_order);

        CREATE INDEX IF NOT EXISTS idx_operative_abilities_op
            ON operative_abilities (operative_id, sort_order);

        CREATE INDEX IF NOT EXISTS idx_operative_special_actions_op
            ON operative_special_actions (operative_id, sort_order);

        CREATE INDEX IF NOT EXISTS idx_operative_special_rules_op
            ON operative_special_rules (operative_id, sort_order);
        """;

    private const string Migration_002 = """
        ALTER TABLE operatives ADD COLUMN defence INTEGER NOT NULL DEFAULT 3;
        ALTER TABLE game_operative_states ADD COLUMN defence_dice_modifier INTEGER NOT NULL DEFAULT 0;
        """;

    private const string Migration_003 = """
        ALTER TABLE players ADD COLUMN colour TEXT NOT NULL DEFAULT 'cyan';
        ALTER TABLE players ADD COLUMN is_internal INTEGER NOT NULL DEFAULT 0;
        INSERT OR IGNORE INTO players (id, name, colour, is_internal)
            VALUES ('00000000-0000-0000-0000-000000000001', 'You', 'cyan', 1);
        INSERT OR IGNORE INTO players (id, name, colour, is_internal)
            VALUES ('00000000-0000-0000-0000-000000000002', 'AI', 'red', 1);
        """;

    private const string Migration_004 = """
        UPDATE players SET name = 'Player' WHERE id = '00000000-0000-0000-0000-000000000001';
        """;
}
