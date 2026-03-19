using KillTeam.DataSlate.Domain.Events;
using KillTeam.DataSlate.Domain.Models;

namespace KillTeam.DataSlate.Domain.Engine.WeaponRules.Handlers;

public sealed class HotRuleHandler : IShootWeaponRuleHandler
{
    public async Task ApplyEffectsAsync(Weapon weapon, WeaponEffectContext context)
    {
        if (weapon.Rules.All(r => r.Kind != WeaponRuleKind.Hot))
        {
            return;
        }

        if (context.ResolutionResult.SelfDamageDealt <= 0)
        {
            return;
        }

        var selfDamage = context.ResolutionResult.SelfDamageDealt;
        var newWounds = Math.Max(0, context.AttackerState.CurrentWounds - selfDamage);

        context.AttackerState.CurrentWounds = newWounds;
        context.SelfDamageApplied = selfDamage;

        await (context.EventStream?.EmitAsync((gameSessionId, sequenceNumber, timestamp) =>
            new SelfDamageDealtEvent(
                gameSessionId,
                sequenceNumber,
                timestamp,
                context.Attacker.TeamId,
                context.Attacker.Name,
                selfDamage,
                newWounds)) ?? ValueTask.CompletedTask);

        if (newWounds <= 0 && !context.AttackerState.IsIncapacitated)
        {
            context.AttackerState.IsIncapacitated = true;
            context.AttackerBecameIncapacitated = true;

            await (context.EventStream?.EmitAsync((gameSessionId, sequenceNumber, timestamp) =>
                new IncapacitationEvent(
                    gameSessionId,
                    sequenceNumber,
                    timestamp,
                    context.Attacker.TeamId,
                    context.Attacker.Name,
                    "SelfDamage")) ?? ValueTask.CompletedTask);
        }
    }

    public Task ApplyAfterBlockingAsync(Weapon weapon, ShootAfterBlockingContext context)
    {
        if (weapon.Rules.All(r => r.Kind != WeaponRuleKind.Hot))
        {
            return Task.CompletedTask;
        }

        var roll = Random.Shared.Next(1, 7);

        if (roll < context.HitThreshold)
        {
            context.SelfDamage = 2 * roll;
        }

        return Task.CompletedTask;
    }
}
