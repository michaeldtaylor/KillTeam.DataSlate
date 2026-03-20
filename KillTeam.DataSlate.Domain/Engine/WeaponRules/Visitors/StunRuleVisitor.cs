using KillTeam.DataSlate.Domain.Engine.WeaponRules.Context;
using KillTeam.DataSlate.Domain.Models;

namespace KillTeam.DataSlate.Domain.Engine.WeaponRules.Visitors;

public sealed class StunRuleVisitor : IShootWeaponRuleVisitor
{
    public Task ApplyAfterBlockingAsync(Weapon weapon, BlockingContext context)
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
