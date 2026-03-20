using KillTeam.DataSlate.Domain.Engine.WeaponRules.Context;
using KillTeam.DataSlate.Domain.Models;

namespace KillTeam.DataSlate.Domain.Engine.WeaponRules.Visitors;

public sealed class RangeRuleVisitor : IShootWeaponRuleVisitor
{
    public bool IsAvailable(Weapon weapon, AvailabilityContext context)
    {
        var rule = weapon.GetRule(WeaponRuleKind.Range);

        return rule is null || rule.Param >= context.TargetDistance;
    }
}
