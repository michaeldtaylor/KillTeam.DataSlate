using KillTeam.DataSlate.Domain.Engine.WeaponRules.Context;
using KillTeam.DataSlate.Domain.Models;

namespace KillTeam.DataSlate.Domain.Engine.WeaponRules;

public interface IFightWeaponRuleHandler
{
    Task ApplyPreResolutionAsync(
        Weapon weapon,
        FightPreResolutionContext context) => Task.CompletedTask;
}
