using KillTeam.DataSlate.Domain.Models;

namespace KillTeam.DataSlate.Domain.Engine.WeaponRules;

public interface IShootWeaponRuleHandler
{
    bool IsAvailable(Weapon weapon, ShootWeaponAvailabilityContext context) => true;

    Task ApplyBeforeCoverPromptAsync(Weapon weapon, WeaponCoverContext context) => Task.CompletedTask;

    Task ApplyAfterCoverPromptAsync(Weapon weapon, WeaponCoverContext context) => Task.CompletedTask;

    Task ApplyEffectsAsync(Weapon weapon, WeaponEffectContext context) => Task.CompletedTask;

    Task ApplyBeforeAttackClassificationAsync(Weapon weapon, ShootBeforeClassificationContext context) => Task.CompletedTask;

    Task ApplyAfterAttackClassificationAsync(Weapon weapon, ShootAttackClassifiedContext context) => Task.CompletedTask;

    Task ApplyBeforeDefenceClassificationAsync(Weapon weapon, ShootBeforeDefenceContext context) => Task.CompletedTask;

    Task ApplyAfterDefenceClassificationAsync(Weapon weapon, ShootDefenceClassifiedContext context) => Task.CompletedTask;

    Task ApplyAfterBlockingAsync(Weapon weapon, ShootAfterBlockingContext context) => Task.CompletedTask;
}
