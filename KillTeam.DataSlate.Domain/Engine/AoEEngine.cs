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
        Game game,
        Activation activation,
        Operative attacker,
        GameOperativeState attackerState,
        Operative target,
        GameOperativeState targetState,
        Weapon weapon,
        IReadOnlyList<GameOperativeState> allOperativeStates,
        IReadOnlyDictionary<Guid, Operative> allOperatives,
        GameEventStream? eventStream = null)
    {
        var isAttackerTeam1 = attacker.TeamId == game.Participant1.TeamId;

        var aoeCandidateStates = ActionHelpers.GetAoECandidateStates(target, allOperativeStates, allOperatives);

        var additionalTargetStates = new List<GameOperativeState>();

        if (aoeCandidateStates.Length > 0)
        {
            additionalTargetStates = await inputProvider.SelectAdditionalTargetsAsync(
                aoeCandidateStates,
                allOperatives,
                weapon,
                attacker.Name,
                target.Name,
                attacker.TeamId);
        }

        var allTargetStates = new List<GameOperativeState> { targetState }.Concat(additionalTargetStates).ToList();

        var friendlyCount = allTargetStates.Count(s =>
            allOperatives.TryGetValue(s.OperativeId, out var operative) && operative.TeamId == attacker.TeamId);

        if (friendlyCount > 0)
        {
            if (!await inputProvider.ConfirmFriendlyFireAsync(friendlyCount))
            {
                return new AoEResult(false, 0);
            }
        }

        var attackerDice = await inputProvider.RollOrEnterDiceAsync(weapon.Atk, $"{attacker.Name} attack dice (Attack: {weapon.Atk})");

        attackerDice = await rerollEngine.ApplyAttackerRerollsAsync(
            attackerDice,
            weapon.Rules.ToList(),
            game.Id,
            isAttackerTeam1,
            attacker.Name);

        var effectiveHit = weapon.Hit;

        var action = new GameAction
        {
            Id = Guid.NewGuid(),
            ActivationId = activation.Id,
            Type = ActionType.Shoot,
            ApCost = 1,
            TargetOperativeId = target.Id,
            WeaponId = weapon.Id,
            AttackerDice = attackerDice
        };

        var totalDamage = 0;

        var anyIncapacitation = false;
        var primaryActionPersisted = false;

        foreach (var targetOperativeState in allTargetStates)
        {
            if (!allOperatives.TryGetValue(targetOperativeState.OperativeId, out var targetOperative))
            {
                continue;
            }

            var coverChoice = await inputProvider.GetCoverStatusAsync(targetOperative.Name);
            var inCover = coverChoice == "In cover";
            var isObscured = coverChoice == "Obscured";

            var targetDiceCount = targetOperative.Defence + targetOperativeState.DefenceDiceModifier;

            var defenderDice = targetDiceCount == 0
                ? []
                : await inputProvider.RollOrEnterDiceAsync(targetDiceCount, $"{targetOperative.Name} target dice");

            var isTargetTeam1 = targetOperative.TeamId == game.Participant1.TeamId;

            defenderDice = await rerollEngine.ApplyTargetRerollAsync(defenderDice, game.Id, isTargetTeam1, targetOperative.Name);

            var context = new ShootContext(
                AttackerDice: attackerDice,
                TargetDice: defenderDice,
                InCover: inCover,
                IsObscured: isObscured,
                HitThreshold: effectiveHit,
                SaveThreshold: targetOperative.Save,
                NormalDmg: weapon.NormalDmg,
                CritDmg: weapon.CriticalDmg
            );

            var blastResult = await shootWeaponRulePipeline.ResolveShootAsync(weapon, context);

            var newWounds = Math.Max(0, targetOperativeState.CurrentWounds - blastResult.TotalDamage);

            await (eventStream?.EmitAsync((gameSessionId, sequenceNumber, timestamp) =>
                new ShootResultDisplayedEvent(
                    gameSessionId,
                    sequenceNumber,
                    timestamp,
                    attacker.TeamId,
                    attacker.Name,
                    attackerState.CurrentWounds,
                    attacker.Wounds,
                    targetOperative.Name,
                    blastResult.UnblockedCrits,
                    blastResult.UnblockedNormals,
                    blastResult.TotalDamage,
                    newWounds,
                    targetOperative.Wounds,
                    inCover,
                    isObscured)) ?? ValueTask.CompletedTask);

            var causedIncap = newWounds <= 0 && !targetOperativeState.IsIncapacitated;

            targetOperativeState.CurrentWounds = newWounds;

            await (eventStream?.EmitAsync((gameSessionId, sequenceNumber, timestamp) =>
                new OperativeWoundsChangedEvent(
                    gameSessionId,
                    sequenceNumber,
                    timestamp,
                    targetOperative.TeamId,
                    targetOperativeState.Id,
                    newWounds)) ?? ValueTask.CompletedTask);

            if (causedIncap)
            {
                targetOperativeState.IsIncapacitated = true;
                targetOperativeState.IsOnGuard = false;

                await (eventStream?.EmitAsync((gameSessionId, sequenceNumber, timestamp) =>
                    new OperativeIncapacitatedEvent(
                        gameSessionId,
                        sequenceNumber,
                        timestamp,
                        targetOperative.TeamId,
                        targetOperativeState.Id)) ?? ValueTask.CompletedTask);

                await (eventStream?.EmitAsync((gameSessionId, sequenceNumber, timestamp) =>
                    new OperativeGuardClearedEvent(
                        gameSessionId,
                        sequenceNumber,
                        timestamp,
                        targetOperative.TeamId,
                        targetOperativeState.Id)) ?? ValueTask.CompletedTask);

                anyIncapacitation = true;

                await (eventStream?.EmitAsync((gameSessionId, sequenceNumber, timestamp) =>
                    new IncapacitationEvent(
                        gameSessionId,
                        sequenceNumber,
                        timestamp,
                        attacker.TeamId,
                        targetOperative.Name,
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
