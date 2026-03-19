using KillTeam.DataSlate.Domain.Models;

namespace KillTeam.DataSlate.Domain.Engine.WeaponRules.Handlers;

public sealed class RendingRuleHandler : IShootWeaponRuleHandler
{
    public Task ApplyAfterAttackClassificationAsync(Weapon weapon, ShootAttackClassifiedContext context)
    {
        if (weapon.Rules.All(r => r.Kind != WeaponRuleKind.Rending))
        {
            return Task.CompletedTask;
        }

        if (context.CritHits < 1 || context.NormalHits < 1)
        {
            return Task.CompletedTask;
        }

        context.NormalHits--;
        context.CritHits++;

        return Task.CompletedTask;
    }
}
