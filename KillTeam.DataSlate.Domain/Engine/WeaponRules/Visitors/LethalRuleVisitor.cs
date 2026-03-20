using KillTeam.DataSlate.Domain.Engine.WeaponRules.Context;
using KillTeam.DataSlate.Domain.Models;

namespace KillTeam.DataSlate.Domain.Engine.WeaponRules.Visitors;

public sealed class LethalRuleVisitor : IShootWeaponRuleVisitor
{
    public Task ApplyBeforeAttackClassificationAsync(Weapon weapon, AttackClassificationContext context)
    {
        var rule = weapon.GetRule(WeaponRuleKind.Lethal);

        if (rule?.Param is null)
        {
            return Task.CompletedTask;
        }

        context.CritThreshold = Math.Min(context.CritThreshold, rule.Param.Value);

        return Task.CompletedTask;
    }
}
