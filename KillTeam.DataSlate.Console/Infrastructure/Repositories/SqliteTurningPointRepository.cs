using Microsoft.Data.Sqlite;
using KillTeam.DataSlate.Domain.Models;
using KillTeam.DataSlate.Domain.Repositories;

namespace KillTeam.DataSlate.Console.Infrastructure.Repositories;

public class SqliteTurningPointRepository : ITurningPointRepository
{
    private readonly ISqlExecutor _db;

    public SqliteTurningPointRepository(ISqlExecutor db) => _db = db;

    public SqliteTurningPointRepository(SqliteConnection connection)
        : this(new SqliteExecutor(connection)) { }

    public async Task<TurningPoint> CreateAsync(TurningPoint tp)
    {
        await _db.ExecuteAsync(
            """
            INSERT INTO turning_points
            (id, game_id, number, team_with_initiative_name, cp_team_a, cp_team_b, is_strategy_phase_complete)
            VALUES (@id, @gameId, @number, @teamWithInitiativeName, @cpTeamA, @cpTeamB, @isStrategyPhaseComplete)
            """,
            new()
            {
                ["@id"] = tp.Id.ToString(),
                ["@gameId"] = tp.GameId.ToString(),
                ["@number"] = tp.Number,
                ["@teamWithInitiativeName"] = tp.TeamWithInitiativeName,
                ["@cpTeamA"] = tp.CpTeamA,
                ["@cpTeamB"] = tp.CpTeamB,
                ["@isStrategyPhaseComplete"] = tp.IsStrategyPhaseComplete ? 1 : 0
            });
        return tp;
    }

    public async Task<TurningPoint?> GetCurrentAsync(Guid gameId)
    {
        return await _db.QuerySingleAsync(
            """
            SELECT tp.id, tp.game_id, tp.number, tp.team_with_initiative_name,
                   tp.cp_team_a, tp.cp_team_b, tp.is_strategy_phase_complete
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

    private static TurningPoint MapTurningPoint(SqliteDataReader r) => new()
    {
        Id = Guid.Parse(r.GetString(0)),
        GameId = Guid.Parse(r.GetString(1)),
        Number = r.GetInt32(2),
        TeamWithInitiativeName = r.IsDBNull(3) ? null : r.GetString(3),
        CpTeamA = r.GetInt32(4),
        CpTeamB = r.GetInt32(5),
        IsStrategyPhaseComplete = r.GetInt32(6) != 0
    };
}
