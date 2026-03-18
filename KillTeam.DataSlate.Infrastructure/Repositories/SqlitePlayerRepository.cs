using KillTeam.DataSlate.Domain.Models;
using KillTeam.DataSlate.Domain.Repositories;
using Microsoft.Data.Sqlite;

namespace KillTeam.DataSlate.Infrastructure.Repositories;

public class SqlitePlayerRepository : IPlayerRepository
{
    private readonly ISqlExecutor _db;

    public SqlitePlayerRepository(ISqlExecutor db) => _db = db;

    public SqlitePlayerRepository(SqliteConnection connection)
        : this(new SqliteExecutor(connection)) { }

    public async Task AddAsync(Player player)
    {
        await _db.ExecuteAsync(
            "INSERT INTO players (id, name, colour, is_internal) VALUES (@id, @name, @colour, @isInternal)",
            new()
            {
                ["@id"] = player.Id.ToString(),
                ["@name"] = player.Name,
                ["@colour"] = player.Colour,
                ["@isInternal"] = player.IsInternal ? 1 : 0,
            });
    }

    public async Task<IEnumerable<Player>> GetAllAsync()
    {
        return await _db.QueryAsync(
            "SELECT id, name, colour, is_internal FROM players WHERE is_internal = 0 ORDER BY name",
            reader => new Player
            {
                Id = Guid.Parse(reader.GetString(0)),
                Name = reader.GetString(1),
                Colour = reader.GetString(2),
                IsInternal = reader.GetInt32(3) != 0,
            });
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
            "SELECT id, name, colour, is_internal FROM players WHERE name = @name COLLATE NOCASE LIMIT 1",
            reader => new Player
            {
                Id = Guid.Parse(reader.GetString(0)),
                Name = reader.GetString(1),
                Colour = reader.GetString(2),
                IsInternal = reader.GetInt32(3) != 0,
            },
            new() { ["@name"] = name });
    }

    public async Task<int> CountGamesAsync(Guid playerId)
    {
        return await _db.ScalarAsync<int>(
            "SELECT COUNT(*) FROM games WHERE participant1_player_id = @id OR participant2_player_id = @id",
            new() { ["@id"] = playerId.ToString() });
    }

    public async Task<IReadOnlyList<PlayerStats>> GetAllWithStatsAsync(string? nameFilter = null)
    {
        var sql = """
            SELECT p.id, p.name,
                   COUNT(g.id) AS games_played,
                   COALESCE(SUM(CASE
                       WHEN g.participant1_player_id = p.id AND g.winner_team_id = g.participant1_team_id THEN 1
                       WHEN g.participant2_player_id = p.id AND g.winner_team_id = g.participant2_team_id THEN 1
                       ELSE 0 END), 0) AS wins
            FROM players p
            LEFT JOIN games g ON (g.participant1_player_id = p.id OR g.participant2_player_id = p.id)
                AND g.status = 'Completed'
            """;

        Dictionary<string, object?> parameters = new();

        sql += " WHERE p.is_internal = 0";

        if (!string.IsNullOrWhiteSpace(nameFilter))
        {
            sql += " AND p.name LIKE @filter COLLATE NOCASE";
            parameters["@filter"] = $"%{nameFilter}%";
        }

        sql += " GROUP BY p.id, p.name ORDER BY p.name";

        return await _db.QueryAsync(
            sql,
            reader => new PlayerStats(
                Guid.Parse(reader.GetString(0)),
                reader.GetString(1),
                reader.GetInt32(2),
                reader.GetInt32(3)),
            parameters);
    }
}
