using KillTeam.DataSlate.Domain.Engine.Input;
using KillTeam.DataSlate.Domain.Engine.WeaponRules;
using KillTeam.DataSlate.Domain.Engine.WeaponRules.Context;
using KillTeam.DataSlate.Domain.Events;
using KillTeam.DataSlate.Domain.Models;
using KillTeam.DataSlate.Domain.Repositories;

namespace KillTeam.DataSlate.Domain.Engine;

public class FightEngine(
    IFightInputProvider inputProvider,
    RerollEngine rerollEngine,
    IActionRepository actionRepository,
    FightWeaponRulePipeline fightWeaponRulePipeline)
{
    public async Task<FightResult> RunAsync(
        GameContext context,
        Activation activation,
        Operative attacker,
        GameOperativeState attackerState)
    {
        var isAttackerTeam1 = attacker.TeamId == context.Game.Participant1.Team.Id;
        var attackerTeamId = attacker.TeamId;

        var targetStates = ActionHelpers.GetTargetStates(attacker, context.OperativeStates, context.Operatives);

        if (targetStates.Length == 0)
        {
            await (context.EventStream?.EmitAsync((gameSessionId, sequenceNumber, timestamp) =>
                new CombatWarningEvent(
                    gameSessionId,
                    sequenceNumber,
                    timestamp,
                    attackerTeamId,
                    CombatWarningKind.NoValidTargets,
                    "No valid fight targets available.")) ?? ValueTask.CompletedTask);

            return new FightResult(false, false, 0, 0, null);
        }

        GameOperativeState targetState;

        if (targetStates.Length == 1)
        {
            targetState = targetStates[0];

            if (context.Operatives.TryGetValue(targetState.OperativeId, out var autoTarget))
            {
                await (context.EventStream?.EmitAsync((gameSessionId, sequenceNumber, timestamp) =>
                    new FightTargetSelectedEvent(
                        gameSessionId,
                        sequenceNumber,
                        timestamp,
                        attackerTeamId,
                        autoTarget.Name,
                        targetState.CurrentWounds,
                        autoTarget.Wounds,
                        true)) ?? ValueTask.CompletedTask);
            }
        }
        else
        {
            targetState = await inputProvider.SelectTargetAsync(targetStates, context.Operatives);
        }

        if (!context.Operatives.TryGetValue(targetState.OperativeId, out var target))
        {
            await (context.EventStream?.EmitAsync((gameSessionId, sequenceNumber, timestamp) =>
                new CombatWarningEvent(
                    gameSessionId,
                    sequenceNumber,
                    timestamp,
                    attackerTeamId,
                    CombatWarningKind.TargetNotFound,
                    "Target operative not found.")) ?? ValueTask.CompletedTask);

            return new FightResult(false, false, 0, 0, null);
        }

        var targetTeamId = target.TeamId;
        var isTargetTeam1 = target.TeamId == context.Game.Participant1.Team.Id;

        var attackerMeleeWeapons = attacker.Weapons.Where(w => w.Type == WeaponType.Melee).ToList();

        if (attackerMeleeWeapons.Count == 0)
        {
            await (context.EventStream?.EmitAsync((gameSessionId, sequenceNumber, timestamp) =>
                new CombatWarningEvent(
                    gameSessionId,
                    sequenceNumber,
                    timestamp,
                    attackerTeamId,
                    CombatWarningKind.NoWeaponsAvailable,
                    $"{attacker.Name} has no melee weapons!")) ?? ValueTask.CompletedTask);

            return new FightResult(false, false, 0, 0, targetState.OperativeId);
        }

        var attackerIsInjured = attackerState.CurrentWounds < attacker.Wounds / 2;

        Weapon attackerWeapon;

        if (attackerMeleeWeapons.Count == 1)
        {
            attackerWeapon = attackerMeleeWeapons[0];

            await (context.EventStream?.EmitAsync((gameSessionId, sequenceNumber, timestamp) =>
                new WeaponSelectedEvent(
                    gameSessionId,
                    sequenceNumber,
                    timestamp,
                    attackerTeamId,
                    attackerWeapon.Name,
                    attackerWeapon.Atk,
                    attackerWeapon.Hit,
                    attackerWeapon.NormalDmg,
                    attackerWeapon.CriticalDmg,
                    "Attacker",
                    true,
                    attackerIsInjured,
                    attackerIsInjured ? attackerWeapon.Hit + 1 : attackerWeapon.Hit)) ?? ValueTask.CompletedTask);
        }
        else
        {
            attackerWeapon = await inputProvider.SelectAttackerWeaponAsync(attackerMeleeWeapons, attackerIsInjured);
        }

        var attackerEffectiveHit = attackerIsInjured ? attackerWeapon.Hit + 1 : attackerWeapon.Hit;

        var targetMeleeWeapons = target.Weapons
            .Where(w => w.Type == WeaponType.Melee)
            .ToList();

        Weapon? targetWeapon = null;

        var targetEffectiveHit = 3;

        switch (targetMeleeWeapons.Count)
        {
            case 1:
            {
                targetWeapon = targetMeleeWeapons[0];

                var targetIsInjured = targetState.CurrentWounds < target.Wounds / 2;

                targetEffectiveHit = targetIsInjured ? targetWeapon.Hit + 1 : targetWeapon.Hit;

                await (context.EventStream?.EmitAsync((gameSessionId, sequenceNumber, timestamp) =>
                    new WeaponSelectedEvent(
                        gameSessionId,
                        sequenceNumber,
                        timestamp,
                        targetTeamId,
                        targetWeapon.Name,
                        targetWeapon.Atk,
                        targetWeapon.Hit,
                        targetWeapon.NormalDmg,
                        targetWeapon.CriticalDmg,
                        "Target",
                        true,
                        targetIsInjured,
                        targetEffectiveHit)) ?? ValueTask.CompletedTask);
                break;
            }
            case > 1:
            {
                targetWeapon = await inputProvider.SelectTargetWeaponAsync(targetMeleeWeapons);
                var targetIsInjured = targetState.CurrentWounds < target.Wounds / 2;

                targetEffectiveHit = targetIsInjured ? targetWeapon.Hit + 1 : targetWeapon.Hit;
                break;
            }
            default:
                await (context.EventStream?.EmitAsync((gameSessionId, sequenceNumber, timestamp) =>
                    new TargetNoMeleeWeaponsEvent(
                        gameSessionId,
                        sequenceNumber,
                        timestamp,
                        targetTeamId,
                        target.Name)) ?? ValueTask.CompletedTask);
                break;
        }

        var fightAssist = await inputProvider.GetFightAssistCountAsync();

        attackerEffectiveHit = Math.Max(2, attackerEffectiveHit - fightAssist);

        var attackerRolls = await inputProvider.RollOrEnterDiceAsync(attackerWeapon.Atk, $"{attacker.Name} attack dice (Attack: {attackerWeapon.Atk})", attacker.Name, "Attacker", "Fight", attackerTeamId, context.EventStream);

        attackerRolls = await rerollEngine.ApplyAttackerRerollsAsync(
            attackerRolls,
            attackerWeapon.Rules.ToList(),
            context.Game.Id,
            isAttackerTeam1,
            attacker.Name,
            attackerTeamId,
            context.EventStream);

        var targetAttackCount = targetWeapon?.Atk ?? 0;

        int[] targetRolls = [];

        if (targetAttackCount > 0)
        {
            targetRolls = await inputProvider.RollOrEnterDiceAsync(targetAttackCount, $"{target.Name} fight-back dice (Attack: {targetAttackCount})", target.Name, "Target", "Fight", targetTeamId, context.EventStream);
            targetRolls = await rerollEngine.ApplyTargetRerollAsync(targetRolls, context.Game.Id, isTargetTeam1, target.Name, targetTeamId, context.EventStream);
        }

        var attackerPool = FightResolution.CalculateDice(attackerRolls, attackerEffectiveHit);
        var targetPool = targetWeapon is not null
            ? FightResolution.CalculateDice(targetRolls, targetEffectiveHit)
            : new FightDicePool([]);

        var fightSetupContext = new FightSetupContext
        {
            Attacker = attacker,
            Target = target,
            AttackerPool = attackerPool,
            TargetPool = targetPool,
            EventStream = context.EventStream,
        };

        await fightWeaponRulePipeline.SetupAsync(attackerWeapon, fightSetupContext);

        attackerPool = fightSetupContext.AttackerPool;
        targetPool = fightSetupContext.TargetPool;

        var attackerCurrentWounds = attackerState.CurrentWounds;
        var targetCurrentWounds = targetState.CurrentWounds;

        var fightResolutionContext = new FightResolutionContext
        {
            Attacker = attacker,
            Target = target,
            AttackerWeapon = attackerWeapon,
            TargetWeapon = targetWeapon,
            AttackerPool = attackerPool,
            TargetPool = targetPool,
            BlockRestrictedToCrits = fightSetupContext.BlockRestrictedToCrits,
            AttackerCurrentWounds = attackerCurrentWounds,
            TargetCurrentWounds = targetCurrentWounds,
            InputProvider = inputProvider,
            EventStream = context.EventStream,
        };

        var loopResult = await fightWeaponRulePipeline.ResolveFightAsync(fightResolutionContext);

        var attackerCausedIncapacitation = loopResult.TargetCurrentWounds <= 0 && !targetState.IsIncapacitated;
        var targetCausedIncapacitation = loopResult.AttackerCurrentWounds <= 0 && !attackerState.IsIncapacitated;

        attackerState.CurrentWounds = loopResult.AttackerCurrentWounds;
        targetState.CurrentWounds = loopResult.TargetCurrentWounds;

        await (context.EventStream?.EmitAsync((gameSessionId, sequenceNumber, timestamp) =>
            new OperativeWoundsChangedEvent(
                gameSessionId,
                sequenceNumber,
                timestamp,
                attackerTeamId,
                attackerState.Id,
                loopResult.AttackerCurrentWounds)) ?? ValueTask.CompletedTask);

        await (context.EventStream?.EmitAsync((gameSessionId, sequenceNumber, timestamp) =>
            new OperativeWoundsChangedEvent(
                gameSessionId,
                sequenceNumber,
                timestamp,
                targetTeamId,
                targetState.Id,
                loopResult.TargetCurrentWounds)) ?? ValueTask.CompletedTask);

        if (attackerCausedIncapacitation)
        {
            targetState.IsIncapacitated = true;
            targetState.IsOnGuard = false;

            await (context.EventStream?.EmitAsync((gameSessionId, sequenceNumber, timestamp) =>
                new OperativeIncapacitatedEvent(
                    gameSessionId,
                    sequenceNumber,
                    timestamp,
                    targetTeamId,
                    targetState.Id)) ?? ValueTask.CompletedTask);

            await (context.EventStream?.EmitAsync((gameSessionId, sequenceNumber, timestamp) =>
                new OperativeGuardClearedEvent(
                    gameSessionId,
                    sequenceNumber,
                    timestamp,
                    targetTeamId,
                    targetState.Id)) ?? ValueTask.CompletedTask);

            await (context.EventStream?.EmitAsync((gameSessionId, sequenceNumber, timestamp) =>
                new IncapacitationEvent(
                    gameSessionId,
                    sequenceNumber,
                    timestamp,
                    attackerTeamId,
                    target.Name,
                    "Fight")) ?? ValueTask.CompletedTask);
        }
        if (targetCausedIncapacitation)
        {
            attackerState.IsIncapacitated = true;
            attackerState.IsOnGuard = false;

            await (context.EventStream?.EmitAsync((gameSessionId, sequenceNumber, timestamp) =>
                new OperativeIncapacitatedEvent(
                    gameSessionId,
                    sequenceNumber,
                    timestamp,
                    attackerTeamId,
                    attackerState.Id)) ?? ValueTask.CompletedTask);

            await (context.EventStream?.EmitAsync((gameSessionId, sequenceNumber, timestamp) =>
                new OperativeGuardClearedEvent(
                    gameSessionId,
                    sequenceNumber,
                    timestamp,
                    attackerTeamId,
                    attackerState.Id)) ?? ValueTask.CompletedTask);

            await (context.EventStream?.EmitAsync((gameSessionId, sequenceNumber, timestamp) =>
                new IncapacitationEvent(
                    gameSessionId,
                    sequenceNumber,
                    timestamp,
                    targetTeamId,
                    attacker.Name,
                    "Fight")) ?? ValueTask.CompletedTask);
        }

        var action = new GameAction
        {
            Id = Guid.NewGuid(),
            ActivationId = activation.Id,
            Type = ActionType.Fight,
            ApCost = 1,
            TargetOperativeId = targetState.OperativeId,
            WeaponId = attackerWeapon.Id,
            AttackerDice = attackerRolls,
            TargetDice = targetRolls,
            NormalDamageDealt = loopResult.AttackerDamageDealt,
            CriticalDamageDealt = 0,
            CausedIncapacitation = attackerCausedIncapacitation
        };

        await actionRepository.CreateAsync(action);

        await (context.EventStream?.EmitAsync((gameSessionId, sequenceNumber, timestamp) =>
            new FightResolvedEvent(
                gameSessionId,
                sequenceNumber,
                timestamp,
                attackerTeamId,
                attacker.Name,
                target.Name,
                loopResult.AttackerDamageDealt,
                loopResult.TargetDamageDealt,
                attackerCausedIncapacitation,
                targetCausedIncapacitation)) ?? ValueTask.CompletedTask);

        var note = await inputProvider.GetNarrativeNoteAsync();

        if (!string.IsNullOrWhiteSpace(note))
        {
            await actionRepository.UpdateNarrativeAsync(action.Id, note);
        }

        return new FightResult(
            attackerCausedIncapacitation,
            targetCausedIncapacitation,
            loopResult.AttackerDamageDealt,
            loopResult.TargetDamageDealt,
            targetState.OperativeId);
    }
}
