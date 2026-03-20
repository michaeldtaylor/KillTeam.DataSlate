using KillTeam.DataSlate.Domain.Engine.WeaponRules.Context;
using KillTeam.DataSlate.Domain.Models;

namespace KillTeam.DataSlate.Domain.Engine.WeaponRules.Visitors;

public sealed class SevereRuleVisitor : IShootWeaponRuleVisitor
{
    public Task ApplyAfterAttackClassificationAsync(Weapon weapon, ClassifiedAttackContext context)
    {
        if (weapon.Rules.All(r => r.Kind != WeaponRuleKind.Severe))
        {
            return Task.CompletedTask;
        }

        if (context.CritHits < 1)
        {
            return Task.CompletedTask;
        }

        context.NormalHits /= 2;

        return Task.CompletedTask;
    }
}
