using KillTeam.DataSlate.Domain.Engine.WeaponRules.Context;
using KillTeam.DataSlate.Domain.Models;

namespace KillTeam.DataSlate.Domain.Engine.WeaponRules.Handlers;

public sealed class PunishingRuleHandler : IShootWeaponRuleHandler
{
    public Task ApplyAfterAttackClassificationAsync(Weapon weapon, ShootAttackClassifiedContext context)
    {
        if (weapon.Rules.All(r => r.Kind != WeaponRuleKind.Punishing))
        {
            return Task.CompletedTask;
        }

        if (context.CritHits < 1)
        {
            return Task.CompletedTask;
        }

        context.CritHits += context.NormalHits;
        context.NormalHits = 0;

        return Task.CompletedTask;
    }
}
