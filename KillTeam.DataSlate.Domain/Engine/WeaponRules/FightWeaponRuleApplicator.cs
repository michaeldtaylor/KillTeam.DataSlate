using KillTeam.DataSlate.Domain.Engine.WeaponRules.Handlers;
using KillTeam.DataSlate.Domain.Models;

namespace KillTeam.DataSlate.Domain.Engine.WeaponRules;

public sealed class FightWeaponRuleApplicator
{
    private readonly IReadOnlyList<IFightWeaponRuleHandler> _handlers =
    [
        new BrutalRuleHandler(),
        new ShockRuleHandler(),
    ];

    public async Task ApplyPreResolutionAsync(Weapon attackerWeapon, FightPreResolutionContext context)
    {
        foreach (var handler in _handlers)
        {
            await handler.ApplyPreResolutionAsync(attackerWeapon, context);
        }
    }
}
