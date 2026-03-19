using KillTeam.DataSlate.Domain.Models;

namespace KillTeam.DataSlate.Domain.Engine.WeaponRules.Handlers;

public sealed class StunRuleHandler : IShootWeaponRuleHandler
{
    public Task ApplyAfterBlockingAsync(Weapon weapon, ShootAfterBlockingContext context)
    {
        if (weapon.Rules.All(r => r.Kind != WeaponRuleKind.Stun))
        {
            return Task.CompletedTask;
        }

        if (context.UnblockedCrits >= 1)
        {
            context.StunApplied = true;
        }

        return Task.CompletedTask;
    }
}
