using Microsoft.Data.Sqlite;
using KillTeam.DataSlate.Domain.Models;
using KillTeam.DataSlate.Domain.Repositories;

namespace KillTeam.DataSlate.Console.Infrastructure.Repositories;

public class SqliteGameRepository : IGameRepository
{
    private readonly ISqlExecutor _db;

    public SqliteGameRepository(ISqlExecutor db) => _db = db;

    public SqliteGameRepository(SqliteConnection connection)
        : this(new SqliteExecutor(connection)) { }

    public async Task<Game> CreateAsync(Game game)
    {
        await _db.ExecuteAsync(
            """
            INSERT INTO games
            (id, played_at, mission_name, team_a_name, team_b_name, player_a_id, player_b_id,
             status, cp_team_a, cp_team_b, winner_team_name, victory_points_team_a, victory_points_team_b)
            VALUES
            (@id, @playedAt, @missionName, @teamAName, @teamBName, @playerAId, @playerBId,
             @status, @cpTeamA, @cpTeamB, @winnerTeamName, @vpTeamA, @vpTeamB)
            """,
            new()
            {
                ["@id"] = game.Id.ToString(),
                ["@playedAt"] = game.PlayedAt.ToUniversalTime().ToString("o"),
                ["@missionName"] = game.MissionName,
                ["@teamAName"] = game.TeamAName,
                ["@teamBName"] = game.TeamBName,
                ["@playerAId"] = game.PlayerAId.ToString(),
                ["@playerBId"] = game.PlayerBId.ToString(),
                ["@status"] = game.Status.ToString(),
                ["@cpTeamA"] = game.CpTeamA,
                ["@cpTeamB"] = game.CpTeamB,
                ["@winnerTeamName"] = game.WinnerTeamName,
                ["@vpTeamA"] = game.VictoryPointsTeamA,
                ["@vpTeamB"] = game.VictoryPointsTeamB
            });
        return game;
    }

    public async Task<Game?> GetByIdAsync(Guid id)
    {
        return await _db.QuerySingleAsync(
            """
            SELECT id, played_at, mission_name, team_a_name, team_b_name, player_a_id, player_b_id,
                   status, cp_team_a, cp_team_b, winner_team_name, victory_points_team_a, victory_points_team_b
            FROM games WHERE id = @id
            """,
            MapGame,
            new() { ["@id"] = id.ToString() });
    }

    public async Task UpdateStatusAsync(Guid gameId, GameStatus status, string? winnerTeamName, int vpTeamA, int vpTeamB)
    {
        await _db.ExecuteAsync(
            """
            UPDATE games SET status = @status, winner_team_name = @winnerName,
                victory_points_team_a = @vpA, victory_points_team_b = @vpB
            WHERE id = @id
            """,
            new()
            {
                ["@status"] = status.ToString(),
                ["@winnerName"] = winnerTeamName,
                ["@vpA"] = vpTeamA,
                ["@vpB"] = vpTeamB,
                ["@id"] = gameId.ToString()
            });
    }

    public async Task UpdateCpAsync(Guid gameId, int cpTeamA, int cpTeamB)
    {
        await _db.ExecuteAsync(
            "UPDATE games SET cp_team_a = @cpA, cp_team_b = @cpB WHERE id = @id",
            new() { ["@cpA"] = cpTeamA, ["@cpB"] = cpTeamB, ["@id"] = gameId.ToString() });
    }

    private static Game MapGame(SqliteDataReader r) => new()
    {
        Id = Guid.Parse(r.GetString(0)),
        PlayedAt = DateTime.Parse(r.GetString(1)).ToUniversalTime(),
        MissionName = r.IsDBNull(2) ? null : r.GetString(2),
        TeamAName = r.GetString(3),
        TeamBName = r.GetString(4),
        PlayerAId = Guid.Parse(r.GetString(5)),
        PlayerBId = Guid.Parse(r.GetString(6)),
        Status = Enum.Parse<GameStatus>(r.GetString(7)),
        CpTeamA = r.GetInt32(8),
        CpTeamB = r.GetInt32(9),
        WinnerTeamName = r.IsDBNull(10) ? null : r.GetString(10),
        VictoryPointsTeamA = r.GetInt32(11),
        VictoryPointsTeamB = r.GetInt32(12)
    };
}
