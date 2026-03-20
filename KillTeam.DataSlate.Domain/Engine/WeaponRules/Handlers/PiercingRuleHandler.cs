using KillTeam.DataSlate.Domain.Engine.WeaponRules.Context;
using KillTeam.DataSlate.Domain.Models;

namespace KillTeam.DataSlate.Domain.Engine.WeaponRules.Handlers;

public sealed class PiercingRuleHandler : IShootWeaponRuleHandler
{
    public Task ApplyBeforeDefenceClassificationAsync(Weapon weapon, ShootBeforeDefenceContext context)
    {
        var rule = weapon.Rules.FirstOrDefault(r => r.Kind == WeaponRuleKind.Piercing);

        if (rule?.Param is not > 0)
        {
            return Task.CompletedTask;
        }

        var removeCount = Math.Min(rule.Param.Value, context.DefenceDice.Count);

        context.DefenceDice.RemoveRange(0, removeCount);

        return Task.CompletedTask;
    }
}
