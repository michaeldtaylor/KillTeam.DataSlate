using KillTeam.DataSlate.Domain.Engine.WeaponRules.Context;
using KillTeam.DataSlate.Domain.Models;

namespace KillTeam.DataSlate.Domain.Engine.WeaponRules.Visitors;

public sealed class AccurateRuleVisitor : IShootWeaponRuleVisitor
{
    public Task ApplyBeforeAttackClassificationAsync(Weapon weapon, AttackClassificationContext context)
    {
        var rule = weapon.Rules.FirstOrDefault(r => r.Kind == WeaponRuleKind.Accurate);

        if (rule is null)
        {
            return Task.CompletedTask;
        }

        context.BonusNormals += rule.Param ?? 0;

        return Task.CompletedTask;
    }
}
