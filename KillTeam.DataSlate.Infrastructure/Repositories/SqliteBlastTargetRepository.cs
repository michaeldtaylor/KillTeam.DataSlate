using System.Text.Json;
using KillTeam.DataSlate.Domain.Models;
using KillTeam.DataSlate.Domain.Repositories;
using Microsoft.Data.Sqlite;

namespace KillTeam.DataSlate.Infrastructure.Repositories;

public class SqliteBlastTargetRepository : IBlastTargetRepository
{
    private readonly ISqlExecutor _db;

    public SqliteBlastTargetRepository(ISqlExecutor db) => _db = db;

    public SqliteBlastTargetRepository(SqliteConnection connection)
        : this(new SqliteExecutor(connection)) { }

    public async Task CreateAsync(BlastTarget target)
    {
        await _db.ExecuteAsync(
            """
            INSERT INTO action_blast_targets
            (id, action_id, target_operative_id, operative_name, defender_dice,
             normal_hits, critical_hits, blocks, normal_damage_dealt, critical_damage_dealt,
             caused_incapacitation)
            VALUES
            (@id, @actionId, @targetOperativeId, @operativeName, @defenderDice,
             @normalHits, @criticalHits, @blocks, @normalDamageDealt, @criticalDamageDealt,
             @causedIncapacitation)
            """,
            new()
            {
                ["@id"] = target.Id.ToString(),
                ["@actionId"] = target.ActionId.ToString(),
                ["@targetOperativeId"] = target.TargetOperativeId.ToString(),
                ["@operativeName"] = target.OperativeName,
                ["@defenderDice"] = JsonSerializer.Serialize(target.DefenderDice),
                ["@normalHits"] = target.NormalHits,
                ["@criticalHits"] = target.CriticalHits,
                ["@blocks"] = target.Blocks,
                ["@normalDamageDealt"] = target.NormalDamageDealt,
                ["@criticalDamageDealt"] = target.CriticalDamageDealt,
                ["@causedIncapacitation"] = target.CausedIncapacitation ? 1 : 0
            });
    }

    public async Task<IEnumerable<BlastTarget>> GetByActionIdAsync(Guid actionId)
    {
        return await _db.QueryAsync(
            """
            SELECT id, action_id, target_operative_id, operative_name, defender_dice,
                   normal_hits, critical_hits, blocks, normal_damage_dealt, critical_damage_dealt,
                   caused_incapacitation
            FROM action_blast_targets WHERE action_id = @actionId
            """,
            reader => new BlastTarget
            {
                Id = Guid.Parse(reader.GetString(0)),
                ActionId = Guid.Parse(reader.GetString(1)),
                TargetOperativeId = Guid.Parse(reader.GetString(2)),
                OperativeName = reader.GetString(3),
                DefenderDice = JsonSerializer.Deserialize<int[]>(reader.GetString(4)) ?? [],
                NormalHits = reader.GetInt32(5),
                CriticalHits = reader.GetInt32(6),
                Blocks = reader.GetInt32(7),
                NormalDamageDealt = reader.GetInt32(8),
                CriticalDamageDealt = reader.GetInt32(9),
                CausedIncapacitation = reader.GetInt32(10) != 0
            },
            new() { ["@actionId"] = actionId.ToString() });
    }
}
