using KillTeam.DataSlate.Domain.Engine.WeaponRules.Context;
using KillTeam.DataSlate.Domain.Models;

namespace KillTeam.DataSlate.Domain.Engine.WeaponRules.Visitors;

public sealed class DevastatingRuleVisitor : IShootWeaponRuleVisitor
{
    public Task ApplyAfterBlockingAsync(Weapon weapon, BlockingContext context)
    {
        var rule = weapon.Rules.FirstOrDefault(r => r.Kind == WeaponRuleKind.Devastating);

        if (rule?.Param is null)
        {
            return Task.CompletedTask;
        }

        context.EffectiveCritDmg = rule.Param.Value;

        return Task.CompletedTask;
    }
}
