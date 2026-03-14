using System.Text.Json;
using Microsoft.Data.Sqlite;
using KillTeam.DataSlate.Domain.Models;

namespace KillTeam.DataSlate.Console.Infrastructure.Repositories;

public class SqliteOperativeRepository
{
    private readonly ISqlExecutor _db;

    public SqliteOperativeRepository(ISqlExecutor db) => _db = db;

    public SqliteOperativeRepository(SqliteConnection connection)
        : this(new SqliteExecutor(connection)) { }

    public async Task UpsertByTeamAsync(IEnumerable<Operative> operatives, string teamId)
    {
        await _db.ExecuteTransactionAsync(async (conn, tx) =>
        {
            using var del = conn.CreateCommand();
            del.Transaction = tx;
            del.CommandText = "DELETE FROM operatives WHERE team_id = @teamId";
            del.Parameters.AddWithValue("@teamId", teamId);
            await del.ExecuteNonQueryAsync();

            foreach (var operative in operatives)
            {
                operative.TeamId = teamId;
                using var cmd = conn.CreateCommand();
                cmd.Transaction = tx;
                cmd.CommandText = """
                    INSERT INTO operatives
                    (id, team_id, name, operative_type, move, apl, wounds, save, equipment_json)
                    VALUES (@id, @teamId, @name, @operativeType, @move, @apl, @wounds, @save, @equipmentJson)
                    """;
                cmd.Parameters.AddWithValue("@id", operative.Id.ToString());
                cmd.Parameters.AddWithValue("@teamId", operative.TeamId);
                cmd.Parameters.AddWithValue("@name", operative.Name);
                cmd.Parameters.AddWithValue("@operativeType", operative.OperativeType);
                cmd.Parameters.AddWithValue("@move", operative.Move);
                cmd.Parameters.AddWithValue("@apl", operative.Apl);
                cmd.Parameters.AddWithValue("@wounds", operative.Wounds);
                cmd.Parameters.AddWithValue("@save", operative.Save);
                cmd.Parameters.AddWithValue("@equipmentJson", JsonSerializer.Serialize(operative.Equipment));
                await cmd.ExecuteNonQueryAsync();
            }
        });
    }
}
