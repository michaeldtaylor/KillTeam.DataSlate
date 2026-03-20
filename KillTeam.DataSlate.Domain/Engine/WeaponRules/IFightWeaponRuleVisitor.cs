using KillTeam.DataSlate.Domain.Engine.WeaponRules.Context;
using KillTeam.DataSlate.Domain.Models;

namespace KillTeam.DataSlate.Domain.Engine.WeaponRules;

public interface IFightWeaponRuleVisitor
{
    Task ApplyPreResolutionAsync(
        Weapon weapon,
        FightSetupContext context) => Task.CompletedTask;
}
