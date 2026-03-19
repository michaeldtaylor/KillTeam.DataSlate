using KillTeam.DataSlate.Domain.Models;

namespace KillTeam.DataSlate.Domain.Engine.WeaponRules.Handlers;

public sealed class BrutalRuleHandler : IFightWeaponRuleHandler
{
    public Task ApplyPreResolutionAsync(Weapon weapon, FightPreResolutionContext context)
    {
        if (weapon.Rules.Any(r => r.Kind == WeaponRuleKind.Brutal))
        {
            context.IsBrutal = true;
        }

        return Task.CompletedTask;
    }
}
