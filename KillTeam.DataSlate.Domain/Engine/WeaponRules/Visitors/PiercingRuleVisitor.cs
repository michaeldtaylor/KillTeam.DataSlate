using KillTeam.DataSlate.Domain.Engine.WeaponRules.Context;
using KillTeam.DataSlate.Domain.Models;

namespace KillTeam.DataSlate.Domain.Engine.WeaponRules.Visitors;

public sealed class PiercingRuleVisitor : IShootWeaponRuleVisitor
{
    public Task ApplyBeforeDefenceClassificationAsync(Weapon weapon, DefenceClassificationContext context)
    {
        var rule = weapon.GetRule(WeaponRuleKind.Piercing);

        if (rule?.Param is not > 0)
        {
            return Task.CompletedTask;
        }

        var removeCount = Math.Min(rule.Param.Value, context.DefenceDice.Count);

        context.DefenceDice.RemoveRange(0, removeCount);

        return Task.CompletedTask;
    }
}
