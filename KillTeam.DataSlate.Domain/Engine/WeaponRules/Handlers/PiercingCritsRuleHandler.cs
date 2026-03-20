using KillTeam.DataSlate.Domain.Engine.WeaponRules.Context;
using KillTeam.DataSlate.Domain.Models;

namespace KillTeam.DataSlate.Domain.Engine.WeaponRules.Handlers;

public sealed class PiercingCritsRuleHandler : IShootWeaponRuleHandler
{
    public Task ApplyAfterDefenceClassificationAsync(Weapon weapon, ShootDefenceClassifiedContext context)
    {
        var rule = weapon.Rules.FirstOrDefault(r => r.Kind == WeaponRuleKind.PiercingCrits);

        if (rule?.Param is not > 0 || context.RawCrits < 1)
        {
            return Task.CompletedTask;
        }

        var removeCount = rule.Param.Value;
        var fromCrits = Math.Min(removeCount, context.CritSaves);

        context.CritSaves -= fromCrits;
        removeCount -= fromCrits;
        context.NormalSaves = Math.Max(0, context.NormalSaves - removeCount);

        return Task.CompletedTask;
    }
}
