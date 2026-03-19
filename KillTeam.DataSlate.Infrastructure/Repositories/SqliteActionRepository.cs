using System.Text.Json;
using KillTeam.DataSlate.Domain.Models;
using KillTeam.DataSlate.Domain.Repositories;
using Microsoft.Data.Sqlite;

namespace KillTeam.DataSlate.Infrastructure.Repositories;

public class SqliteActionRepository : IActionRepository
{
    private readonly ISqlExecutor _db;

    public SqliteActionRepository(ISqlExecutor db) => _db = db;

    public SqliteActionRepository(SqliteConnection connection)
        : this(new SqliteExecutor(connection)) { }

    public async Task CreateAsync(GameAction action)
    {
        await _db.ExecuteAsync(
            """
            INSERT INTO actions
            (id, activation_id, type, ap_cost, target_operative_id, weapon_id,
             attacker_dice, target_dice, target_in_cover, is_obscured,
             normal_hits, critical_hits, blocks,
             normal_damage_dealt, critical_damage_dealt, self_damage_dealt,
             stun_applied, caused_incapacitation, narrative_note)
            VALUES
            (@id, @activationId, @type, @apCost, @targetOperativeId, @weaponId,
             @attackerDice, @targetDice, @targetInCover, @isObscured,
             @normalHits, @criticalHits, @blocks,
             @normalDamageDealt, @criticalDamageDealt, @selfDamageDealt,
             @stunApplied, @causedIncapacitation, @narrativeNote)
            """,
            new()
            {
                ["@id"] = action.Id.ToString(),
                ["@activationId"] = action.ActivationId.ToString(),
                ["@type"] = action.Type.ToString(),
                ["@apCost"] = action.ApCost,
                ["@targetOperativeId"] = action.TargetOperativeId?.ToString(),
                ["@weaponId"] = action.WeaponId?.ToString(),
                ["@attackerDice"] = JsonSerializer.Serialize(action.AttackerDice),
                ["@targetDice"] = JsonSerializer.Serialize(action.TargetDice),
                ["@targetInCover"] = action.TargetInCover.HasValue ? (object?)(action.TargetInCover.Value ? 1 : 0) : null,
                ["@isObscured"] = action.IsObscured.HasValue ? (object?)(action.IsObscured.Value ? 1 : 0) : null,
                ["@normalHits"] = action.NormalHits,
                ["@criticalHits"] = action.CriticalHits,
                ["@blocks"] = action.Blocks,
                ["@normalDamageDealt"] = action.NormalDamageDealt,
                ["@criticalDamageDealt"] = action.CriticalDamageDealt,
                ["@selfDamageDealt"] = action.SelfDamageDealt,
                ["@stunApplied"] = action.StunApplied ? 1 : 0,
                ["@causedIncapacitation"] = action.CausedIncapacitation ? 1 : 0,
                ["@narrativeNote"] = action.NarrativeNote
            });
    }

    public async Task UpdateNarrativeAsync(Guid id, string? note)
    {
        await _db.ExecuteAsync(
            "UPDATE actions SET narrative_note = @note WHERE id = @id",
            new() { ["@note"] = note, ["@id"] = id.ToString() });
    }

    public async Task<IEnumerable<GameAction>> GetByActivationAsync(Guid activationId)
    {
        return await _db.QueryAsync(
            """
            SELECT id, activation_id, type, ap_cost, target_operative_id, weapon_id,
                   attacker_dice, target_dice, target_in_cover, is_obscured,
                   normal_hits, critical_hits, blocks, normal_damage_dealt,
                   critical_damage_dealt, caused_incapacitation, self_damage_dealt,
                   stun_applied, narrative_note
            FROM actions WHERE activation_id = @id ORDER BY rowid
            """,
            reader => new GameAction
            {
                Id = Guid.Parse(reader.GetString(0)),
                ActivationId = Guid.Parse(reader.GetString(1)),
                Type = Enum.Parse<ActionType>(reader.GetString(2)),
                ApCost = reader.GetInt32(3),
                TargetOperativeId = reader.IsDBNull(4) ? null : Guid.Parse(reader.GetString(4)),
                WeaponId = reader.IsDBNull(5) ? null : Guid.Parse(reader.GetString(5)),
                AttackerDice = reader.IsDBNull(6) ? [] : JsonSerializer.Deserialize<int[]>(reader.GetString(6)) ?? [],
                TargetDice = reader.IsDBNull(7) ? [] : JsonSerializer.Deserialize<int[]>(reader.GetString(7)) ?? [],
                TargetInCover = reader.IsDBNull(8) ? null : reader.GetInt32(8) != 0,
                IsObscured = reader.IsDBNull(9) ? null : reader.GetInt32(9) != 0,
                NormalHits = reader.GetInt32(10),
                CriticalHits = reader.GetInt32(11),
                Blocks = reader.GetInt32(12),
                NormalDamageDealt = reader.GetInt32(13),
                CriticalDamageDealt = reader.GetInt32(14),
                CausedIncapacitation = reader.GetInt32(15) != 0,
                SelfDamageDealt = reader.GetInt32(16),
                StunApplied = reader.GetInt32(17) != 0,
                NarrativeNote = reader.IsDBNull(18) ? null : reader.GetString(18)
            },
            new() { ["@id"] = activationId.ToString() });
    }
}
