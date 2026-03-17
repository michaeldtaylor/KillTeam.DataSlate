using KillTeam.DataSlate.Domain.Models;
using KillTeam.DataSlate.Domain.Repositories;
using Microsoft.Data.Sqlite;

namespace KillTeam.DataSlate.Infrastructure.Repositories;

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
            reader => new PloyUse
            {
                Id = Guid.Parse(reader.GetString(0)),
                TurningPointId = Guid.Parse(reader.GetString(1)),
                TeamId = reader.GetString(2),
                PloyName = reader.GetString(3),
                Description = reader.IsDBNull(4) ? null : reader.GetString(4),
                CpCost = reader.GetInt32(5)
            },
            new() { ["@tpId"] = turningPointId.ToString() });
    }
}
