using KillTeam.DataSlate.Domain.Engine.Input;
using KillTeam.DataSlate.Domain.Engine.WeaponRules;
using KillTeam.DataSlate.Domain.Engine.WeaponRules.Context;
using KillTeam.DataSlate.Domain.Events;
using KillTeam.DataSlate.Domain.Models;
using KillTeam.DataSlate.Domain.Repositories;

namespace KillTeam.DataSlate.Domain.Engine;

public class AoEEngine(
    IAoEInputProvider inputProvider,
    ShootWeaponRulePipeline shootWeaponRulePipeline,
    RerollEngine rerollEngine,
    IActionRepository actionRepository)
{
    public async Task<AoEResult> RunAsync(
        GameContext context,
        Activation activation,
        OperativeContext attacker,
        OperativeContext target,
        Weapon weapon)
    {
        var isAttackerTeam1 = attacker.Operative.TeamId == context.Game.Participant1.Team.Id;

        var aoeCandidates = ActionHelpers.GetAoECandidates(target, context.Operatives);

        var additionalTargets = new List<OperativeContext>();

        if (aoeCandidates.Length > 0)
        {
            additionalTargets = await inputProvider.SelectAdditionalTargetsAsync(
                aoeCandidates,
                weapon,
                attacker.Operative.Name,
                target.Operative.Name,
                attacker.Operative.TeamId);
        }

        var allTargets = new List<OperativeContext> { target }.Concat(additionalTargets).ToList();

        var friendlyCount = allTargets.Count(oc => oc.Operative.TeamId == attacker.Operative.TeamId);

        if (friendlyCount > 0)
        {
            if (!await inputProvider.ConfirmFriendlyFireAsync(friendlyCount))
            {
                return new AoEResult(false, 0);
            }
        }

        var attackerDice = await inputProvider.RollOrEnterDiceAsync(weapon.Atk, $"{attacker.Operative.Name} attack dice (Attack: {weapon.Atk})");

        attackerDice = await rerollEngine.ApplyAttackerRerollsAsync(
            attackerDice,
            weapon.Rules.ToList(),
            context.Game.Id,
            isAttackerTeam1,
            attacker.Operative.Name);

        var effectiveHit = weapon.Hit;

        var action = new GameAction
        {
            Id = Guid.NewGuid(),
            ActivationId = activation.Id,
            Type = ActionType.Shoot,
            ApCost = 1,
            TargetOperativeId = target.Operative.Id,
            WeaponId = weapon.Id,
            AttackerDice = attackerDice
        };

        var totalDamage = 0;

        var anyIncapacitation = false;
        var primaryActionPersisted = false;

        foreach (var oc in allTargets)
        {
            var coverChoice = await inputProvider.GetCoverStatusAsync(oc.Operative.Name);
            var inCover = coverChoice == "In cover";
            var isObscured = coverChoice == "Obscured";

            var targetDiceCount = oc.Operative.Defence + oc.State.DefenceDiceModifier;

            var defenderDice = targetDiceCount == 0
                ? []
                : await inputProvider.RollOrEnterDiceAsync(targetDiceCount, $"{oc.Operative.Name} target dice");

            var isTargetTeam1 = oc.Operative.TeamId == context.Game.Participant1.Team.Id;

            defenderDice = await rerollEngine.ApplyTargetRerollAsync(defenderDice, context.Game.Id, isTargetTeam1, oc.Operative.Name);

            var ShootResolutionContext = new ShootResolutionContext(
                AttackerDice: attackerDice,
                TargetDice: defenderDice,
                InCover: inCover,
                IsObscured: isObscured,
                HitThreshold: effectiveHit,
                SaveThreshold: oc.Operative.Save,
                NormalDmg: weapon.NormalDmg,
                CritDmg: weapon.CriticalDmg
            );

            var blastResult = await shootWeaponRulePipeline.ResolveShootAsync(weapon, ShootResolutionContext);

            var newWounds = Math.Max(0, oc.State.CurrentWounds - blastResult.TotalDamage);

            await (context.EventStream?.EmitAsync((gameSessionId, sequenceNumber, timestamp) =>
                new ShootResultDisplayedEvent(
                    gameSessionId,
                    sequenceNumber,
                    timestamp,
                    attacker.Operative.TeamId,
                    attacker.Operative.Name,
                    attacker.State.CurrentWounds,
                    attacker.Operative.Wounds,
                    oc.Operative.Name,
                    blastResult.UnblockedCrits,
                    blastResult.UnblockedNormals,
                    blastResult.TotalDamage,
                    newWounds,
                    oc.Operative.Wounds,
                    inCover,
                    isObscured)) ?? ValueTask.CompletedTask);

            var causedIncap = newWounds <= 0 && !oc.State.IsIncapacitated;

            oc.State.CurrentWounds = newWounds;

            await (context.EventStream?.EmitAsync((gameSessionId, sequenceNumber, timestamp) =>
                new OperativeWoundsChangedEvent(
                    gameSessionId,
                    sequenceNumber,
                    timestamp,
                    oc.Operative.TeamId,
                    oc.State.Id,
                    newWounds)) ?? ValueTask.CompletedTask);

            if (causedIncap)
            {
                oc.State.IsIncapacitated = true;
                oc.State.IsOnGuard = false;

                await (context.EventStream?.EmitAsync((gameSessionId, sequenceNumber, timestamp) =>
                    new OperativeIncapacitatedEvent(
                        gameSessionId,
                        sequenceNumber,
                        timestamp,
                        oc.Operative.TeamId,
                        oc.State.Id)) ?? ValueTask.CompletedTask);

                await (context.EventStream?.EmitAsync((gameSessionId, sequenceNumber, timestamp) =>
                    new OperativeGuardClearedEvent(
                        gameSessionId,
                        sequenceNumber,
                        timestamp,
                        oc.Operative.TeamId,
                        oc.State.Id)) ?? ValueTask.CompletedTask);

                anyIncapacitation = true;

                await (context.EventStream?.EmitAsync((gameSessionId, sequenceNumber, timestamp) =>
                    new IncapacitationEvent(
                        gameSessionId,
                        sequenceNumber,
                        timestamp,
                        attacker.Operative.TeamId,
                        oc.Operative.Name,
                        "Shoot")) ?? ValueTask.CompletedTask);
            }

            totalDamage += blastResult.TotalDamage;

            if (primaryActionPersisted)
            {
                // Secondary blast targets: damage and incapacitation are tracked via events only
                continue;
            }

            action.TargetDice = defenderDice;
            action.TargetInCover = inCover;
            action.IsObscured = isObscured;
            action.NormalHits = blastResult.UnblockedNormals;
            action.CriticalHits = blastResult.UnblockedCrits;
            action.NormalDamageDealt = blastResult.UnblockedNormals * weapon.NormalDmg;
            action.CriticalDamageDealt = blastResult.UnblockedCrits * weapon.CriticalDmg;
            action.CausedIncapacitation = causedIncap;

            await actionRepository.CreateAsync(action);

            primaryActionPersisted = true;
        }

        if (!primaryActionPersisted)
        {
            await actionRepository.CreateAsync(action);
        }

        var note = await inputProvider.GetNarrativeNoteAsync();

        if (!string.IsNullOrWhiteSpace(note))
        {
            await actionRepository.UpdateNarrativeAsync(action.Id, note);
        }

        return new AoEResult(anyIncapacitation, totalDamage);
    }
}
