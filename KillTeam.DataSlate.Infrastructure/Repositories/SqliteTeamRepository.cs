using System.Text.Json;
using KillTeam.DataSlate.Domain.Models;
using KillTeam.DataSlate.Domain.Repositories;
using Microsoft.Data.Sqlite;

namespace KillTeam.DataSlate.Infrastructure.Repositories;

public class SqliteTeamRepository : ITeamRepository
{
    private readonly ISqlExecutor _db;

    public SqliteTeamRepository(ISqlExecutor db) => _db = db;

    public SqliteTeamRepository(SqliteConnection connection)
        : this(new SqliteExecutor(connection)) { }

    public async Task UpsertAsync(Team team)
    {
        await _db.ExecuteTransactionAsync(async (connection, transaction) =>
        {
            // ── Team row ───────────────────────────────────────────────────────
            await using var command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText = """
                INSERT INTO teams (id, name, faction, grand_faction,
                                   operative_selection_archetype, operative_selection_text,
                                   supplementary_info)
                VALUES (@id, @name, @faction, @grandFaction,
                        @selArchetype, @selText, @suppInfo)
                ON CONFLICT(name) DO UPDATE SET
                    faction = excluded.faction,
                    id = excluded.id,
                    grand_faction = excluded.grand_faction,
                    operative_selection_archetype = excluded.operative_selection_archetype,
                    operative_selection_text = excluded.operative_selection_text,
                    supplementary_info = excluded.supplementary_info
                """;
            command.Parameters.AddWithValue("@id", team.Id);
            command.Parameters.AddWithValue("@name", team.Name);
            command.Parameters.AddWithValue("@faction", team.Faction);
            command.Parameters.AddWithValue("@grandFaction", team.GrandFaction);
            command.Parameters.AddWithValue("@selArchetype", team.OperativeSelectionArchetype);
            command.Parameters.AddWithValue("@selText", team.OperativeSelectionText);
            command.Parameters.AddWithValue("@suppInfo", team.SupplementaryInfo);
            await command.ExecuteNonQueryAsync();

            // ── Team-level child tables (delete + reinsert) ────────────────────
            await DeleteByTeamAsync(connection, transaction, "faction_rules", team.Id);
            await DeleteByTeamAsync(connection, transaction, "strategy_ploys", team.Id);
            await DeleteByTeamAsync(connection, transaction, "firefight_ploys", team.Id);
            await DeleteByTeamAsync(connection, transaction, "faction_equipment", team.Id);
            await DeleteByTeamAsync(connection, transaction, "universal_equipment", team.Id);

            await InsertNamedRulesAsync(connection, transaction, "faction_rules", team.Id, team.FactionRules);
            await InsertNamedRulesAsync(connection, transaction, "strategy_ploys", team.Id, team.StrategyPloys);
            await InsertNamedRulesAsync(connection, transaction, "firefight_ploys", team.Id, team.FirefightPloys);
            await InsertEquipmentAsync(connection, transaction, "faction_equipment", team.Id, team.FactionEquipment);
            await InsertEquipmentAsync(connection, transaction, "universal_equipment", team.Id, team.UniversalEquipment);

            // ── Operatives ─────────────────────────────────────────────────────
            foreach (var operative in team.Operatives)
            {
                operative.TeamId = team.Id;
                await using var operativeCommand = connection.CreateCommand();
                operativeCommand.Transaction = transaction;
                operativeCommand.CommandText = """
                    INSERT OR REPLACE INTO operatives
                    (id, team_id, name, operative_type, move, apl, wounds, save, defence,
                     equipment_json, primary_keyword, keywords_json)
                    VALUES (@id, @teamId, @name, @operativeType, @move, @apl, @wounds, @save, @defence,
                            @equipmentJson, @primaryKeyword, @keywordsJson)
                    """;
                operativeCommand.Parameters.AddWithValue("@id", operative.Id.ToString());
                operativeCommand.Parameters.AddWithValue("@teamId", operative.TeamId);
                operativeCommand.Parameters.AddWithValue("@name", operative.Name);
                operativeCommand.Parameters.AddWithValue("@operativeType", operative.OperativeType);
                operativeCommand.Parameters.AddWithValue("@move", operative.Move);
                operativeCommand.Parameters.AddWithValue("@apl", operative.Apl);
                operativeCommand.Parameters.AddWithValue("@wounds", operative.Wounds);
                operativeCommand.Parameters.AddWithValue("@save", operative.Save);
                operativeCommand.Parameters.AddWithValue("@defence", operative.Defence);
                operativeCommand.Parameters.AddWithValue("@equipmentJson", JsonSerializer.Serialize(operative.Equipment));
                operativeCommand.Parameters.AddWithValue("@primaryKeyword", operative.PrimaryKeyword);
                operativeCommand.Parameters.AddWithValue("@keywordsJson", JsonSerializer.Serialize(operative.Keywords));
                await operativeCommand.ExecuteNonQueryAsync();

                // Operative child tables
                await DeleteByOperativeAsync(connection, transaction, "operative_abilities", operative.Id);
                await DeleteByOperativeAsync(connection, transaction, "operative_special_actions", operative.Id);
                await DeleteByOperativeAsync(connection, transaction, "operative_special_rules", operative.Id);

                await InsertAbilitiesAsync(connection, transaction, operative.Id, operative.Abilities);
                await InsertSpecialActionsAsync(connection, transaction, operative.Id, operative.SpecialActions);
                await InsertWeaponRulesAsync(connection, transaction, operative.Id, operative.OperativeWeaponRules);

                foreach (var weapon in operative.Weapons)
                {
                    await using var weaponCommand = connection.CreateCommand();
                    weaponCommand.Transaction = transaction;
                    weaponCommand.CommandText = """
                        INSERT OR REPLACE INTO weapons
                        (id, operative_id, name, type, atk, hit, normal_dmg, critical_dmg, special_rules)
                        VALUES (@id, @operativeId, @name, @type, @atk, @hit, @normalDmg, @criticalDmg, @specialRules)
                        """;
                    weaponCommand.Parameters.AddWithValue("@id", weapon.Id.ToString());
                    weaponCommand.Parameters.AddWithValue("@operativeId", operative.Id.ToString());
                    weaponCommand.Parameters.AddWithValue("@name", weapon.Name);
                    weaponCommand.Parameters.AddWithValue("@type", weapon.Type.ToString());
                    weaponCommand.Parameters.AddWithValue("@atk", weapon.Atk);
                    weaponCommand.Parameters.AddWithValue("@hit", weapon.Hit);
                    weaponCommand.Parameters.AddWithValue("@normalDmg", weapon.NormalDmg);
                    weaponCommand.Parameters.AddWithValue("@criticalDmg", weapon.CriticalDmg);
                    weaponCommand.Parameters.AddWithValue("@specialRules", weapon.WeaponRules);
                    await weaponCommand.ExecuteNonQueryAsync();
                }
            }
        });
    }

