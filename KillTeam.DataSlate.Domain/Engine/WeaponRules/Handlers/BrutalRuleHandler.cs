using KillTeam.DataSlate.Domain.Engine.WeaponRules.Context;
using KillTeam.DataSlate.Domain.Models;

namespace KillTeam.DataSlate.Domain.Engine.WeaponRules.Handlers;

public sealed class BrutalRuleHandler : IFightWeaponRuleHandler
{
    public Task ApplyPreResolutionAsync(Weapon weapon, FightPreResolutionContext context)
    {
        if (weapon.Rules.Any(r => r.Kind == WeaponRuleKind.Brutal))
        {
            context.BlockRestrictedToCrits = true;
        }

        return Task.CompletedTask;
    }
}
