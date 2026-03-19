using KillTeam.DataSlate.Domain.Models;

namespace KillTeam.DataSlate.Domain.Engine.WeaponRules.Handlers;

public sealed class RangeRuleHandler : IShootWeaponRuleHandler
{
    public bool IsAvailable(Weapon weapon, ShootWeaponAvailabilityContext context)
    {
        var rangeRule = weapon.Rules.FirstOrDefault(r => r.Kind == WeaponRuleKind.Range);

        return rangeRule is null || rangeRule.Param >= context.TargetDistance;
    }
}
