using KillTeam.DataSlate.Domain.Events;
using KillTeam.DataSlate.Domain.Models;

namespace KillTeam.DataSlate.Domain.Engine.WeaponRules.Handlers;

public sealed class ShockRuleHandler : IFightWeaponRuleHandler
{
    public async Task ApplyPreResolutionAsync(
        Weapon weapon,
        FightPreResolutionContext context)
    {
        if (weapon.Rules.All(r => r.Kind != WeaponRuleKind.Shock))
        {
            return;
        }

        if (context.AttackerPool.Remaining.All(d => d.Result != DieResult.Crit))
        {
            return;
        }

        var lowestTargetSuccess = context.TargetPool.Remaining
            .OrderBy(d => d.RolledValue)
            .FirstOrDefault(d => d.Result != DieResult.Miss);

        if (lowestTargetSuccess is null)
        {
            return;
        }

        context.TargetPool = context.TargetPool with
        {
            Remaining = context.TargetPool.Remaining.Where(d => d.Id != lowestTargetSuccess.Id).ToList(),
        };

        await (context.EventStream?.EmitAsync((gameSessionId, sequenceNumber, timestamp) =>
            new ShockAppliedEvent(
                gameSessionId,
                sequenceNumber,
                timestamp,
                context.Attacker.TeamId,
                context.Target.Name,
                lowestTargetSuccess.RolledValue)) ?? ValueTask.CompletedTask);
    }
}
