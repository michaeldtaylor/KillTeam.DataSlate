using KillTeam.DataSlate.Domain.Engine.WeaponRules.Context;
using KillTeam.DataSlate.Domain.Engine.WeaponRules.Visitors;
using KillTeam.DataSlate.Domain.Models;

namespace KillTeam.DataSlate.Domain.Engine.WeaponRules;

public sealed class FightWeaponRulePipeline
{
    private readonly IReadOnlyList<IFightWeaponRuleVisitor> _handlers =
    [
        new BrutalRuleVisitor(),
        new ShockRuleVisitor(),
    ];

    public async Task SetupAsync(Weapon weapon, FightSetupContext context)
    {
        foreach (var handler in _handlers)
        {
            await handler.SetupAsync(weapon, context);
        }
    }
}
