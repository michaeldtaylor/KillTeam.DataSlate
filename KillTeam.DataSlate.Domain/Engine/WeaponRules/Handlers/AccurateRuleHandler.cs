using KillTeam.DataSlate.Domain.Models;

namespace KillTeam.DataSlate.Domain.Engine.WeaponRules.Handlers;

public sealed class AccurateRuleHandler : IShootWeaponRuleHandler
{
    public Task ApplyBeforeAttackClassificationAsync(Weapon weapon, ShootBeforeClassificationContext context)
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
