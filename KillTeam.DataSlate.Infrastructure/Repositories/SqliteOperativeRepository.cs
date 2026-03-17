using System.Text.Json;
using KillTeam.DataSlate.Domain.Models;
using KillTeam.DataSlate.Domain.Repositories;
using Microsoft.Data.Sqlite;

namespace KillTeam.DataSlate.Infrastructure.Repositories;

public class SqliteOperativeRepository : IOperativeRepository
{
    private readonly ISqlExecutor _db;

    public SqliteOperativeRepository(ISqlExecutor db) => _db = db;

    public SqliteOperativeRepository(SqliteConnection connection)
        : this(new SqliteExecutor(connection)) { }

    public async Task<string?> GetNameByIdAsync(Guid operativeId)
    {
        return await _db.ScalarAsync<string>(
            "SELECT name FROM operatives WHERE id = @id",
            new() { ["@id"] = operativeId.ToString() });
    }

    public async Task UpsertByTeamAsync(IEnumerable<Operative> operatives, string teamId)
    {
        await _db.ExecuteTransactionAsync(async (connection, transaction) =>
        {
            await using var deleteCommand = connection.CreateCommand();
            deleteCommand.Transaction = transaction;
            deleteCommand.CommandText = "DELETE FROM operatives WHERE team_id = @teamId";
            deleteCommand.Parameters.AddWithValue("@teamId", teamId);
            await deleteCommand.ExecuteNonQueryAsync();

            foreach (var operative in operatives)
            {
                operative.TeamId = teamId;
                await using var command = connection.CreateCommand();
                command.Transaction = transaction;
                command.CommandText = """
                    INSERT INTO operatives
                    (id, team_id, name, operative_type, move, apl, wounds, save, defence, equipment_json)
                    VALUES (@id, @teamId, @name, @operativeType, @move, @apl, @wounds, @save, @defence, @equipmentJson)
                    """;
                command.Parameters.AddWithValue("@id", operative.Id.ToString());
                command.Parameters.AddWithValue("@teamId", operative.TeamId);
                command.Parameters.AddWithValue("@name", operative.Name);
                command.Parameters.AddWithValue("@operativeType", operative.OperativeType);
                command.Parameters.AddWithValue("@move", operative.Move);
                command.Parameters.AddWithValue("@apl", operative.Apl);
                command.Parameters.AddWithValue("@wounds", operative.Wounds);
                command.Parameters.AddWithValue("@save", operative.Save);
                command.Parameters.AddWithValue("@defence", operative.Defence);
                command.Parameters.AddWithValue("@equipmentJson", JsonSerializer.Serialize(operative.Equipment));
                await command.ExecuteNonQueryAsync();
            }
        });
    }
}
