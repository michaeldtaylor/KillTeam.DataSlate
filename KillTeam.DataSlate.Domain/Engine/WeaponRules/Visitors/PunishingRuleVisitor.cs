using KillTeam.DataSlate.Domain.Engine.WeaponRules.Context;
using KillTeam.DataSlate.Domain.Models;

namespace KillTeam.DataSlate.Domain.Engine.WeaponRules.Visitors;

public sealed class PunishingRuleVisitor : IShootWeaponRuleVisitor
{
    public Task ApplyAfterAttackClassificationAsync(Weapon weapon, ClassifiedAttackContext context)
    {
        if (!weapon.HasRule(WeaponRuleKind.Punishing))
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
