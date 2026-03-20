using KillTeam.DataSlate.Domain.Engine.WeaponRules.Context;
using KillTeam.DataSlate.Domain.Models;

namespace KillTeam.DataSlate.Domain.Engine.WeaponRules.Visitors;

public sealed class BrutalRuleVisitor : IFightWeaponRuleVisitor
{
    public Task SetupAsync(Weapon weapon, FightSetupContext context)
    {
        if (weapon.HasRule(WeaponRuleKind.Brutal))
        {
            context.BlockRestrictedToCrits = true;
        }

        return Task.CompletedTask;
    }
}
