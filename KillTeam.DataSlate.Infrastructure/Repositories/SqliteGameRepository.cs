using KillTeam.DataSlate.Domain.Models;
using KillTeam.DataSlate.Domain.Repositories;
using Microsoft.Data.Sqlite;

namespace KillTeam.DataSlate.Infrastructure.Repositories;

public class SqliteGameRepository : IGameRepository
{
    private readonly ISqlExecutor _db;

    public SqliteGameRepository(ISqlExecutor db) => _db = db;

    public SqliteGameRepository(SqliteConnection connection)
        : this(new SqliteExecutor(connection)) { }

    public async Task CreateAsync(Game game)
    {
        await _db.ExecuteAsync(
            """
            INSERT INTO games
            (id, played_at, mission_name,
             participant1_team_id, participant1_team_name, participant1_faction, participant1_grand_faction,
             participant2_team_id, participant2_team_name, participant2_faction, participant2_grand_faction,
             participant1_player_id, participant2_player_id, status,
             participant1_command_points, participant2_command_points,
             winner_team_id, participant1_victory_points, participant2_victory_points)
            VALUES
            (@id, @playedAt, @missionName,
             @team1Id, @team1Name, @team1Faction, @team1GrandFaction,
             @team2Id, @team2Name, @team2Faction, @team2GrandFaction,
             @player1Id, @player2Id, @status,
             @cpTeam1, @cpTeam2,
             @winnerTeamId, @vpTeam1, @vpTeam2)
            """,
            new()
            {
                ["@id"] = game.Id.ToString(),
                ["@playedAt"] = game.StartedAt.ToUniversalTime().ToString("o"),
                ["@missionName"] = game.MissionName,
                ["@team1Id"] = game.Participant1.Team.Id,
                ["@team1Name"] = game.Participant1.Team.Name,
                ["@team1Faction"] = game.Participant1.Team.Faction,
                ["@team1GrandFaction"] = game.Participant1.Team.GrandFaction,
                ["@team2Id"] = game.Participant2.Team.Id,
                ["@team2Name"] = game.Participant2.Team.Name,
                ["@team2Faction"] = game.Participant2.Team.Faction,
                ["@team2GrandFaction"] = game.Participant2.Team.GrandFaction,
                ["@player1Id"] = game.Participant1.PlayerId.ToString(),
                ["@player2Id"] = game.Participant2.PlayerId.ToString(),
                ["@status"] = game.Status.ToString(),
                ["@cpTeam1"] = game.Participant1.CommandPoints,
                ["@cpTeam2"] = game.Participant2.CommandPoints,
                ["@winnerTeamId"] = game.WinnerTeamId,
                ["@vpTeam1"] = game.Participant1.VictoryPoints,
                ["@vpTeam2"] = game.Participant2.VictoryPoints
            });
    }

    public async Task<Game?> GetByIdAsync(Guid id)
    {
        return await _db.QuerySingleAsync(
            """
            SELECT id, played_at, mission_name,
                   participant1_team_id, participant1_team_name, participant1_faction, participant1_grand_faction,
                   participant1_player_id, participant1_command_points, participant1_victory_points,
                   participant2_team_id, participant2_team_name, participant2_faction, participant2_grand_faction,
                   participant2_player_id, participant2_command_points, participant2_victory_points,
                   status, winner_team_id
            FROM games WHERE id = @id
            """,
            MapGame,
            new() { ["@id"] = id.ToString() });
    }

    public async Task UpdateStatusAsync(Guid id, GameStatus status, string? winnerTeamId, int victoryPointsParticipant1, int victoryPointsParticipant2)
    {
        await _db.ExecuteAsync(
            """
            UPDATE games SET status = @status, winner_team_id = @winnerId,
                participant1_victory_points = @victoryPoints1, participant2_victory_points = @victoryPoints2
            WHERE id = @id
            """,
            new()
            {
                ["@status"] = status.ToString(),
                ["@winnerId"] = winnerTeamId,
                ["@victoryPoints1"] = victoryPointsParticipant1,
                ["@victoryPoints2"] = victoryPointsParticipant2,
                ["@id"] = id.ToString()
            });
    }

