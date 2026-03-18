using KillTeam.DataSlate.Domain.Models;
using KillTeam.DataSlate.Domain.Repositories;
using Microsoft.Data.Sqlite;

namespace KillTeam.DataSlate.Infrastructure.Repositories;

public class SqliteTurningPointRepository : ITurningPointRepository
{
    private readonly ISqlExecutor _db;

    public SqliteTurningPointRepository(ISqlExecutor db) => _db = db;

    public SqliteTurningPointRepository(SqliteConnection connection)
        : this(new SqliteExecutor(connection)) { }

    public async Task<TurningPoint> CreateAsync(TurningPoint turningPoint)
    {
        await _db.ExecuteAsync(
            """
            INSERT INTO turning_points
            (id, game_id, number, team_with_initiative_id, command_points_participant1, command_points_participant2, is_strategy_phase_complete)
            VALUES (@id, @gameId, @number, @teamWithInitiativeId, @cpTeam1, @cpTeam2, @isStrategyPhaseComplete)
            """,
            new()
            {
                ["@id"] = turningPoint.Id.ToString(),
                ["@gameId"] = turningPoint.GameId.ToString(),
                ["@number"] = turningPoint.Number,
                ["@teamWithInitiativeId"] = turningPoint.TeamWithInitiativeId,
                ["@cpTeam1"] = turningPoint.CommandPointsParticipant1,
                ["@cpTeam2"] = turningPoint.CommandPointsParticipant2,
                ["@isStrategyPhaseComplete"] = turningPoint.IsStrategyPhaseComplete ? 1 : 0
            });

        return turningPoint;
    }

    public async Task<TurningPoint?> GetCurrentAsync(Guid gameId)
    {
        return await _db.QuerySingleAsync(
            """
            SELECT tp.id, tp.game_id, tp.number, tp.team_with_initiative_id,
                   tp.command_points_participant1, tp.command_points_participant2, tp.is_strategy_phase_complete
            FROM turning_points tp
            JOIN games g ON g.id = tp.game_id
            WHERE tp.game_id = @gameId AND g.status = 'InProgress'
            ORDER BY tp.number DESC
            LIMIT 1
            """,
            MapTurningPoint,
            new() { ["@gameId"] = gameId.ToString() });
    }

    public async Task CompleteStrategyPhaseAsync(Guid id)
    {
        await _db.ExecuteAsync(
            "UPDATE turning_points SET is_strategy_phase_complete = 1 WHERE id = @id",
            new() { ["@id"] = id.ToString() });
    }

    public async Task<bool> IsStrategyPhaseCompleteAsync(Guid id)
    {
        var result = await _db.ScalarAsync<int>(
            "SELECT is_strategy_phase_complete FROM turning_points WHERE id = @id",
            new() { ["@id"] = id.ToString() });

        return result != 0;
    }

    public async Task<IReadOnlyList<TurningPointSummary>> GetSummariesByGameAsync(Guid gameId)
    {
        return await _db.QueryAsync(
            """
            SELECT tp.id, tp.number, t.name
            FROM turning_points tp
            LEFT JOIN teams t ON t.id = tp.team_with_initiative_id
            WHERE tp.game_id = @gameId
            ORDER BY tp.number
            """,
            reader => new TurningPointSummary(
                Guid.Parse(reader.GetString(0)),
                reader.GetInt32(1),
                reader.IsDBNull(2) ? null : reader.GetString(2)),
            new() { ["@gameId"] = gameId.ToString() });
    }

    private static TurningPoint MapTurningPoint(SqliteDataReader reader) => new()
    {
        Id = Guid.Parse(reader.GetString(0)),
        GameId = Guid.Parse(reader.GetString(1)),
        Number = reader.GetInt32(2),
        TeamWithInitiativeId = reader.IsDBNull(3) ? null : reader.GetString(3),
        CommandPointsParticipant1 = reader.GetInt32(4),
        CommandPointsParticipant2 = reader.GetInt32(5),
        IsStrategyPhaseComplete = reader.GetInt32(6) != 0
    };
}
