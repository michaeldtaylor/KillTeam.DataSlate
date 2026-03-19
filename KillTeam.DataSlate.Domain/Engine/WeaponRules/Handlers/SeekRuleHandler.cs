using KillTeam.DataSlate.Domain.Events;
using KillTeam.DataSlate.Domain.Models;

namespace KillTeam.DataSlate.Domain.Engine.WeaponRules.Handlers;

public sealed class SeekRuleHandler : IShootWeaponRuleHandler
{
    public async Task ApplyBeforeCoverPromptAsync(Weapon weapon, WeaponCoverContext context)
    {
        if (weapon.Rules.All(r => r.Kind != WeaponRuleKind.Seek))
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