    public async Task UpdateCommandPointsAsync(Guid id, int commandPointsParticipant1, int commandPointsParticipant2)
    {
        await _db.ExecuteAsync(
            "UPDATE games SET participant1_command_points = @cp1, participant2_command_points = @cp2 WHERE id = @id",
            new() { ["@cp1"] = commandPointsParticipant1, ["@cp2"] = commandPointsParticipant2, ["@id"] = id.ToString() });
    }

    public async Task<GameHeader?> GetHeaderAsync(Guid gameId)
    {
        return await _db.QuerySingleAsync(
            """
            SELECT g.status, g.mission_name,
                   pa.name, g.participant1_team_name, pb.name, g.participant2_team_name,
                   CASE WHEN g.winner_team_id = g.participant1_team_id THEN g.participant1_team_name
                        WHEN g.winner_team_id = g.participant2_team_id THEN g.participant2_team_name
                        ELSE NULL END,
                   g.participant1_victory_points, g.participant2_victory_points
            FROM games g
            JOIN players pa ON pa.id = g.participant1_player_id
            JOIN players pb ON pb.id = g.participant2_player_id
            WHERE g.id = @id
            """,
            reader => new GameHeader(
                Enum.Parse<GameStatus>(reader.GetString(0)),
                reader.IsDBNull(1) ? null : reader.GetString(1),
                reader.GetString(2),
                reader.GetString(3),
                reader.GetString(4),
                reader.GetString(5),
                reader.IsDBNull(6) ? null : reader.GetString(6),
                reader.GetInt32(7),
                reader.GetInt32(8)),
            new() { ["@id"] = gameId.ToString() });
    }

    public async Task<IReadOnlyList<GameHistoryEntry>> GetHistoryAsync(string? playerNameFilter = null)
    {
        var sql = """
            SELECT g.id, g.played_at, g.mission_name,
                   pa.name, g.participant1_team_name, pb.name, g.participant2_team_name,
                   g.participant1_victory_points, g.participant2_victory_points,
                   CASE WHEN g.winner_team_id = g.participant1_team_id THEN g.participant1_team_name
                        WHEN g.winner_team_id = g.participant2_team_id THEN g.participant2_team_name
                        ELSE NULL END
            FROM games g
            JOIN players pa ON pa.id = g.participant1_player_id
            JOIN players pb ON pb.id = g.participant2_player_id
            WHERE g.status = 'Completed'
            """;

        Dictionary<string, object?> parameters = new();

        if (!string.IsNullOrWhiteSpace(playerNameFilter))
        {
            sql += " AND (pa.name LIKE @playerFilter OR pb.name LIKE @playerFilter) COLLATE NOCASE";
            parameters["@playerFilter"] = $"%{playerNameFilter}%";
        }

        sql += " ORDER BY g.played_at DESC";

        return await _db.QueryAsync(
            sql,
            reader => new GameHistoryEntry(
                Guid.Parse(reader.GetString(0)),
                reader.GetString(1),
                reader.IsDBNull(2) ? null : reader.GetString(2),
                reader.GetString(3),
                reader.GetString(4),
                reader.GetString(5),
                reader.GetString(6),
                reader.GetInt32(7),
                reader.GetInt32(8),
                reader.IsDBNull(9) ? null : reader.GetString(9)),
            parameters);
    }

    private static Game MapGame(SqliteDataReader reader) => new()
    {
        Id = Guid.Parse(reader.GetString(0)),
        StartedAt = DateTime.Parse(reader.GetString(1)).ToUniversalTime(),
        MissionName = reader.IsDBNull(2) ? null : reader.GetString(2),
        Participant1 = new GameParticipant
        {
            Team = new TeamSummary(reader.GetString(3), reader.GetString(4), reader.GetString(5), reader.GetString(6)),
            PlayerId = Guid.Parse(reader.GetString(7)),
            CommandPoints = reader.GetInt32(8),
            VictoryPoints = reader.GetInt32(9)
        },
        Participant2 = new GameParticipant
        {
            Team = new TeamSummary(reader.GetString(10), reader.GetString(11), reader.GetString(12), reader.GetString(13)),
            PlayerId = Guid.Parse(reader.GetString(14)),
            CommandPoints = reader.GetInt32(15),
            VictoryPoints = reader.GetInt32(16)
        },
        Status = Enum.Parse<GameStatus>(reader.GetString(17)),
        WinnerTeamId = reader.IsDBNull(18) ? null : reader.GetString(18)
    };
}
