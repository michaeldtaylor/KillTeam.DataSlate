using Microsoft.Data.Sqlite;
using KillTeam.DataSlate.Domain.Models;
using KillTeam.DataSlate.Domain.Repositories;

namespace KillTeam.DataSlate.Infrastructure.Repositories;

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
            (id, played_at, mission_name, participant1_team_id, participant1_team_name, participant2_team_id, participant2_team_name,
             participant1_player_id, participant2_player_id, status, participant1_command_points, participant2_command_points,
             winner_team_id, participant1_victory_points, participant2_victory_points)
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
                ["@teamAId"] = game.Participant1.TeamId,
                ["@teamAName"] = game.Participant1.TeamName,
                ["@teamBId"] = game.Participant2.TeamId,
                ["@teamBName"] = game.Participant2.TeamName,
                ["@playerAId"] = game.Participant1.PlayerId.ToString(),
                ["@playerBId"] = game.Participant2.PlayerId.ToString(),
                ["@status"] = game.Status.ToString(),
                ["@cpTeamA"] = game.Participant1.CommandPoints,
                ["@cpTeamB"] = game.Participant2.CommandPoints,
                ["@winnerTeamId"] = game.WinnerTeamId,
                ["@vpTeamA"] = game.Participant1.VictoryPoints,
                ["@vpTeamB"] = game.Participant2.VictoryPoints
            });
        return game;
    }

    public async Task<Game?> GetByIdAsync(Guid id)
    {
        return await _db.QuerySingleAsync(
            """
            SELECT id, played_at, mission_name,
                   participant1_team_id, participant1_team_name, participant1_player_id, participant1_command_points, participant1_victory_points,
                   participant2_team_id, participant2_team_name, participant2_player_id, participant2_command_points, participant2_victory_points,
                   status, winner_team_id
            FROM games WHERE id = @id
            """,
            MapGame,
            new() { ["@id"] = id.ToString() });
    }

    public async Task UpdateStatusAsync(Guid gameId, GameStatus status, string? winnerTeamId, int victoryPointsParticipant1, int victoryPointsParticipant2)
    {
        await _db.ExecuteAsync(
            """
            UPDATE games SET status = @status, winner_team_id = @winnerId,
                participant1_victory_points = @vpA, participant2_victory_points = @vpB
            WHERE id = @id
            """,
            new()
            {
                ["@status"] = status.ToString(),
                ["@winnerId"] = winnerTeamId,
                ["@vpA"] = victoryPointsParticipant1,
                ["@vpB"] = victoryPointsParticipant2,
                ["@id"] = gameId.ToString()
            });
    }

    public async Task UpdateCpAsync(Guid gameId, int commandPointsParticipant1, int commandPointsParticipant2)
    {
        await _db.ExecuteAsync(
            "UPDATE games SET participant1_command_points = @cpA, participant2_command_points = @cpB WHERE id = @id",
            new() { ["@cpA"] = commandPointsParticipant1, ["@cpB"] = commandPointsParticipant2, ["@id"] = gameId.ToString() });
    }

    private static Game MapGame(SqliteDataReader r) => new()
    {
        Id = Guid.Parse(r.GetString(0)),
        PlayedAt = DateTime.Parse(r.GetString(1)).ToUniversalTime(),
        MissionName = r.IsDBNull(2) ? null : r.GetString(2),
        Participant1 = new GameParticipant
        {
            TeamId = r.GetString(3),
            TeamName = r.GetString(4),
            PlayerId = Guid.Parse(r.GetString(5)),
            CommandPoints = r.GetInt32(6),
            VictoryPoints = r.GetInt32(7)
        },
        Participant2 = new GameParticipant
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
