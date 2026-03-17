using System.Text.Json;
using Microsoft.Data.Sqlite;
using KillTeam.DataSlate.Domain.Models;
using KillTeam.DataSlate.Domain.Repositories;

namespace KillTeam.DataSlate.Infrastructure.Repositories;

public class SqliteActionRepository : IActionRepository
{
    private readonly ISqlExecutor _db;

    public SqliteActionRepository(ISqlExecutor db) => _db = db;

    public SqliteActionRepository(SqliteConnection connection)
        : this(new SqliteExecutor(connection)) { }

    public async Task<GameAction> CreateAsync(GameAction action)
    {
        await _db.ExecuteAsync(
            """
            INSERT INTO actions
            (id, activation_id, type, ap_cost, target_operative_id, weapon_id,
             attacker_dice, defender_dice, target_in_cover, is_obscured,
             normal_hits, critical_hits, blocks,
             normal_damage_dealt, critical_damage_dealt, self_damage_dealt,
             stun_applied, caused_incapacitation, narrative_note)
            VALUES
            (@id, @activationId, @type, @apCost, @targetOperativeId, @weaponId,
             @attackerDice, @defenderDice, @targetInCover, @isObscured,
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
                ["@defenderDice"] = JsonSerializer.Serialize(action.DefenderDice),
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
        return action;
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
                   attacker_dice, defender_dice, target_in_cover, is_obscured,
                   normal_hits, critical_hits, blocks, normal_damage_dealt,
                   critical_damage_dealt, caused_incapacitation, self_damage_dealt,
                   stun_applied, narrative_note
            FROM actions WHERE activation_id = @id ORDER BY rowid
            """,
            r => new GameAction
            {
                Id = Guid.Parse(r.GetString(0)),
                ActivationId = Guid.Parse(r.GetString(1)),
                Type = Enum.Parse<ActionType>(r.GetString(2)),
                ApCost = r.GetInt32(3),
                TargetOperativeId = r.IsDBNull(4) ? null : Guid.Parse(r.GetString(4)),
                WeaponId = r.IsDBNull(5) ? null : Guid.Parse(r.GetString(5)),
                AttackerDice = r.IsDBNull(6) ? [] : JsonSerializer.Deserialize<int[]>(r.GetString(6)) ?? [],
                DefenderDice = r.IsDBNull(7) ? [] : JsonSerializer.Deserialize<int[]>(r.GetString(7)) ?? [],
                TargetInCover = r.IsDBNull(8) ? null : r.GetInt32(8) != 0,
                IsObscured = r.IsDBNull(9) ? null : r.GetInt32(9) != 0,
                NormalHits = r.GetInt32(10),
                CriticalHits = r.GetInt32(11),
                Blocks = r.GetInt32(12),
                NormalDamageDealt = r.GetInt32(13),
                CriticalDamageDealt = r.GetInt32(14),
                CausedIncapacitation = r.GetInt32(15) != 0,
                SelfDamageDealt = r.GetInt32(16),
                StunApplied = r.GetInt32(17) != 0,
                NarrativeNote = r.IsDBNull(18) ? null : r.GetString(18)
            },
            new() { ["@id"] = activationId.ToString() });
    }
}