    // ─── Child table helpers ──────────────────────────────────────────────────

    private static async Task DeleteByTeamAsync(SqliteConnection connection, SqliteTransaction transaction,
        string table, string teamId)
    {
        await using var deleteCommand = connection.CreateCommand();
        deleteCommand.Transaction = transaction;
        deleteCommand.CommandText = $"DELETE FROM {table} WHERE team_id = @teamId";
        deleteCommand.Parameters.AddWithValue("@teamId", teamId);
        await deleteCommand.ExecuteNonQueryAsync();
    }

    private static async Task DeleteByOperativeAsync(SqliteConnection connection, SqliteTransaction transaction,
        string table, Guid operativeId)
    {
        await using var deleteCommand = connection.CreateCommand();
        deleteCommand.Transaction = transaction;
        deleteCommand.CommandText = $"DELETE FROM {table} WHERE operative_id = @opId";
        deleteCommand.Parameters.AddWithValue("@opId", operativeId.ToString());
        await deleteCommand.ExecuteNonQueryAsync();
    }

    private static async Task InsertNamedRulesAsync(SqliteConnection connection, SqliteTransaction transaction,
        string table, string teamId, List<NamedRule> rules)
    {
        var hasCategoryColumn = table == "faction_rules";

        for (var i = 0; i < rules.Count; i++)
        {
            var rule = rules[i];
            await using var insertCommand = connection.CreateCommand();
            insertCommand.Transaction = transaction;
            insertCommand.CommandText = hasCategoryColumn
                ? $"INSERT INTO {table} (id, team_id, name, category, text, sort_order) VALUES (@id, @teamId, @name, @category, @text, @sort)"
                : $"INSERT INTO {table} (id, team_id, name, text, sort_order) VALUES (@id, @teamId, @name, @text, @sort)";
            insertCommand.Parameters.AddWithValue("@id", Guid.NewGuid().ToString());
            insertCommand.Parameters.AddWithValue("@teamId", teamId);
            insertCommand.Parameters.AddWithValue("@name", rule.Name);

            if (hasCategoryColumn)
            {
                insertCommand.Parameters.AddWithValue("@category", (object?)rule.Category ?? DBNull.Value);
            }

            insertCommand.Parameters.AddWithValue("@text", rule.Text);
            insertCommand.Parameters.AddWithValue("@sort", i);
            await insertCommand.ExecuteNonQueryAsync();
        }
    }

