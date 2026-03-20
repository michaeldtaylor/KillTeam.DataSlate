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
        Game game,
        Activation activation,
        Operative attacker,
        GameOperativeState attackerState,
        IReadOnlyList<GameOperativeState> allOperativeStates,
        IReadOnlyDictionary<Guid, Operative> allOperatives,
        GameEventStream? eventStream = null)
    {
        var isAttackerTeam1 = attacker.TeamId == game.Participant1.TeamId;
        var attackerTeamId = attacker.TeamId;

        var targetStates = ActionHelpers.GetTargetStates(attacker, allOperativeStates, allOperatives);

        if (targetStates.Length == 0)
        {
            await (eventStream?.EmitAsync((gameSessionId, sequenceNumber, timestamp) =>
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

            if (allOperatives.TryGetValue(targetState.OperativeId, out var autoTarget))
            {
                await (eventStream?.EmitAsync((gameSessionId, sequenceNumber, timestamp) =>
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
            targetState = await inputProvider.SelectTargetAsync(targetStates, allOperatives);
        }

        if (!allOperatives.TryGetValue(targetState.OperativeId, out var target))
        {
            await (eventStream?.EmitAsync((gameSessionId, sequenceNumber, timestamp) =>
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
        var isTargetTeam1 = target.TeamId == game.Participant1.TeamId;

        var attackerMeleeWeapons = attacker.Weapons.Where(w => w.Type == WeaponType.Melee).ToList();

        if (attackerMeleeWeapons.Count == 0)
        {
            await (eventStream?.EmitAsync((gameSessionId, sequenceNumber, timestamp) =>
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

            await (eventStream?.EmitAsync((gameSessionId, sequenceNumber, timestamp) =>
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

                await (eventStream?.EmitAsync((gameSessionId, sequenceNumber, timestamp) =>
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
                await (eventStream?.EmitAsync((gameSessionId, sequenceNumber, timestamp) =>
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

        var attackerRolls = await inputProvider.RollOrEnterDiceAsync(attackerWeapon.Atk, $"{attacker.Name} attack dice (Attack: {attackerWeapon.Atk})", attacker.Name, "Attacker", "Fight", attackerTeamId, eventStream);

        attackerRolls = await rerollEngine.ApplyAttackerRerollsAsync(
            attackerRolls,
            attackerWeapon.Rules.ToList(),
            game.Id,
            isAttackerTeam1,
            attacker.Name,
            attackerTeamId,
            eventStream);

        var targetAttackCount = targetWeapon?.Atk ?? 0;

        int[] targetRolls = [];

        if (targetAttackCount > 0)
        {
            targetRolls = await inputProvider.RollOrEnterDiceAsync(targetAttackCount, $"{target.Name} fight-back dice (Attack: {targetAttackCount})", target.Name, "Target", "Fight", targetTeamId, eventStream);
            targetRolls = await rerollEngine.ApplyTargetRerollAsync(targetRolls, game.Id, isTargetTeam1, target.Name, targetTeamId, eventStream);
        }

        var attackerPool = FightResolution.CalculateDice(attackerRolls, attackerEffectiveHit);
        var targetPool = targetWeapon is not null
            ? FightResolution.CalculateDice(targetRolls, targetEffectiveHit)
            : new FightDicePool([]);

        var preResolutionContext = new FightSetupContext
        {
            Attacker = attacker,
            Target = target,
            EventStream = eventStream,
            AttackerPool = attackerPool,
            TargetPool = targetPool,
        };

        await fightWeaponRulePipeline.SetupAsync(attackerWeapon, preResolutionContext);

        attackerPool = preResolutionContext.AttackerPool;
        targetPool = preResolutionContext.TargetPool;

        var attackerCurrentWounds = attackerState.CurrentWounds;
        var targetCurrentWounds = targetState.CurrentWounds;
        var totalAttackerDamageDealt = 0;
        var totalTargetDamageDealt = 0;
        var turnOrder = DieOwner.Attacker;

        while (attackerPool.Remaining.Count > 0 || targetPool.Remaining.Count > 0)
        {
            var fightTurn = FightTurn.Resolve(
                turnOrder,
                attackerPool,
                targetPool,
                attacker,
                target,
                attackerWeapon,
                targetWeapon,
                preResolutionContext.BlockRestrictedToCrits);

            var (activePool, opponentPool, currentTurn, activeOperative, opponentOperative, activeWeapon, restrictBlocksToCrits) = fightTurn;

            var attackerWoundsNow = attackerCurrentWounds;
            var targetWoundsNow = targetCurrentWounds;
            var attackerPoolNow = attackerPool.Remaining.Select(d => new FightDieSnapshot(d.Result, d.RolledValue)).ToList();
            var targetPoolNow = targetPool.Remaining.Select(d => new FightDieSnapshot(d.Result, d.RolledValue)).ToList();

            await (eventStream?.EmitAsync((gameSessionId, sequenceNumber, timestamp) =>
                new FightPoolsDisplayedEvent(
                    gameSessionId,
                    sequenceNumber,
                    timestamp,
                    attackerTeamId,
                    attacker.Name,
                    attackerWoundsNow,
                    attacker.Wounds,
                    attackerPoolNow,
                    target.Name,
                    targetWoundsNow,
                    target.Wounds,
                    targetPoolNow)) ?? ValueTask.CompletedTask);

            var actions = FightResolution.GetAvailableActions(activePool, opponentPool, restrictBlocksToCrits);

            var uniqueActions = actions
                .GroupBy(a => (a.Type, a.Die.Result, TargetResult: a.TargetDie?.Result))
                .Select(g => g.First())
                .ToList();

            var opponentHasCrits = opponentPool.Remaining.Any(d => d.Result == DieResult.Crit);

            if (opponentHasCrits)
            {
                uniqueActions = uniqueActions
                    .Where(a => a.Type != FightActionType.Block
                        || a.Die.Result != DieResult.Crit
                        || a.TargetDie?.Result != DieResult.Hit)
                    .ToList();
            }

            if (uniqueActions.Count == 0)
            {
                break;
            }

            var actionChoice = await inputProvider.SelectActionAsync(uniqueActions, activeOperative.Name);

            if (actionChoice.Type == FightActionType.Strike)
            {
                var damageDealt = FightResolution.ApplyStrike(actionChoice.Die, activeWeapon.NormalDmg, activeWeapon.CriticalDmg);

                await (eventStream?.EmitAsync((gameSessionId, sequenceNumber, timestamp) =>
                    new FightStrikeResolvedEvent(
                        gameSessionId,
                        sequenceNumber,
                        timestamp,
                        activeOperative.TeamId,
                        activeOperative.Name,
                        opponentOperative.Name,
                        actionChoice.Die.RolledValue,
                        actionChoice.Die.Result,
                        damageDealt)) ?? ValueTask.CompletedTask);

                if (currentTurn == DieOwner.Attacker)
                {
                    targetCurrentWounds = Math.Max(0, targetCurrentWounds - damageDealt);
                    totalAttackerDamageDealt += damageDealt;
                }
                else
                {
                    attackerCurrentWounds = Math.Max(0, attackerCurrentWounds - damageDealt);
                    totalTargetDamageDealt += damageDealt;
                }

                activePool = new FightDicePool(activePool.Remaining.Where(d => d.Id != actionChoice.Die.Id).ToList());
            }
            else
            {
                await (eventStream?.EmitAsync((gameSessionId, sequenceNumber, timestamp) =>
                    new FightBlockResolvedEvent(
                        gameSessionId,
                        sequenceNumber,
                        timestamp,
                        activeOperative.TeamId,
                        activeOperative.Name,
                        actionChoice.Die.RolledValue,
                        actionChoice.Die.Result,
                        actionChoice.TargetDie!.RolledValue,
                        actionChoice.TargetDie!.Result)) ?? ValueTask.CompletedTask);

                (activePool, opponentPool) = FightResolution.ApplySingleBlock(
                    actionChoice.Die,
                    actionChoice.TargetDie!,
                    activePool,
                    opponentPool);
            }

            (attackerPool, targetPool) = fightTurn.Reintegrate(activePool, opponentPool);

            var nextTurn = currentTurn == DieOwner.Attacker ? DieOwner.Target : DieOwner.Attacker;
            var nextHasDice = nextTurn == DieOwner.Attacker ? attackerPool.Remaining.Count > 0 : targetPool.Remaining.Count > 0;

            if (nextHasDice)
            {
                turnOrder = nextTurn;
            }
        }

        var attackerCausedIncapacitation = targetCurrentWounds <= 0 && !targetState.IsIncapacitated;
        var targetCausedIncapacitation = attackerCurrentWounds <= 0 && !attackerState.IsIncapacitated;

        attackerState.CurrentWounds = attackerCurrentWounds;
        targetState.CurrentWounds = targetCurrentWounds;

        await (eventStream?.EmitAsync((gameSessionId, sequenceNumber, timestamp) =>
            new OperativeWoundsChangedEvent(
                gameSessionId,
                sequenceNumber,
                timestamp,
                attackerTeamId,
                attackerState.Id,
                attackerCurrentWounds)) ?? ValueTask.CompletedTask);

        await (eventStream?.EmitAsync((gameSessionId, sequenceNumber, timestamp) =>
            new OperativeWoundsChangedEvent(
                gameSessionId,
                sequenceNumber,
                timestamp,
                targetTeamId,
                targetState.Id,
                targetCurrentWounds)) ?? ValueTask.CompletedTask);

        if (attackerCausedIncapacitation)
        {
            targetState.IsIncapacitated = true;
            targetState.IsOnGuard = false;

            await (eventStream?.EmitAsync((gameSessionId, sequenceNumber, timestamp) =>
                new OperativeIncapacitatedEvent(
                    gameSessionId,
                    sequenceNumber,
                    timestamp,
                    targetTeamId,
                    targetState.Id)) ?? ValueTask.CompletedTask);

            await (eventStream?.EmitAsync((gameSessionId, sequenceNumber, timestamp) =>
                new OperativeGuardClearedEvent(
                    gameSessionId,
                    sequenceNumber,
                    timestamp,
                    targetTeamId,
                    targetState.Id)) ?? ValueTask.CompletedTask);

            await (eventStream?.EmitAsync((gameSessionId, sequenceNumber, timestamp) =>
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

            await (eventStream?.EmitAsync((gameSessionId, sequenceNumber, timestamp) =>
                new OperativeIncapacitatedEvent(
                    gameSessionId,
                    sequenceNumber,
                    timestamp,
                    attackerTeamId,
                    attackerState.Id)) ?? ValueTask.CompletedTask);

            await (eventStream?.EmitAsync((gameSessionId, sequenceNumber, timestamp) =>
                new OperativeGuardClearedEvent(
                    gameSessionId,
                    sequenceNumber,
                    timestamp,
                    attackerTeamId,
                    attackerState.Id)) ?? ValueTask.CompletedTask);

            await (eventStream?.EmitAsync((gameSessionId, sequenceNumber, timestamp) =>
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
            NormalDamageDealt = totalAttackerDamageDealt,
            CriticalDamageDealt = 0,
            CausedIncapacitation = attackerCausedIncapacitation
        };

        await actionRepository.CreateAsync(action);

        await (eventStream?.EmitAsync((gameSessionId, sequenceNumber, timestamp) =>
            new FightResolvedEvent(
                gameSessionId,
                sequenceNumber,
                timestamp,
                attackerTeamId,
                attacker.Name,
                target.Name,
                totalAttackerDamageDealt,
                totalTargetDamageDealt,
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
            totalAttackerDamageDealt,
            totalTargetDamageDealt,
            targetState.OperativeId);
    }
}
