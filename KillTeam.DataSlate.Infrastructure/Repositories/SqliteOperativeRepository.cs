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

    public async Task<string?> GetNameByIdAsync(Guid id)
    {
        return await _db.ScalarAsync<string>(
            "SELECT name FROM operatives WHERE id = @id",
            new() { ["@id"] = id.ToString() });
    }
}
