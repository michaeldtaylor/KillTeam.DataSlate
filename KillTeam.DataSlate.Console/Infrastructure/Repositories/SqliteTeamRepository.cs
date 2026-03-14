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
            cmd.CommandText = """
                INSERT INTO teams (id, name, faction) VALUES (@id, @name, @faction)
                ON CONFLICT(name) DO UPDATE SET faction = excluded.faction, id = excluded.id
                """;
            cmd.Parameters.AddWithValue("@id", team.Id);
            cmd.Parameters.AddWithValue("@name", team.Name);
            cmd.Parameters.AddWithValue("@faction", team.Faction);
            await cmd.ExecuteNonQueryAsync();

            foreach (var operative in team.Operatives)
            {
                operative.TeamId = team.Id;
                using var opCmd = conn.CreateCommand();
                opCmd.Transaction = tx;
                opCmd.CommandText = """
                    INSERT OR REPLACE INTO operatives
                    (id, team_id, name, operative_type, move, apl, wounds, save, equipment_json)
                    VALUES (@id, @teamId, @name, @operativeType, @move, @apl, @wounds, @save, @equipmentJson)
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
            "SELECT id, name, faction FROM teams",
            r => new Models.Team
            {
                Id = r.GetString(0),
                Name = r.GetString(1),
                Faction = r.GetString(2)
            });
    }

    public async Task<Models.Team?> GetByNameAsync(string name)
    {
        return await _db.QuerySingleAsync(
            "SELECT id, name, faction FROM teams WHERE name = @name COLLATE NOCASE LIMIT 1",
            r => new Models.Team
            {
                Id = r.GetString(0),
                Name = r.GetString(1),
                Faction = r.GetString(2)
            },
            new() { ["@name"] = name });
    }

    public async Task<Models.Team?> GetWithOperativesAsync(string name)
    {
        var row = await _db.QuerySingleAsync(
            "SELECT id, name, faction FROM teams WHERE name = @name COLLATE NOCASE",
            r => new { Id = r.GetString(0), Name = r.GetString(1), Faction = r.GetString(2) },
            new() { ["@name"] = name });

        if (row is null) return null;

        var operatives = (await _db.QueryAsync(
            """
            SELECT id, name, operative_type, move, apl, wounds, save, equipment_json
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
                Equipment = JsonSerializer.Deserialize<string[]>(r.GetString(7)) ?? []
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
            Faction = row.Faction,
            Operatives = operatives
        };
    }
}
