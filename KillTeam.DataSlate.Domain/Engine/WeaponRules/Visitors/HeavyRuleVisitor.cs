using KillTeam.DataSlate.Domain.Engine.WeaponRules.Context;
using KillTeam.DataSlate.Domain.Models;

namespace KillTeam.DataSlate.Domain.Engine.WeaponRules.Visitors;

public sealed class HeavyRuleVisitor : IShootWeaponRuleVisitor
{
    public bool IsAvailable(Weapon weapon, AvailabilityContext context)
    {
        return weapon.Rules.All(r => r.Kind != WeaponRuleKind.Heavy) || !context.HasMovedNonDash;
    }
}
