using KillTeam.DataSlate.Domain.Models;

namespace KillTeam.DataSlate.Domain.Engine.WeaponRules.Handlers;

public sealed class LethalRuleHandler : IShootWeaponRuleHandler
{
    public Task ApplyBeforeAttackClassificationAsync(Weapon weapon, ShootBeforeClassificationContext context)
    {
        var rule = weapon.Rules.FirstOrDefault(r => r.Kind == WeaponRuleKind.Lethal);

        if (rule?.Param is null)
        {
            return Task.CompletedTask;
        }

        context.CritThreshold = Math.Min(context.CritThreshold, rule.Param.Value);

        return Task.CompletedTask;
    }
}
