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
            (id, played_at, mission_name, team_a_id, team_a_name, team_b_id, team_b_name,
             player_a_id, player_b_id, status, cp_team_a, cp_team_b,
             winner_team_id, victory_points_team_a, victory_points_team_b)
            VALUES
            (@id, @playedAt, @missionName, @teamAId, @teamAName, @teamBId, @teamBName,
             @playerAId, @playerBId, @status, @cpTeamA, @cpTeamB,
             @winnerTeamId, @vpTeamA, @vpTeamB)
            """,
            new()
            {
                ["@id"] = game.Id.ToString(),
                ["@playedAt"] = game.PlayedAt.ToUniversalTime().ToString("o"),
                ["@missionName"] = game.MissionName,
                ["@teamAId"] = game.TeamA.TeamId,
                ["@teamAName"] = game.TeamA.TeamName,
                ["@teamBId"] = game.TeamB.TeamId,
                ["@teamBName"] = game.TeamB.TeamName,
                ["@playerAId"] = game.TeamA.PlayerId.ToString(),
                ["@playerBId"] = game.TeamB.PlayerId.ToString(),
                ["@status"] = game.Status.ToString(),
                ["@cpTeamA"] = game.TeamA.CommandPoints,
                ["@cpTeamB"] = game.TeamB.CommandPoints,
                ["@winnerTeamId"] = game.WinnerTeamId,
                ["@vpTeamA"] = game.TeamA.VictoryPoints,
                ["@vpTeamB"] = game.TeamB.VictoryPoints
            });
        return game;
    }

    public async Task<Game?> GetByIdAsync(Guid id)
    {
        return await _db.QuerySingleAsync(
            """
            SELECT id, played_at, mission_name,
                   team_a_id, team_a_name, player_a_id, cp_team_a, victory_points_team_a,
                   team_b_id, team_b_name, player_b_id, cp_team_b, victory_points_team_b,
                   status, winner_team_id
            FROM games WHERE id = @id
            """,
            MapGame,
            new() { ["@id"] = id.ToString() });
    }

    public async Task UpdateStatusAsync(Guid gameId, GameStatus status, string? winnerTeamId, int vpTeamA, int vpTeamB)
    {
        await _db.ExecuteAsync(
            """
            UPDATE games SET status = @status, winner_team_id = @winnerId,
                victory_points_team_a = @vpA, victory_points_team_b = @vpB
            WHERE id = @id
            """,
            new()
            {
                ["@status"] = status.ToString(),
                ["@winnerId"] = winnerTeamId,
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
        TeamA = new GameParticipant
        {
            TeamId = r.GetString(3),
            TeamName = r.GetString(4),
            PlayerId = Guid.Parse(r.GetString(5)),
            CommandPoints = r.GetInt32(6),
            VictoryPoints = r.GetInt32(7)
        },
        TeamB = new GameParticipant
        {
            TeamId = r.GetString(8),
            TeamName = r.GetString(9),
            PlayerId = Guid.Parse(r.GetString(10)),
            CommandPoints = r.GetInt32(11),
            VictoryPoints = r.GetInt32(12)
        },
        Status = Enum.Parse<GameStatus>(r.GetString(13)),
        WinnerTeamId = r.IsDBNull(14) ? null : r.GetString(14)
    };
}
