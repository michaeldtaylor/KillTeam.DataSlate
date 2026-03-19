using KillTeam.DataSlate.Domain.Engine.Input;
using KillTeam.DataSlate.Domain.Engine.WeaponRules;
using KillTeam.DataSlate.Domain.Events;
using KillTeam.DataSlate.Domain.Models;
using KillTeam.DataSlate.Domain.Repositories;
using KillTeam.DataSlate.Domain.Services;

namespace KillTeam.DataSlate.Domain.Engine;

public class FightEngine(
    IFightInputProvider inputProvider,
    RerollEngine rerollEngine,
    IActionRepository actionRepository,
    FightWeaponRuleApplicator weaponRuleApplicator)
{
    public async Task<FightSessionResult> RunAsync(
        Operative attacker,
        GameOperativeState attackerState,
        IReadOnlyList<GameOperativeState> allOperativeStates,
        IReadOnlyDictionary<Guid, Operative> allOperatives,
        Game game,
        Activation activation,
        GameEventStream? eventStream = null)
    {
        var isAttackerTeam1 = attacker.TeamId == game.Participant1.TeamId;
        var isAttackerTeamId = attacker.TeamId;

        var targetStates = allOperativeStates
            .Where(s => !s.IsIncapacitated
                && allOperatives.TryGetValue(s.OperativeId, out var o)
                && o.TeamId != attacker.TeamId)
            .ToList();

        if (targetStates.Count == 0)
        {
            await (eventStream?.EmitAsync((gameSessionId, sequenceNumber, timestamp) =>
                new CombatWarningEvent(
                    gameSessionId,
                    sequenceNumber,
                    timestamp,
                    isAttackerTeamId,
                    CombatWarningKind.NoValidTargets,
                    "No valid fight targets available.")) ?? ValueTask.CompletedTask);

            return new FightSessionResult(false, false, 0, 0, Guid.Empty);
        }

        GameOperativeState targetState;

        if (targetStates.Count == 1)
        {
            targetState = targetStates[0];

            if (allOperatives.TryGetValue(targetState.OperativeId, out var autoTarget))
            {
                await (eventStream?.EmitAsync((gameSessionId, sequenceNumber, timestamp) =>
                    new FightTargetSelectedEvent(
                        gameSessionId,
                        sequenceNumber,
                        timestamp,
                        isAttackerTeamId,
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
                    isAttackerTeamId,
                    CombatWarningKind.TargetNotFound,
                    "Target operative not found.")) ?? ValueTask.CompletedTask);

            return new FightSessionResult(false, false, 0, 0, Guid.Empty);
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
                    isAttackerTeamId,
                    CombatWarningKind.NoWeaponsAvailable,
                    $"{attacker.Name} has no melee weapons!")) ?? ValueTask.CompletedTask);

            return new FightSessionResult(false, false, 0, 0, targetState.OperativeId);
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
                    isAttackerTeamId,
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

        if (targetMeleeWeapons.Count == 1)
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
        }
        else if (targetMeleeWeapons.Count > 1)
        {
            targetWeapon = await inputProvider.SelectTargetWeaponAsync(targetMeleeWeapons);
            var targetIsInjured = targetState.CurrentWounds < target.Wounds / 2;

            targetEffectiveHit = targetIsInjured ? targetWeapon.Hit + 1 : targetWeapon.Hit;
        }
        else
        {
            await (eventStream?.EmitAsync((gameSessionId, sequenceNumber, timestamp) =>
                new TargetNoMeleeWeaponsEvent(
                    gameSessionId,
                    sequenceNumber,
                    timestamp,
                    targetTeamId,
                    target.Name)) ?? ValueTask.CompletedTask);
        }

        var fightAssist = await inputProvider.GetFightAssistCountAsync();

        attackerEffectiveHit = Math.Max(2, attackerEffectiveHit - fightAssist);

        var attackerRolls = await inputProvider.RollOrEnterDiceAsync(attackerWeapon.Atk, $"{attacker.Name} attack dice (Attack: {attackerWeapon.Atk})", attacker.Name, "Attacker", "Fight", isAttackerTeamId, eventStream);

        attackerRolls = await rerollEngine.ApplyAttackerRerollsAsync(
            attackerRolls,
            attackerWeapon.Rules.ToList(),
            game.Id,
            isAttackerTeam1,
            attacker.Name,
            isAttackerTeamId,
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

        var preResolutionContext = new FightPreResolutionContext
        {
            Attacker = attacker,
            Target = target,
            EventStream = eventStream,
            AttackerPool = attackerPool,
            TargetPool = targetPool,
        };

        await weaponRuleApplicator.ApplyPreResolutionAsync(attackerWeapon, preResolutionContext);

        attackerPool = preResolutionContext.AttackerPool;
        targetPool = preResolutionContext.TargetPool;

        var attackerCurrentWounds = attackerState.CurrentWounds;
        var targetCurrentWounds = targetState.CurrentWounds;
        var totalAttackerDamageDealt = 0;
        var totalTargetDamageDealt = 0;
        var turnOrder = DieOwner.Attacker;

        while (attackerPool.Remaining.Count > 0 || targetPool.Remaining.Count > 0)
        {
            var turnContext = FightTurnContext.Resolve(
                turnOrder,
                attackerPool,
                targetPool,
                attacker,
                target,
                attackerWeapon,targetWeapon);

            var (activePool, opponentPool, currentTurn, activeOperative, opponentOperative, activeWeapon) = turnContext;

            var attackerWoundsNow = attackerCurrentWounds;
            var targetWoundsNow = targetCurrentWounds;
            var attackerPoolNow = attackerPool;
            var targetPoolNow = targetPool;

            await (eventStream?.EmitAsync((gameSessionId, sequenceNumber, timestamp) =>
                new FightPoolsDisplayedEvent(
                    gameSessionId,
                    sequenceNumber,
                    timestamp,
                    isAttackerTeamId,
                    attacker.Name,
                    attackerWoundsNow,
                    attacker.Wounds,
                    attackerPoolNow.Remaining.Select(d => new FightDieSnapshot(d.Result, d.RolledValue)).ToList(),
                    target.Name,
                    targetWoundsNow,
                    target.Wounds,
                    targetPoolNow.Remaining.Select(d => new FightDieSnapshot(d.Result, d.RolledValue)).ToList())) ?? ValueTask.CompletedTask);

            var useBrutal = preResolutionContext.IsBrutal && currentTurn == DieOwner.Attacker;
            var actions = FightResolution.GetAvailableActions(activePool, opponentPool, useBrutal);

            var uniqueActions = actions
                .GroupBy(a => (a.Type, a.ActiveDie.Result, TargetResult: a.OpponentDie?.Result))
                .Select(g => g.First())
                .ToList();

            var opponentHasCrits = opponentPool.Remaining.Any(d => d.Result == DieResult.Crit);

            if (opponentHasCrits)
            {
                uniqueActions = uniqueActions
                    .Where(a => a.Type != FightActionType.Block
                        || a.ActiveDie.Result != DieResult.Crit
                        || a.OpponentDie?.Result != DieResult.Hit)
                    .ToList();
            }

            if (uniqueActions.Count == 0)
            {
                break;
            }

            var actionChoice = await inputProvider.SelectActionAsync(uniqueActions, activeOperative.Name);

            if (actionChoice.Type == FightActionType.Strike)
            {
                var damageDealt = FightResolution.ApplyStrike(actionChoice.ActiveDie, activeWeapon.NormalDmg, activeWeapon.CriticalDmg);

                await (eventStream?.EmitAsync((gameSessionId, sequenceNumber, timestamp) =>
                    new FightStrikeResolvedEvent(
                        gameSessionId,
                        sequenceNumber,
                        timestamp,
                        activeOperative.TeamId,
                        activeOperative.Name,
                        opponentOperative.Name,
                        actionChoice.ActiveDie.RolledValue,
                        actionChoice.ActiveDie.Result,
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

                activePool = new FightDicePool(activePool.Remaining.Where(d => d.Id != actionChoice.ActiveDie.Id).ToList());
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
                        actionChoice.ActiveDie.RolledValue,
                        actionChoice.ActiveDie.Result,
                        actionChoice.OpponentDie!.RolledValue,
                        actionChoice.OpponentDie!.Result)) ?? ValueTask.CompletedTask);

                (activePool, opponentPool) = FightResolution.ApplySingleBlock(
                    actionChoice.ActiveDie,
                    actionChoice.OpponentDie!,
                    activePool,
                    opponentPool);
            }

            (attackerPool, targetPool) = turnContext.Reintegrate(activePool, opponentPool);

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
                isAttackerTeamId,
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
                    isAttackerTeamId,
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
                    isAttackerTeamId,
                    attackerState.Id)) ?? ValueTask.CompletedTask);

            await (eventStream?.EmitAsync((gameSessionId, sequenceNumber, timestamp) =>
                new OperativeGuardClearedEvent(
                    gameSessionId,
                    sequenceNumber,
                    timestamp,
                    isAttackerTeamId,
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
                isAttackerTeamId,
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

        return new FightSessionResult(
            attackerCausedIncapacitation,
            targetCausedIncapacitation,
            totalAttackerDamageDealt,
            totalTargetDamageDealt,
            targetState.OperativeId);
    }
}
