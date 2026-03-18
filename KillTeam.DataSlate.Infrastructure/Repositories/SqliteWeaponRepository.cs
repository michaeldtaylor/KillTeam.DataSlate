using KillTeam.DataSlate.Domain.Models;
using Microsoft.Data.Sqlite;

namespace KillTeam.DataSlate.Infrastructure.Repositories;

public class SqliteWeaponRepository
{
    private readonly ISqlExecutor _db;

    public SqliteWeaponRepository(ISqlExecutor db) => _db = db;

    public SqliteWeaponRepository(SqliteConnection connection)
        : this(new SqliteExecutor(connection)) { }

    public async Task UpsertByOperativeAsync(IEnumerable<Weapon> weapons, Guid operativeId)
    {
        await _db.ExecuteTransactionAsync(async (connection, transaction) =>
        {
            await using var deleteCommand = connection.CreateCommand();
            deleteCommand.Transaction = transaction;
            deleteCommand.CommandText = "DELETE FROM weapons WHERE operative_id = @operativeId";
            deleteCommand.Parameters.AddWithValue("@operativeId", operativeId.ToString());
            await deleteCommand.ExecuteNonQueryAsync();

            foreach (var weapon in weapons)
            {
                await using var command = connection.CreateCommand();
                command.Transaction = transaction;
                command.CommandText = """
                    INSERT INTO weapons
                    (id, operative_id, name, type, atk, hit, normal_dmg, critical_dmg, special_rules)
                    VALUES (@id, @operativeId, @name, @type, @atk, @hit, @normalDmg, @criticalDmg, @specialRules)
                    """;
                command.Parameters.AddWithValue("@id", weapon.Id.ToString());
                command.Parameters.AddWithValue("@operativeId", operativeId.ToString());
                command.Parameters.AddWithValue("@name", weapon.Name);
                command.Parameters.AddWithValue("@type", weapon.Type.ToString());
                command.Parameters.AddWithValue("@atk", weapon.Atk);
                command.Parameters.AddWithValue("@hit", weapon.Hit);
                command.Parameters.AddWithValue("@normalDmg", weapon.NormalDmg);
                command.Parameters.AddWithValue("@criticalDmg", weapon.CriticalDmg);
                command.Parameters.AddWithValue("@specialRules", weapon.WeaponRules);
                await command.ExecuteNonQueryAsync();
            }
        });
    }
}
