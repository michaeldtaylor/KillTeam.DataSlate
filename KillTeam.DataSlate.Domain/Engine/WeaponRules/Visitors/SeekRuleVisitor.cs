using KillTeam.DataSlate.Domain.Engine.WeaponRules.Context;
using KillTeam.DataSlate.Domain.Events;
using KillTeam.DataSlate.Domain.Models;

namespace KillTeam.DataSlate.Domain.Engine.WeaponRules.Visitors;

public sealed class SeekRuleVisitor : IShootWeaponRuleVisitor
{
    public async Task ApplyBeforeCoverPromptAsync(Weapon weapon, CoverContext context)
    {
        if (!weapon.HasRule(WeaponRuleKind.Seek))
        {
            return;
        }

        context.CoverPromptSuppressed = true;
        context.InCover = false;
        context.IsObscured = false;

        await (context.EventStream?.EmitAsync((gameSessionId, sequenceNumber, timestamp) =>
            new CombatWarningEvent(
                gameSessionId,
                sequenceNumber,
                timestamp,
                context.Attacker.TeamId,
                CombatWarningKind.NoWeaponsAvailable,
                $"Seek: {context.Target.Name} cannot use terrain for cover.")) ?? ValueTask.CompletedTask);
    }
}
