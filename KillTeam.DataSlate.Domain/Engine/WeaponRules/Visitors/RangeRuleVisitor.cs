using KillTeam.DataSlate.Domain.Engine.WeaponRules.Context;
using KillTeam.DataSlate.Domain.Models;

namespace KillTeam.DataSlate.Domain.Engine.WeaponRules.Visitors;

public sealed class RangeRuleVisitor : IShootWeaponRuleVisitor
{
    public bool IsAvailable(Weapon weapon, AvailabilityContext context)
    {
        var rangeRule = weapon.Rules.FirstOrDefault(r => r.Kind == WeaponRuleKind.Range);

        return rangeRule is null || rangeRule.Param >= context.TargetDistance;
    }
}
