using KillTeam.DataSlate.Domain.Engine.WeaponRules.Context;
using KillTeam.DataSlate.Domain.Models;

namespace KillTeam.DataSlate.Domain.Engine.WeaponRules.Visitors;

public sealed class SilentRuleVisitor : IShootWeaponRuleVisitor
{
    public bool IsAvailable(Weapon weapon, AvailabilityContext context)
    {
        return !context.IsOnConceal || weapon.Rules.Any(r => r.Kind == WeaponRuleKind.Silent);
    }
}