    private static async Task InsertEquipmentAsync(SqliteConnection connection, SqliteTransaction transaction,
        string table, string teamId, List<EquipmentItem> items)
    {
        for (var i = 0; i < items.Count; i++)
        {
            var equipment = items[i];
            await using var insertCommand = connection.CreateCommand();
            insertCommand.Transaction = transaction;
            insertCommand.CommandText = $"INSERT INTO {table} (id, team_id, name, text, sort_order) VALUES (@id, @teamId, @name, @text, @sort)";
            insertCommand.Parameters.AddWithValue("@id", Guid.NewGuid().ToString());
            insertCommand.Parameters.AddWithValue("@teamId", teamId);
            insertCommand.Parameters.AddWithValue("@name", equipment.Name);
            insertCommand.Parameters.AddWithValue("@text", equipment.Text);
            insertCommand.Parameters.AddWithValue("@sort", i);
            await insertCommand.ExecuteNonQueryAsync();
        }
    }

    private static async Task InsertAbilitiesAsync(SqliteConnection connection, SqliteTransaction transaction,
        Guid operativeId, List<OperativeAbility> abilities)
    {
        for (var i = 0; i < abilities.Count; i++)
        {
            var ability = abilities[i];
            await using var insertCommand = connection.CreateCommand();
            insertCommand.Transaction = transaction;
            insertCommand.CommandText = "INSERT INTO operative_abilities (id, operative_id, name, text, sort_order) VALUES (@id, @opId, @name, @text, @sort)";
            insertCommand.Parameters.AddWithValue("@id", Guid.NewGuid().ToString());
            insertCommand.Parameters.AddWithValue("@opId", operativeId.ToString());
            insertCommand.Parameters.AddWithValue("@name", ability.Name);
            insertCommand.Parameters.AddWithValue("@text", ability.Text);
            insertCommand.Parameters.AddWithValue("@sort", i);
            await insertCommand.ExecuteNonQueryAsync();
        }
    }

    private static async Task InsertSpecialActionsAsync(SqliteConnection connection, SqliteTransaction transaction,
        Guid operativeId, List<OperativeSpecialAction> actions)
    {
        for (var i = 0; i < actions.Count; i++)
        {
            var action = actions[i];
            await using var insertCommand = connection.CreateCommand();
            insertCommand.Transaction = transaction;
            insertCommand.CommandText = "INSERT INTO operative_special_actions (id, operative_id, name, text, ap_cost, sort_order) VALUES (@id, @opId, @name, @text, @apCost, @sort)";
            insertCommand.Parameters.AddWithValue("@id", Guid.NewGuid().ToString());
            insertCommand.Parameters.AddWithValue("@opId", operativeId.ToString());
            insertCommand.Parameters.AddWithValue("@name", action.Name);
            insertCommand.Parameters.AddWithValue("@text", action.Text);
            insertCommand.Parameters.AddWithValue("@apCost", action.ApCost);
            insertCommand.Parameters.AddWithValue("@sort", i);
            await insertCommand.ExecuteNonQueryAsync();
        }
    }

    private static async Task InsertWeaponRulesAsync(SqliteConnection connection, SqliteTransaction transaction,
        Guid operativeId, List<OperativeWeaponRule> rules)
    {
        for (var i = 0; i < rules.Count; i++)
        {
            var rule = rules[i];
            await using var insertCommand = connection.CreateCommand();
            insertCommand.Transaction = transaction;
            insertCommand.CommandText = "INSERT INTO operative_special_rules (id, operative_id, name, text, sort_order) VALUES (@id, @opId, @name, @text, @sort)";
            insertCommand.Parameters.AddWithValue("@id", Guid.NewGuid().ToString());
            insertCommand.Parameters.AddWithValue("@opId", operativeId.ToString());
            insertCommand.Parameters.AddWithValue("@name", rule.Name);
            insertCommand.Parameters.AddWithValue("@text", rule.Text);
            insertCommand.Parameters.AddWithValue("@sort", i);
            await insertCommand.ExecuteNonQueryAsync();
        }
    }

    public async Task<IEnumerable<TeamSummary>> GetAllAsync()
    {
        return await _db.QueryAsync(
            "SELECT id, name, faction, grand_faction FROM teams",
            reader => new TeamSummary(
                reader.GetString(0),
                reader.GetString(1),
                reader.GetString(2),
                reader.GetString(3)));
    }

