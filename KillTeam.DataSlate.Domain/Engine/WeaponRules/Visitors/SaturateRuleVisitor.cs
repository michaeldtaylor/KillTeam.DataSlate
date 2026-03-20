using KillTeam.DataSlate.Domain.Engine.WeaponRules.Context;
using KillTeam.DataSlate.Domain.Events;
using KillTeam.DataSlate.Domain.Models;

namespace KillTeam.DataSlate.Domain.Engine.WeaponRules.Visitors;

public sealed class SaturateRuleVisitor : IShootWeaponRuleVisitor
{
    public async Task ApplyAfterCoverPromptAsync(Weapon weapon, CoverContext context)
    {
        if (weapon.Rules.All(r => r.Kind != WeaponRuleKind.Saturate))
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
                $"Saturate: {context.Target.Name} cannot retain cover saves.")) ?? ValueTask.CompletedTask);

        context.InCover = false;
    }
}
