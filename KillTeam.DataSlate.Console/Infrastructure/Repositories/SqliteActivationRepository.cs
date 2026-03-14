using Microsoft.Data.Sqlite;
using KillTeam.DataSlate.Domain.Models;
using KillTeam.DataSlate.Domain.Repositories;

namespace KillTeam.DataSlate.Console.Infrastructure.Repositories;

public class SqliteActivationRepository : IActivationRepository
{
    private readonly ISqlExecutor _db;

    public SqliteActivationRepository(ISqlExecutor db) => _db = db;

    public SqliteActivationRepository(SqliteConnection connection)
        : this(new SqliteExecutor(connection)) { }

    public async Task<Activation> CreateAsync(Activation activation)
    {
        await _db.ExecuteAsync(
            """
            INSERT INTO activations
            (id, turning_point_id, sequence_number, operative_id, team_id,
             order_selected, is_counteract, is_guard_interrupt, narrative_note)
            VALUES
            (@id, @turningPointId, @sequenceNumber, @operativeId, @teamId,
             @orderSelected, @isCounteract, @isGuardInterrupt, @narrativeNote)
            """,
            new()
            {
                ["@id"] = activation.Id.ToString(),
                ["@turningPointId"] = activation.TurningPointId.ToString(),
                ["@sequenceNumber"] = activation.SequenceNumber,
                ["@operativeId"] = activation.OperativeId.ToString(),
                ["@teamId"] = activation.TeamId.ToString(),
                ["@orderSelected"] = activation.OrderSelected.ToString(),
                ["@isCounteract"] = activation.IsCounteract ? 1 : 0,
                ["@isGuardInterrupt"] = activation.IsGuardInterrupt ? 1 : 0,
                ["@narrativeNote"] = activation.NarrativeNote
            });
        return activation;
    }

    public async Task<IEnumerable<Activation>> GetByTurningPointAsync(Guid turningPointId)
    {
        return await _db.QueryAsync(
            """
            SELECT id, turning_point_id, sequence_number, operative_id, team_id,
                   order_selected, is_counteract, is_guard_interrupt, narrative_note
            FROM activations
            WHERE turning_point_id = @tpId
            ORDER BY sequence_number
            """,
            r => new Activation
            {
                Id = Guid.Parse(r.GetString(0)),
                TurningPointId = Guid.Parse(r.GetString(1)),
                SequenceNumber = r.GetInt32(2),
                OperativeId = Guid.Parse(r.GetString(3)),
                TeamId = Guid.Parse(r.GetString(4)),
                OrderSelected = Enum.Parse<Order>(r.GetString(5)),
                IsCounteract = r.GetInt32(6) != 0,
                IsGuardInterrupt = r.GetInt32(7) != 0,
                NarrativeNote = r.IsDBNull(8) ? null : r.GetString(8)
            },
            new() { ["@tpId"] = turningPointId.ToString() });
    }

    public async Task UpdateNarrativeAsync(Guid id, string? note)
    {
        await _db.ExecuteAsync(
            "UPDATE activations SET narrative_note = @note WHERE id = @id",
            new() { ["@note"] = note, ["@id"] = id.ToString() });
    }
}
