using System.Text.Json;
using Microsoft.Data.Sqlite;
using KillTeam.DataSlate.Domain.Models;
using KillTeam.DataSlate.Domain.Repositories;
using Models = KillTeam.DataSlate.Domain.Models;

namespace KillTeam.DataSlate.Infrastructure.Repositories;

public class SqliteTeamRepository : ITeamRepository
{
    private readonly ISqlExecutor _db;

    public SqliteTeamRepository(ISqlExecutor db) => _db = db;

    public SqliteTeamRepository(SqliteConnection connection)
        : this(new SqliteExecutor(connection)) { }

    public async Task UpsertAsync(Models.Team team)
    {
        await _db.ExecuteTransactionAsync(async (conn, tx) =>
        {
            // ── Team row ───────────────────────────────────────────────────────
            using var cmd = conn.CreateCommand();
            cmd.Transaction = tx;
            cmd.CommandText = """
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
            cmd.Parameters.AddWithValue("@id", team.Id);
            cmd.Parameters.AddWithValue("@name", team.Name);
            cmd.Parameters.AddWithValue("@faction", team.Faction);
            cmd.Parameters.AddWithValue("@grandFaction", team.GrandFaction);
            cmd.Parameters.AddWithValue("@selArchetype", team.OperativeSelectionArchetype);
            cmd.Parameters.AddWithValue("@selText", team.OperativeSelectionText);
            cmd.Parameters.AddWithValue("@suppInfo", team.SupplementaryInfo);
            await cmd.ExecuteNonQueryAsync();

            // ── Team-level child tables (delete + reinsert) ────────────────────
            await DeleteByTeamAsync(conn, tx, "faction_rules", team.Id);
            await DeleteByTeamAsync(conn, tx, "strategy_ploys", team.Id);
            await DeleteByTeamAsync(conn, tx, "firefight_ploys", team.Id);
            await DeleteByTeamAsync(conn, tx, "faction_equipment", team.Id);
            await DeleteByTeamAsync(conn, tx, "universal_equipment", team.Id);

            await InsertNamedRulesAsync(conn, tx, "faction_rules", team.Id, team.FactionRules);
            await InsertNamedRulesAsync(conn, tx, "strategy_ploys", team.Id, team.StrategyPloys);
            await InsertNamedRulesAsync(conn, tx, "firefight_ploys", team.Id, team.FirefightPloys);
            await InsertEquipmentAsync(conn, tx, "faction_equipment", team.Id, team.FactionEquipment);
            await InsertEquipmentAsync(conn, tx, "universal_equipment", team.Id, team.UniversalEquipment);

            // ── Operatives ─────────────────────────────────────────────────────
            foreach (var operative in team.Operatives)
            {
                operative.TeamId = team.Id;
                using var opCmd = conn.CreateCommand();
                opCmd.Transaction = tx;
                opCmd.CommandText = """
                    INSERT OR REPLACE INTO operatives
                    (id, team_id, name, operative_type, move, apl, wounds, save,
                     equipment_json, primary_keyword, keywords_json)
                    VALUES (@id, @teamId, @name, @operativeType, @move, @apl, @wounds, @save,
                            @equipmentJson, @primaryKeyword, @keywordsJson)
                    """;
                opCmd.Parameters.AddWithValue("@id", operative.Id.ToString());
                opCmd.Parameters.AddWithValue("@teamId", operative.TeamId);
                opCmd.Parameters.AddWithValue("@name", operative.Name);
                opCmd.Parameters.AddWithValue("@operativeType", operative.OperativeType);
                opCmd.Parameters.AddWithValue("@move", operative.Move);
                opCmd.Parameters.AddWithValue("@apl", operative.Apl);
                opCmd.Parameters.AddWithValue("@wounds", operative.Wounds);
                opCmd.Parameters.AddWithValue("@save", operative.Save);
                opCmd.Parameters.AddWithValue("@equipmentJson", JsonSerializer.Serialize(operative.Equipment));
                opCmd.Parameters.AddWithValue("@primaryKeyword", operative.PrimaryKeyword);
                opCmd.Parameters.AddWithValue("@keywordsJson", JsonSerializer.Serialize(operative.Keywords));
                await opCmd.ExecuteNonQueryAsync();

                // Operative child tables
                await DeleteByOperativeAsync(conn, tx, "operative_abilities", operative.Id);
                await DeleteByOperativeAsync(conn, tx, "operative_special_actions", operative.Id);
                await DeleteByOperativeAsync(conn, tx, "operative_special_rules", operative.Id);

                await InsertAbilitiesAsync(conn, tx, operative.Id, operative.Abilities);
                await InsertSpecialActionsAsync(conn, tx, operative.Id, operative.SpecialActions);
                await InsertWeaponRulesAsync(conn, tx, operative.Id, operative.SpecialRules);

                foreach (var weapon in operative.Weapons)
                {
                    weapon.OperativeId = operative.Id;
                    using var wpCmd = conn.CreateCommand();
                    wpCmd.Transaction = tx;
                    wpCmd.CommandText = """
                        INSERT OR REPLACE INTO weapons
                        (id, operative_id, name, type, atk, hit, normal_dmg, critical_dmg, special_rules)
                        VALUES (@id, @operativeId, @name, @type, @atk, @hit, @normalDmg, @criticalDmg, @specialRules)
                        """;
                    wpCmd.Parameters.AddWithValue("@id", weapon.Id.ToString());
                    wpCmd.Parameters.AddWithValue("@operativeId", weapon.OperativeId.ToString());
                    wpCmd.Parameters.AddWithValue("@name", weapon.Name);
                    wpCmd.Parameters.AddWithValue("@type", weapon.Type.ToString());
                    wpCmd.Parameters.AddWithValue("@atk", weapon.Atk);
                    wpCmd.Parameters.AddWithValue("@hit", weapon.Hit);
                    wpCmd.Parameters.AddWithValue("@normalDmg", weapon.NormalDmg);
                    wpCmd.Parameters.AddWithValue("@criticalDmg", weapon.CriticalDmg);
                    wpCmd.Parameters.AddWithValue("@specialRules", weapon.SpecialRules);
                    await wpCmd.ExecuteNonQueryAsync();
                }
            }
        });
    }

    // ─── Child table helpers ──────────────────────────────────────────────────

    private static async Task DeleteByTeamAsync(SqliteConnection conn, SqliteTransaction tx,
        string table, string teamId)
    {
        using var del = conn.CreateCommand();
        del.Transaction = tx;
        del.CommandText = $"DELETE FROM {table} WHERE team_id = @teamId";
        del.Parameters.AddWithValue("@teamId", teamId);
        await del.ExecuteNonQueryAsync();
    }

    private static async Task DeleteByOperativeAsync(SqliteConnection conn, SqliteTransaction tx,
        string table, Guid operativeId)
    {
        using var del = conn.CreateCommand();
        del.Transaction = tx;
        del.CommandText = $"DELETE FROM {table} WHERE operative_id = @opId";
        del.Parameters.AddWithValue("@opId", operativeId.ToString());
        await del.ExecuteNonQueryAsync();
    }

    private static async Task InsertNamedRulesAsync(SqliteConnection conn, SqliteTransaction tx,
        string table, string teamId, List<NamedRule> rules)
    {
        var hasCategoryColumn = table == "faction_rules";
        for (var i = 0; i < rules.Count; i++)
        {
            var r = rules[i];
            using var ins = conn.CreateCommand();
            ins.Transaction = tx;
            ins.CommandText = hasCategoryColumn
                ? $"INSERT INTO {table} (id, team_id, name, category, text, sort_order) VALUES (@id, @teamId, @name, @category, @text, @sort)"
                : $"INSERT INTO {table} (id, team_id, name, text, sort_order) VALUES (@id, @teamId, @name, @text, @sort)";
            ins.Parameters.AddWithValue("@id", Guid.NewGuid().ToString());
            ins.Parameters.AddWithValue("@teamId", teamId);
            ins.Parameters.AddWithValue("@name", r.Name);
            if (hasCategoryColumn)
            {
                ins.Parameters.AddWithValue("@category", (object?)r.Category ?? DBNull.Value);
            }
            ins.Parameters.AddWithValue("@text", r.Text);
            ins.Parameters.AddWithValue("@sort", i);
            await ins.ExecuteNonQueryAsync();
        }
    }

    private static async Task InsertEquipmentAsync(SqliteConnection conn, SqliteTransaction tx,
        string table, string teamId, List<EquipmentItem> items)
    {
        for (var i = 0; i < items.Count; i++)
        {
            var e = items[i];
            using var ins = conn.CreateCommand();
            ins.Transaction = tx;
            ins.CommandText = $"INSERT INTO {table} (id, team_id, name, text, sort_order) VALUES (@id, @teamId, @name, @text, @sort)";
            ins.Parameters.AddWithValue("@id", Guid.NewGuid().ToString());
            ins.Parameters.AddWithValue("@teamId", teamId);
            ins.Parameters.AddWithValue("@name", e.Name);
            ins.Parameters.AddWithValue("@text", e.Text);
            ins.Parameters.AddWithValue("@sort", i);
            await ins.ExecuteNonQueryAsync();
        }
    }

    private static async Task InsertAbilitiesAsync(SqliteConnection conn, SqliteTransaction tx,
        Guid operativeId, List<OperativeAbility> abilities)
    {
        for (var i = 0; i < abilities.Count; i++)
        {
            var a = abilities[i];
            using var ins = conn.CreateCommand();
            ins.Transaction = tx;
            ins.CommandText = "INSERT INTO operative_abilities (id, operative_id, name, text, sort_order) VALUES (@id, @opId, @name, @text, @sort)";
            ins.Parameters.AddWithValue("@id", Guid.NewGuid().ToString());
            ins.Parameters.AddWithValue("@opId", operativeId.ToString());
            ins.Parameters.AddWithValue("@name", a.Name);
            ins.Parameters.AddWithValue("@text", a.Text);
            ins.Parameters.AddWithValue("@sort", i);
            await ins.ExecuteNonQueryAsync();
        }
    }

    private static async Task InsertSpecialActionsAsync(SqliteConnection conn, SqliteTransaction tx,
        Guid operativeId, List<OperativeSpecialAction> actions)
    {
        for (var i = 0; i < actions.Count; i++)
        {
            var a = actions[i];
            using var ins = conn.CreateCommand();
            ins.Transaction = tx;
            ins.CommandText = "INSERT INTO operative_special_actions (id, operative_id, name, text, ap_cost, sort_order) VALUES (@id, @opId, @name, @text, @apCost, @sort)";
            ins.Parameters.AddWithValue("@id", Guid.NewGuid().ToString());
            ins.Parameters.AddWithValue("@opId", operativeId.ToString());
            ins.Parameters.AddWithValue("@name", a.Name);
            ins.Parameters.AddWithValue("@text", a.Text);
            ins.Parameters.AddWithValue("@apCost", a.ApCost);
            ins.Parameters.AddWithValue("@sort", i);
            await ins.ExecuteNonQueryAsync();
        }
    }

    private static async Task InsertWeaponRulesAsync(SqliteConnection conn, SqliteTransaction tx,
        Guid operativeId, List<OperativeWeaponRule> rules)
    {
        for (var i = 0; i < rules.Count; i++)
        {
            var r = rules[i];
            using var ins = conn.CreateCommand();
            ins.Transaction = tx;
            ins.CommandText = "INSERT INTO operative_special_rules (id, operative_id, name, text, sort_order) VALUES (@id, @opId, @name, @text, @sort)";
            ins.Parameters.AddWithValue("@id", Guid.NewGuid().ToString());
            ins.Parameters.AddWithValue("@opId", operativeId.ToString());
            ins.Parameters.AddWithValue("@name", r.Name);
            ins.Parameters.AddWithValue("@text", r.Text);
            ins.Parameters.AddWithValue("@sort", i);
            await ins.ExecuteNonQueryAsync();
        }
    }

    public async Task<IEnumerable<Models.Team>> GetAllAsync()
    {
        return await _db.QueryAsync(
            "SELECT id, name, faction, grand_faction FROM teams",
            r => new Models.Team
            {
                Id = r.GetString(0),
                Name = r.GetString(1),
                Faction = r.GetString(2),
                GrandFaction = r.GetString(3),
            });
    }

    public async Task<Models.Team?> GetByNameAsync(string name)
    {
        return await _db.QuerySingleAsync(
            "SELECT id, name, faction, grand_faction FROM teams WHERE name = @name COLLATE NOCASE LIMIT 1",
            r => new Models.Team
            {
                Id = r.GetString(0),
                Name = r.GetString(1),
                Faction = r.GetString(2),
                GrandFaction = r.GetString(3),
            },
            new() { ["@name"] = name });
    }

    public async Task<Models.Team?> GetWithOperativesAsync(string name)
    {
        var row = await _db.QuerySingleAsync(
            """
            SELECT id, name, faction, grand_faction,
                   operative_selection_archetype, operative_selection_text,
                   supplementary_info
            FROM teams WHERE name = @name COLLATE NOCASE
            """,
            r => new
            {
                Id = r.GetString(0), Name = r.GetString(1), Faction = r.GetString(2),
                GrandFaction = r.GetString(3),
                SelectionArchetype = r.GetString(4), SelectionText = r.GetString(5),
                SupplementaryInfo = r.GetString(6),
            },
            new() { ["@name"] = name });

        if (row is null)
        {
            return null;
        }

        var operatives = (await _db.QueryAsync(
            """
            SELECT id, name, operative_type, move, apl, wounds, save, equipment_json,
                   primary_keyword, keywords_json
            FROM operatives WHERE team_id = @teamId
            """,
            r => new Operative
            {
                Id = Guid.Parse(r.GetString(0)),
                TeamId = row.Id,
                Name = r.GetString(1),
                OperativeType = r.GetString(2),
                Move = r.GetInt32(3),
                Apl = r.GetInt32(4),
                Wounds = r.GetInt32(5),
                Save = r.GetInt32(6),
                Equipment = JsonSerializer.Deserialize<string[]>(r.GetString(7)) ?? [],
                PrimaryKeyword = r.GetString(8),
                Keywords = JsonSerializer.Deserialize<string[]>(r.GetString(9)) ?? [],
            },
            new() { ["@teamId"] = row.Id })).ToList();

        foreach (var op in operatives)
        {
            var weapons = await _db.QueryAsync(
                """
                SELECT id, name, type, atk, hit, normal_dmg, critical_dmg, special_rules
                FROM weapons WHERE operative_id = @opId
                """,
                r => new Weapon
                {
                    Id = Guid.Parse(r.GetString(0)),
                    OperativeId = op.Id,
                    Name = r.GetString(1),
                    Type = Enum.Parse<WeaponType>(r.GetString(2)),
                    Atk = r.GetInt32(3),
                    Hit = r.GetInt32(4),
                    NormalDmg = r.GetInt32(5),
                    CriticalDmg = r.GetInt32(6),
                    SpecialRules = r.GetString(7)
                },
                new() { ["@opId"] = op.Id.ToString() });
            op.Weapons.AddRange(weapons);
        }

        return new Models.Team
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
}
