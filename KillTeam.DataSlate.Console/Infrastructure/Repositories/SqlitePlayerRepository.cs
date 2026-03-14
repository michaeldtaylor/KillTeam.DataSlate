using Microsoft.Data.Sqlite;
using KillTeam.DataSlate.Domain.Models;
using KillTeam.DataSlate.Domain.Repositories;

namespace KillTeam.DataSlate.Console.Infrastructure.Repositories;

public class SqlitePlayerRepository : IPlayerRepository
{
    private readonly ISqlExecutor _db;

    public SqlitePlayerRepository(ISqlExecutor db) => _db = db;

    public SqlitePlayerRepository(SqliteConnection connection)
        : this(new SqliteExecutor(connection)) { }

    public async Task AddAsync(Player player)
    {
        await _db.ExecuteAsync(
            "INSERT INTO players (id, name) VALUES (@id, @name)",
            new() { ["@id"] = player.Id.ToString(), ["@name"] = player.Name });
    }

    public async Task<IEnumerable<Player>> GetAllAsync()
    {
        return await _db.QueryAsync(
            "SELECT id, name FROM players ORDER BY name",
            r => new Player { Id = Guid.Parse(r.GetString(0)), Name = r.GetString(1) });
    }

    public async Task DeleteAsync(Guid id)
    {
        await _db.ExecuteAsync(
            "DELETE FROM players WHERE id = @id",
            new() { ["@id"] = id.ToString() });
    }

    public async Task<Player?> FindByNameAsync(string name)
    {
        return await _db.QuerySingleAsync(
            "SELECT id, name FROM players WHERE name = @name COLLATE NOCASE LIMIT 1",
            r => new Player { Id = Guid.Parse(r.GetString(0)), Name = r.GetString(1) },
            new() { ["@name"] = name });
    }
}
