using KillTeam.DataSlate.Domain.Engine.WeaponRules.Context;
using KillTeam.DataSlate.Domain.Models;

namespace KillTeam.DataSlate.Domain.Engine.WeaponRules.Handlers;

public sealed class SilentRuleHandler : IShootWeaponRuleHandler
{
    public bool IsAvailable(Weapon weapon, ShootWeaponAvailabilityContext context)
    {
        return !context.IsOnConceal || weapon.Rules.Any(r => r.Kind == WeaponRuleKind.Silent);
    }
}
