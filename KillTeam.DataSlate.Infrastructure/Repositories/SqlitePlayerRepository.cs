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

    public async Task CreateAsync(Player player)
    {
        await _db.ExecuteAsync(
            "INSERT INTO players (id, username, first_name, last_name, colour) VALUES (@id, @username, @firstName, @lastName, @colour)",
            new()
            {
                ["@id"] = player.Id.ToString(),
                ["@username"] = player.Username,
                ["@firstName"] = player.FirstName,
                ["@lastName"] = player.LastName,
                ["@colour"] = player.Colour,
            });
    }

    public async Task<IEnumerable<Player>> GetAllAsync()
    {
        return await _db.QueryAsync(
            "SELECT id, username, first_name, last_name, colour FROM players ORDER BY username",
            reader => new Player
            {
                Id = Guid.Parse(reader.GetString(0)),
                Username = reader.GetString(1),
                FirstName = reader.GetString(2),
                LastName = reader.GetString(3),
                Colour = reader.GetString(4),
            });
    }

    public async Task DeleteAsync(Guid id)
    {
        await _db.ExecuteAsync(
            "DELETE FROM players WHERE id = @id",
            new() { ["@id"] = id.ToString() });
    }

    public async Task<Player?> FindByUsernameAsync(string username)
    {
        return await _db.QuerySingleAsync(
            "SELECT id, username, first_name, last_name, colour FROM players WHERE username = @username COLLATE NOCASE LIMIT 1",
            reader => new Player
            {
                Id = Guid.Parse(reader.GetString(0)),
                Username = reader.GetString(1),
                FirstName = reader.GetString(2),
                LastName = reader.GetString(3),
                Colour = reader.GetString(4),
            },
            new() { ["@username"] = username });
    }

    public async Task<int> CountGamesAsync(Guid playerId)
    {
        return await _db.ScalarAsync<int>(
            "SELECT COUNT(*) FROM games WHERE participant1_player_id = @id OR participant2_player_id = @id",
            new() { ["@id"] = playerId.ToString() });
    }

    public async Task<IReadOnlyList<PlayerStats>> GetAllWithStatsAsync(string? usernameFilter = null)
    {
        var sql = """
            SELECT p.id, p.username, p.first_name, p.last_name,
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

        sql += " WHERE 1=1";

        if (!string.IsNullOrWhiteSpace(usernameFilter))
        {
            sql += " AND p.username LIKE @filter COLLATE NOCASE";
            parameters["@filter"] = $"%{usernameFilter}%";
        }

        sql += " GROUP BY p.id, p.username, p.first_name, p.last_name ORDER BY p.username";

        return await _db.QueryAsync(
            sql,
            reader => new PlayerStats(
                Guid.Parse(reader.GetString(0)),
                reader.GetString(1),
                reader.GetString(2),
                reader.GetString(3),
                reader.GetInt32(4),
                reader.GetInt32(5)),
            parameters);
    }
}
