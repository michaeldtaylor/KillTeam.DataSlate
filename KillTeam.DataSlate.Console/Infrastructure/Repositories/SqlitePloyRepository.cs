using Microsoft.Data.Sqlite;
using KillTeam.DataSlate.Domain.Models;
using KillTeam.DataSlate.Domain.Repositories;

namespace KillTeam.DataSlate.Console.Infrastructure.Repositories;

public class SqlitePloyRepository : IPloyRepository
{
    private readonly ISqlExecutor _db;

    public SqlitePloyRepository(ISqlExecutor db) => _db = db;

    public SqlitePloyRepository(SqliteConnection connection)
        : this(new SqliteExecutor(connection)) { }

    public async Task RecordPloyUseAsync(PloyUse ploy)
    {
        await _db.ExecuteAsync(
            """
            INSERT INTO ploy_uses
            (id, turning_point_id, team_id, ploy_name, description, cp_cost)
            VALUES (@id, @turningPointId, @teamId, @ployName, @description, @cpCost)
            """,
            new()
            {
                ["@id"] = ploy.Id.ToString(),
                ["@turningPointId"] = ploy.TurningPointId.ToString(),
                ["@teamId"] = ploy.TeamId,
                ["@ployName"] = ploy.PloyName,
                ["@description"] = ploy.Description,
                ["@cpCost"] = ploy.CpCost
            });
    }

    public async Task<IEnumerable<PloyUse>> GetByTurningPointAsync(Guid turningPointId)
    {
        return await _db.QueryAsync(
            """
            SELECT id, turning_point_id, team_id, ploy_name, description, cp_cost
            FROM ploy_uses WHERE turning_point_id = @tpId
            """,
            r => new PloyUse
            {
                Id = Guid.Parse(r.GetString(0)),
                TurningPointId = Guid.Parse(r.GetString(1)),
                TeamId = r.GetString(2),
                PloyName = r.GetString(3),
                Description = r.IsDBNull(4) ? null : r.GetString(4),
                CpCost = r.GetInt32(5)
            },
            new() { ["@tpId"] = turningPointId.ToString() });
    }
}