    public async Task<Team?> GetByIdAsync(string id)
    {
        var row = await _db.QuerySingleAsync(
            """
            SELECT id, name, faction, grand_faction,
                   operative_selection_archetype, operative_selection_text,
                   supplementary_info
            FROM teams WHERE id = @id
            """,
            reader => new
            {
                Id = reader.GetString(0), Name = reader.GetString(1), Faction = reader.GetString(2),
                GrandFaction = reader.GetString(3),
                SelectionArchetype = reader.GetString(4), SelectionText = reader.GetString(5),
                SupplementaryInfo = reader.GetString(6),
            },
            new() { ["@id"] = id });

        if (row is null)
        {
            return null;
        }

        var operatives = (await _db.QueryAsync(
            """
            SELECT id, name, operative_type, move, apl, wounds, save, defence, equipment_json,
                   primary_keyword, keywords_json
            FROM operatives WHERE team_id = @teamId
            """,
            reader => new Operative
            {
                Id = Guid.Parse(reader.GetString(0)),
                TeamId = row.Id,
                Name = reader.GetString(1),
                OperativeType = reader.GetString(2),
                Move = reader.GetInt32(3),
                Apl = reader.GetInt32(4),
                Wounds = reader.GetInt32(5),
                Save = reader.GetInt32(6),
                Defence = reader.GetInt32(7),
                Equipment = JsonSerializer.Deserialize<string[]>(reader.GetString(8)) ?? [],
                PrimaryKeyword = reader.GetString(9),
                Keywords = JsonSerializer.Deserialize<string[]>(reader.GetString(10)) ?? [],
            },
            new() { ["@teamId"] = row.Id })).ToList();

        foreach (var operative in operatives)
        {
            var weapons = await _db.QueryAsync(
                """
                SELECT id, name, type, atk, hit, normal_dmg, critical_dmg, special_rules
                FROM weapons WHERE operative_id = @opId
                """,
                reader => new Weapon
                {
                    Id = Guid.Parse(reader.GetString(0)),
                    Name = reader.GetString(1),
                    Type = Enum.Parse<WeaponType>(reader.GetString(2)),
                    Atk = reader.GetInt32(3),
                    Hit = reader.GetInt32(4),
                    NormalDmg = reader.GetInt32(5),
                    CriticalDmg = reader.GetInt32(6),
                    WeaponRules = reader.GetString(7),
                    Rules = WeaponRuleParser.Parse(reader.GetString(7))
                },
                new() { ["@opId"] = operative.Id.ToString() });
            operative.Weapons.AddRange(weapons);
        }

        return new Team
        {
            Id = row.Id,
            Name = row.Name,
            GrandFaction = row.GrandFaction,
            Faction = row.Faction,
            Operatives = operatives,
            OperativeSelectionArchetype = row.SelectionArchetype,
            OperativeSelectionText = row.SelectionText,
            SupplementaryInfo = row.SupplementaryInfo,
        };
    }

    public async Task<TeamSummary?> GetSummaryAsync(string id)
    {
        return await _db.QuerySingleAsync(
            "SELECT id, name, faction, grand_faction FROM teams WHERE id = @id LIMIT 1",
            reader => new TeamSummary(
                reader.GetString(0),
                reader.GetString(1),
                reader.GetString(2),
                reader.GetString(3)),
            new() { ["@id"] = id });
    }

    public async Task<TeamStats?> GetStatsAsync(string id)
    {
        var gamesAndWins = await _db.QuerySingleAsync(
            """
            SELECT COUNT(*), COALESCE(SUM(CASE WHEN winner_team_id = @id THEN 1 ELSE 0 END), 0)
            FROM games
            WHERE (participant1_team_id = @id OR participant2_team_id = @id) AND status = 'Completed'
            """,
            reader => (Games: reader.GetInt32(0), Wins: reader.GetInt32(1)),
            new() { ["@id"] = id });

        var kills = await _db.ScalarAsync<int>(
            """
            SELECT COUNT(*) FROM actions a
            JOIN activations act ON act.id = a.activation_id
            JOIN turning_points tp ON tp.id = act.turning_point_id
            WHERE a.caused_incapacitation = 1 AND act.team_id = @id
            UNION ALL
            SELECT COUNT(*) FROM action_blast_targets abt
            JOIN actions a2 ON a2.id = abt.action_id
            JOIN activations act2 ON act2.id = a2.activation_id
            JOIN turning_points tp2 ON tp2.id = act2.turning_point_id
            WHERE abt.caused_incapacitation = 1 AND act2.team_id = @id
            """,
            new() { ["@id"] = id });

        var mostUsedWeapon = await _db.QuerySingleAsync(
            """
            SELECT w.name
            FROM actions a
            JOIN activations act ON act.id = a.activation_id
            JOIN weapons w ON w.id = a.weapon_id
            WHERE a.type IN ('Shoot', 'Fight') AND act.team_id = @id AND a.weapon_id IS NOT NULL
            GROUP BY a.weapon_id
            ORDER BY COUNT(*) DESC
            LIMIT 1
            """,
            reader => reader.GetString(0),
            new() { ["@id"] = id });

        return new TeamStats(gamesAndWins.Games, gamesAndWins.Wins, kills, mostUsedWeapon);
    }
}
