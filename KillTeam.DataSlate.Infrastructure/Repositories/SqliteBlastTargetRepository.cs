using System.Text.Json;
using Microsoft.Data.Sqlite;
using KillTeam.DataSlate.Domain.Models;
using KillTeam.DataSlate.Domain.Repositories;

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
            r => new BlastTarget
            {
                Id = Guid.Parse(r.GetString(0)),
                ActionId = Guid.Parse(r.GetString(1)),
                TargetOperativeId = Guid.Parse(r.GetString(2)),
                OperativeName = r.GetString(3),
                DefenderDice = JsonSerializer.Deserialize<int[]>(r.GetString(4)) ?? [],
                NormalHits = r.GetInt32(5),
                CriticalHits = r.GetInt32(6),
                Blocks = r.GetInt32(7),
                NormalDamageDealt = r.GetInt32(8),
                CriticalDamageDealt = r.GetInt32(9),
                CausedIncapacitation = r.GetInt32(10) != 0
            },
            new() { ["@actionId"] = actionId.ToString() });
    }
}
