using System.Text.Json;
using Microsoft.Data.Sqlite;
using KillTeam.DataSlate.Domain.Models;
using KillTeam.DataSlate.Domain.Repositories;
using Models = KillTeam.DataSlate.Domain.Models;

namespace KillTeam.DataSlate.Console.Infrastructure.Repositories;

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
            using var cmd = conn.CreateCommand();
            cmd.Transaction = tx;
            cmd.CommandText = "INSERT OR REPLACE INTO teams (name, faction) VALUES (@name, @faction)";
            cmd.Parameters.AddWithValue("@name", team.Name);
            cmd.Parameters.AddWithValue("@faction", team.Faction);
            await cmd.ExecuteNonQueryAsync();

            foreach (var operative in team.Operatives)
            {
                operative.TeamName = team.Name;
                using var opCmd = conn.CreateCommand();
                opCmd.Transaction = tx;
                opCmd.CommandText = """
                    INSERT OR REPLACE INTO operatives
                    (id, team_name, name, operative_type, move, apl, wounds, save, equipment_json)
                    VALUES (@id, @TeamName, @name, @operativeType, @move, @apl, @wounds, @save, @equipmentJson)
                    """;
                opCmd.Parameters.AddWithValue("@id", operative.Id.ToString());
                opCmd.Parameters.AddWithValue("@TeamName", operative.TeamName);
                opCmd.Parameters.AddWithValue("@name", operative.Name);
                opCmd.Parameters.AddWithValue("@operativeType", operative.OperativeType);
                opCmd.Parameters.AddWithValue("@move", operative.Move);
                opCmd.Parameters.AddWithValue("@apl", operative.Apl);
                opCmd.Parameters.AddWithValue("@wounds", operative.Wounds);
                opCmd.Parameters.AddWithValue("@save", operative.Save);
                opCmd.Parameters.AddWithValue("@equipmentJson", JsonSerializer.Serialize(operative.Equipment));
                await opCmd.ExecuteNonQueryAsync();

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

    public async Task<IEnumerable<Models.Team>> GetAllAsync()
    {
        return await _db.QueryAsync(
            "SELECT name, faction FROM teams",
            r => new Models.Team
            {
                Name = r.GetString(0),
                Faction = r.GetString(1)
            });
    }

    public async Task<Models.Team?> GetByNameAsync(string name)
    {
        return await _db.QuerySingleAsync(
            "SELECT name, faction FROM teams WHERE name = @name COLLATE NOCASE LIMIT 1",
            r => new Models.Team
            {
                Name = r.GetString(0),
                Faction = r.GetString(1)
            },
            new() { ["@name"] = name });
    }

    public async Task<Models.Team?> GetWithOperativesAsync(string name)
    {
        var team = await _db.QuerySingleAsync(
            "SELECT name, faction FROM teams WHERE name = @name COLLATE NOCASE",
            r => new Models.Team
            {
                Name = r.GetString(0),
                Faction = r.GetString(1),
                Operatives = []
            },
            new() { ["@name"] = name });

        if (team is null) return null;

        var operatives = await _db.QueryAsync(
            """
            SELECT id, name, operative_type, move, apl, wounds, save, equipment_json
            FROM operatives WHERE team_name = @teamName
            """,
            r => new Operative
            {
                Id = Guid.Parse(r.GetString(0)),
                TeamName = team.Name,
                Name = r.GetString(1),
                OperativeType = r.GetString(2),
                Move = r.GetInt32(3),
                Apl = r.GetInt32(4),
                Wounds = r.GetInt32(5),
                Save = r.GetInt32(6),
                Equipment = JsonSerializer.Deserialize<string[]>(r.GetString(7)) ?? []
            },
            new() { ["@teamName"] = team.Name });

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

        team.Operatives = operatives.ToList();
        return team;
    }
}
