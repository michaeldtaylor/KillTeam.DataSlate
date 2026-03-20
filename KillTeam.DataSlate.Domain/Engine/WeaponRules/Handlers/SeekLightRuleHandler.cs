using KillTeam.DataSlate.Domain.Engine.WeaponRules.Context;
using KillTeam.DataSlate.Domain.Events;
using KillTeam.DataSlate.Domain.Models;

namespace KillTeam.DataSlate.Domain.Engine.WeaponRules.Handlers;

public sealed class SeekLightRuleHandler : IShootWeaponRuleHandler
{
    public Task ApplyBeforeCoverPromptAsync(Weapon weapon, WeaponCoverContext context)
    {
        if (weapon.Rules.Any(r => r.Kind == WeaponRuleKind.SeekLight))
        {
            context.LightCoverBlocked = true;
        }

        return Task.CompletedTask;
    }

    public async Task ApplyAfterCoverPromptAsync(Weapon weapon, WeaponCoverContext context)
    {
        if (weapon.Rules.All(r => r.Kind != WeaponRuleKind.SeekLight))
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
