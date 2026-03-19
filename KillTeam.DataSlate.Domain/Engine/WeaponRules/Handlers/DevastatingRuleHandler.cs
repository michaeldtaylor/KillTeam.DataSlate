using KillTeam.DataSlate.Domain.Models;

namespace KillTeam.DataSlate.Domain.Engine.WeaponRules.Handlers;

public sealed class DevastatingRuleHandler : IShootWeaponRuleHandler
{
    public Task ApplyAfterBlockingAsync(Weapon weapon, ShootAfterBlockingContext context)
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
