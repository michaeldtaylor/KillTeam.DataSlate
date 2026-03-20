using KillTeam.DataSlate.Domain.Engine.WeaponRules.Context;
using KillTeam.DataSlate.Domain.Events;
using KillTeam.DataSlate.Domain.Models;

namespace KillTeam.DataSlate.Domain.Engine.WeaponRules.Visitors;

public sealed class SeekLightRuleVisitor : IShootWeaponRuleVisitor
{
    public Task ApplyBeforeCoverPromptAsync(Weapon weapon, CoverContext context)
    {
        if (weapon.HasRule(WeaponRuleKind.SeekLight))
        {
            context.LightCoverBlocked = true;
        }

        return Task.CompletedTask;
    }

    public async Task ApplyAfterCoverPromptAsync(Weapon weapon, CoverContext context)
    {
        if (!weapon.HasRule(WeaponRuleKind.SeekLight))
        {
            return;
        }

        if (!context.InCover)
        {
            return;
        }

        await (context.EventStream?.EmitAsync((gameSessionId, sequenceNumber, timestamp) =>
            new CombatWarningEvent(
                gameSessionId,
                sequenceNumber,
                timestamp,
                context.Attacker.TeamId,
                CombatWarningKind.NoWeaponsAvailable,
                $"Seek Light: {context.Target.Name} cannot use light terrain for cover.")) ?? ValueTask.CompletedTask);

        context.InCover = false;
    }
}
