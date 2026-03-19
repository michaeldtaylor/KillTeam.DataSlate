using KillTeam.DataSlate.Domain.Models;

namespace KillTeam.DataSlate.Domain.Engine.WeaponRules.Handlers;

public sealed class HeavyRuleHandler : IShootWeaponRuleHandler
{
    public bool IsAvailable(Weapon weapon, ShootWeaponAvailabilityContext context)
    {
        return weapon.Rules.All(r => r.Kind != WeaponRuleKind.Heavy) || !context.HasMovedNonDash;
    }
}
