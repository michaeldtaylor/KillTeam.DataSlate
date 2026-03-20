using KillTeam.DataSlate.Domain.Engine.WeaponRules.Context;
using KillTeam.DataSlate.Domain.Models;

namespace KillTeam.DataSlate.Domain.Engine.WeaponRules;

public interface IShootWeaponRuleVisitor
{
    bool IsAvailable(Weapon weapon, AvailabilityContext context) => true;

    Task ApplyBeforeCoverPromptAsync(Weapon weapon, CoverContext context) => Task.CompletedTask;

    Task ApplyAfterCoverPromptAsync(Weapon weapon, CoverContext context) => Task.CompletedTask;

    Task ApplyEffectsAsync(Weapon weapon, EffectsContext context) => Task.CompletedTask;

    Task ApplyBeforeAttackClassificationAsync(Weapon weapon, AttackClassificationContext context) => Task.CompletedTask;

    Task ApplyAfterAttackClassificationAsync(Weapon weapon, ClassifiedAttackContext context) => Task.CompletedTask;

    Task ApplyBeforeDefenceClassificationAsync(Weapon weapon, DefenceClassificationContext context) => Task.CompletedTask;

    Task ApplyAfterDefenceClassificationAsync(Weapon weapon, ClassifiedDefenceContext context) => Task.CompletedTask;

    Task ApplyAfterBlockingAsync(Weapon weapon, BlockingContext context) => Task.CompletedTask;
}
