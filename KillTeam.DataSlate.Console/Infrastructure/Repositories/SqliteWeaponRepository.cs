using Microsoft.Data.Sqlite;
using KillTeam.DataSlate.Domain.Models;

namespace KillTeam.DataSlate.Console.Infrastructure.Repositories;

public class SqliteWeaponRepository
{
    private readonly ISqlExecutor _db;

    public SqliteWeaponRepository(ISqlExecutor db) => _db = db;

    public SqliteWeaponRepository(SqliteConnection connection)
        : this(new SqliteExecutor(connection)) { }

    public async Task UpsertByOperativeAsync(IEnumerable<Weapon> weapons, Guid operativeId)
    {
        await _db.ExecuteTransactionAsync(async (conn, tx) =>
        {
            using var del = conn.CreateCommand();
            del.Transaction = tx;
            del.CommandText = "DELETE FROM weapons WHERE operative_id = @operativeId";
            del.Parameters.AddWithValue("@operativeId", operativeId.ToString());
            await del.ExecuteNonQueryAsync();

            foreach (var weapon in weapons)
            {
                weapon.OperativeId = operativeId;
                using var cmd = conn.CreateCommand();
                cmd.Transaction = tx;
                cmd.CommandText = """
                    INSERT INTO weapons
                    (id, operative_id, name, type, atk, hit, normal_dmg, critical_dmg, special_rules)
                    VALUES (@id, @operativeId, @name, @type, @atk, @hit, @normalDmg, @criticalDmg, @specialRules)
                    """;
                cmd.Parameters.AddWithValue("@id", weapon.Id.ToString());
                cmd.Parameters.AddWithValue("@operativeId", weapon.OperativeId.ToString());
                cmd.Parameters.AddWithValue("@name", weapon.Name);
                cmd.Parameters.AddWithValue("@type", weapon.Type.ToString());
                cmd.Parameters.AddWithValue("@atk", weapon.Atk);
                cmd.Parameters.AddWithValue("@hit", weapon.Hit);
                cmd.Parameters.AddWithValue("@normalDmg", weapon.NormalDmg);
                cmd.Parameters.AddWithValue("@criticalDmg", weapon.CriticalDmg);
                cmd.Parameters.AddWithValue("@specialRules", weapon.SpecialRules);
                await cmd.ExecuteNonQueryAsync();
            }
        });
    }
}
