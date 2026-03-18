using KillTeam.DataSlate.Domain.Models;
using KillTeam.DataSlate.Domain.Repositories;
using Microsoft.Data.Sqlite;

namespace KillTeam.DataSlate.Infrastructure.Repositories;

public class SqliteActivationRepository : IActivationRepository
{
    private readonly ISqlExecutor _db;

    public SqliteActivationRepository(ISqlExecutor db) => _db = db;

    public SqliteActivationRepository(SqliteConnection connection)
        : this(new SqliteExecutor(connection)) { }

    public async Task CreateAsync(Activation activation)
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
                ["@teamId"] = activation.TeamId,
                ["@orderSelected"] = activation.OrderSelected.ToString(),
                ["@isCounteract"] = activation.IsCounteract ? 1 : 0,
                ["@isGuardInterrupt"] = activation.IsGuardInterrupt ? 1 : 0,
                ["@narrativeNote"] = activation.NarrativeNote
            });
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
            reader => new Activation
            {
                Id = Guid.Parse(reader.GetString(0)),
                TurningPointId = Guid.Parse(reader.GetString(1)),
                SequenceNumber = reader.GetInt32(2),
                OperativeId = Guid.Parse(reader.GetString(3)),
                TeamId = reader.GetString(4),
                OrderSelected = Enum.Parse<Order>(reader.GetString(5)),
                IsCounteract = reader.GetInt32(6) != 0,
                IsGuardInterrupt = reader.GetInt32(7) != 0,
                NarrativeNote = reader.IsDBNull(8) ? null : reader.GetString(8)
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
