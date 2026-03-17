using KillTeam.DataSlate.Domain.Engine.Input;
using KillTeam.DataSlate.Domain.Events;
using KillTeam.DataSlate.Domain.Models;
using KillTeam.DataSlate.Domain.Repositories;
using KillTeam.DataSlate.Domain.Services;

namespace KillTeam.DataSlate.Domain.Engine;

public class FightEngine(
    IFightInputProvider inputProvider,
    FightResolutionService fightResolutionService,
    RerollEngine rerollEngine,
    IGameOperativeStateRepository stateRepository,
    IActionRepository actionRepository)
{
    public async Task<FightSessionResult> RunAsync(
        Operative attacker,
        GameOperativeState attackerState,
        IReadOnlyList<GameOperativeState> allOperativeStates,
        IReadOnlyDictionary<Guid, Operative> allOperatives,
        Game game,
        TurningPoint tp,
        Activation activation,
        GameEventStream? eventStream = null)
    {
        var isAttackerTeamA = attacker.TeamId == game.Participant1.TeamId;
        var isAttackerTeamId = attacker.TeamId;

        var enemyStates = allOperativeStates
            .Where(s => !s.IsIncapacitated
                && allOperatives.TryGetValue(s.OperativeId, out var o)
                && o.TeamId != attacker.TeamId)
            .ToList();

        if (enemyStates.Count == 0)
        {
            eventStream?.Emit((seq, ts) => new CombatWarningEvent(eventStream.GameSessionId, seq, ts, isAttackerTeamId, CombatWarningKind.NoValidTargets, "No valid fight targets available."));
            return new FightSessionResult(false, false, 0, 0, Guid.Empty);
        }

        GameOperativeState targetState;
        if (enemyStates.Count == 1)
        {
            targetState = enemyStates[0];
            if (allOperatives.TryGetValue(targetState.OperativeId, out var autoTarget))
            {
                eventStream?.Emit((seq, ts) => new FightTargetSelectedEvent(eventStream.GameSessionId, seq, ts, isAttackerTeamId, autoTarget.Name, targetState.CurrentWounds, autoTarget.Wounds, true));
            }
        }
        else
        {
            targetState = await inputProvider.SelectTargetAsync(enemyStates, allOperatives);
        }

        if (!allOperatives.TryGetValue(targetState.OperativeId, out var targetOp))
        {
            eventStream?.Emit((seq, ts) => new CombatWarningEvent(eventStream.GameSessionId, seq, ts, isAttackerTeamId, CombatWarningKind.TargetNotFound, "Target operative not found."));
            return new FightSessionResult(false, false, 0, 0, Guid.Empty);
        }
        var defenderTeamId = targetOp.TeamId;
        var isDefenderTeamA = targetOp.TeamId == game.Participant1.TeamId;

        var attackerMeleeWeapons = attacker.Weapons.Where(w => w.Type == WeaponType.Melee).ToList();

        if (attackerMeleeWeapons.Count == 0)
        {
            eventStream?.Emit((seq, ts) => new CombatWarningEvent(eventStream.GameSessionId, seq, ts, isAttackerTeamId, CombatWarningKind.NoWeaponsAvailable, $"{attacker.Name} has no melee weapons!"));
            return new FightSessionResult(false, false, 0, 0, targetState.OperativeId);
        }

        var attackerIsInjured = attackerState.CurrentWounds < attacker.Wounds / 2;
        Weapon attackerWeapon;

        if (attackerMeleeWeapons.Count == 1)
        {
            attackerWeapon = attackerMeleeWeapons[0];
            eventStream?.Emit((seq, ts) => new WeaponSelectedEvent(eventStream.GameSessionId, seq, ts, isAttackerTeamId, attackerWeapon.Name, attackerWeapon.Atk, attackerWeapon.Hit, attackerWeapon.NormalDmg, attackerWeapon.CriticalDmg, "Attacker", true, attackerIsInjured, attackerIsInjured ? attackerWeapon.Hit + 1 : attackerWeapon.Hit));
        }
        else
        {
            attackerWeapon = await inputProvider.SelectAttackerWeaponAsync(attackerMeleeWeapons, attackerIsInjured);
        }

        var attackerEffectiveHit = attackerIsInjured ? attackerWeapon.Hit + 1 : attackerWeapon.Hit;

        var defenderMeleeWeapons = targetOp.Weapons.Where(w => w.Type == WeaponType.Melee).ToList();
        Weapon? defenderWeapon = null;
        var defenderEffectiveHit = 3;

        if (defenderMeleeWeapons.Count == 1)
        {
            defenderWeapon = defenderMeleeWeapons[0];
            var defenderIsInjured = targetState.CurrentWounds < targetOp.Wounds / 2;

            defenderEffectiveHit = defenderIsInjured ? defenderWeapon.Hit + 1 : defenderWeapon.Hit;
            eventStream?.Emit((seq, ts) => new WeaponSelectedEvent(eventStream.GameSessionId, seq, ts, defenderTeamId, defenderWeapon.Name, defenderWeapon.Atk, defenderWeapon.Hit, defenderWeapon.NormalDmg, defenderWeapon.CriticalDmg, "Defender", true, defenderIsInjured, defenderEffectiveHit));
        }
        else if (defenderMeleeWeapons.Count > 1)
        {
            defenderWeapon = await inputProvider.SelectDefenderWeaponAsync(defenderMeleeWeapons);
            var defenderIsInjured = targetState.CurrentWounds < targetOp.Wounds / 2;

            defenderEffectiveHit = defenderIsInjured ? defenderWeapon.Hit + 1 : defenderWeapon.Hit;
        }
        else
        {
            eventStream?.Emit((seq, ts) => new DefenderNoMeleeWeaponsEvent(eventStream.GameSessionId, seq, ts, defenderTeamId, targetOp.Name));
        }

        var fightAssist = await inputProvider.GetFightAssistCountAsync();

        attackerEffectiveHit = Math.Max(2, attackerEffectiveHit - fightAssist);

        var attackerRolls = await inputProvider.RollOrEnterDiceAsync(attackerWeapon.Atk, $"{attacker.Name} attack dice (Attack: {attackerWeapon.Atk})", attacker.Name, "Attacker", "Fight", isAttackerTeamId, eventStream);
        attackerRolls = await rerollEngine.ApplyAttackerRerollsAsync(
            attackerRolls, attackerWeapon.ParsedRules.ToList(), game.Id, isAttackerTeamA, attacker.Name, isAttackerTeamId, eventStream);

        var defenderAttackCount = defenderWeapon?.Atk ?? 0;
        int[] defenderRolls = [];

        if (defenderAttackCount > 0)
        {
            defenderRolls = await inputProvider.RollOrEnterDiceAsync(defenderAttackCount, $"{targetOp.Name} fight-back dice (Attack: {defenderAttackCount})", targetOp.Name, "Defender", "Fight", defenderTeamId, eventStream);
            defenderRolls = await rerollEngine.ApplyDefenderRerollAsync(defenderRolls, game.Id, isDefenderTeamA, targetOp.Name, defenderTeamId, eventStream);
        }

        var attackerPool = fightResolutionService.CalculateDice(attackerRolls, attackerEffectiveHit, DieOwner.Attacker);
        var defenderPool = defenderWeapon is not null
            ? fightResolutionService.CalculateDice(defenderRolls, defenderEffectiveHit, DieOwner.Defender)
            : new FightDicePool(DieOwner.Defender, []);

        if (attackerWeapon.ParsedRules.Any(r => r.Kind == SpecialRuleKind.Shock) && attackerPool.Remaining.Any(d => d.Result == DieResult.Crit))
        {
            var lowestDefenderSuccess = defenderPool.Remaining.OrderBy(d => d.RolledValue).FirstOrDefault(d => d.Result != DieResult.Miss);

            if (lowestDefenderSuccess is not null)
            {
                defenderPool = defenderPool with { Remaining = defenderPool.Remaining.Where(d => d.Id != lowestDefenderSuccess.Id).ToList() };
                eventStream?.Emit((seq, ts) => new ShockAppliedEvent(eventStream.GameSessionId, seq, ts, isAttackerTeamId, targetOp.Name, lowestDefenderSuccess.RolledValue));
            }
        }

        var brutalWeapon = attackerWeapon.ParsedRules.Any(r => r.Kind == SpecialRuleKind.Brutal);
        var attackerCurrentWounds = attackerState.CurrentWounds;
        var defenderCurrentWounds = targetState.CurrentWounds;
        var totalAttackerDamageDealt = 0;
        var totalDefenderDamageDealt = 0;
        var currentOwner = DieOwner.Attacker;

        while (attackerPool.Remaining.Count > 0 || defenderPool.Remaining.Count > 0)
        {
            FightDicePool activePool, opponentPool;
            DieOwner activeOwner;

            if (currentOwner == DieOwner.Attacker)
            {
                if (attackerPool.Remaining.Count > 0)
                {
                    activePool = attackerPool; opponentPool = defenderPool; activeOwner = DieOwner.Attacker;
                }
                else
                {
                    activePool = defenderPool; opponentPool = attackerPool; activeOwner = DieOwner.Defender;
                }
            }
            else
            {
                if (defenderPool.Remaining.Count > 0)
                {
                    activePool = defenderPool; opponentPool = attackerPool; activeOwner = DieOwner.Defender;
                }
                else
                {
                    activePool = attackerPool; opponentPool = defenderPool; activeOwner = DieOwner.Attacker;
                }
            }

            Operative activeOp = activeOwner == DieOwner.Attacker ? attacker : targetOp;
            Operative opponentOp = activeOwner == DieOwner.Attacker ? targetOp : attacker;
            Weapon activeWeapon = activeOwner == DieOwner.Attacker ? attackerWeapon : (defenderWeapon ?? attackerWeapon);

            var poolEvt = new FightPoolsDisplayedEvent(
                eventStream?.GameSessionId ?? Guid.Empty, 0, DateTime.UtcNow, isAttackerTeamId,
                attacker.Name, attackerCurrentWounds, attacker.Wounds,
                attackerPool.Remaining.Select(d => new FightDieSnapshot(d.Result == DieResult.Crit ? "CRIT" : "HIT", d.RolledValue)).ToList(),
                targetOp.Name, defenderCurrentWounds, targetOp.Wounds,
                defenderPool.Remaining.Select(d => new FightDieSnapshot(d.Result == DieResult.Crit ? "CRIT" : "HIT", d.RolledValue)).ToList());
            eventStream?.Emit(poolEvt);

            var useBrutal = brutalWeapon && activeOwner == DieOwner.Attacker;
            var actions = fightResolutionService.GetAvailableActions(activePool, opponentPool, useBrutal);

            var uniqueActions = actions
                .GroupBy(a => (a.Type, a.ActiveDie.Result, TargetResult: a.TargetDie?.Result))
                .Select(g => g.First())
                .ToList();

            var opponentHasCrits = opponentPool.Remaining.Any(d => d.Result == DieResult.Crit);

            if (opponentHasCrits)
            {
                uniqueActions = uniqueActions
                    .Where(a => a.Type != FightActionType.Block
                        || a.ActiveDie.Result != DieResult.Crit
                        || a.TargetDie?.Result != DieResult.Hit)
                    .ToList();
            }

            if (uniqueActions.Count == 0)
            {
                break;
            }

            var actionChoice = await inputProvider.SelectActionAsync(uniqueActions, activeOp.Name);

            if (actionChoice.Type == FightActionType.Strike)
            {
                var dmg = fightResolutionService.ApplyStrike(actionChoice.ActiveDie, activeWeapon.NormalDmg, activeWeapon.CriticalDmg);

                eventStream?.Emit((seq, ts) => new FightStrikeResolvedEvent(eventStream.GameSessionId, seq, ts, activeOp.TeamId, activeOp.Name, opponentOp.Name, actionChoice.ActiveDie.RolledValue, actionChoice.ActiveDie.Result == DieResult.Crit ? "CRIT" : "HIT", dmg));

                if (activeOwner == DieOwner.Attacker)
                {
                    defenderCurrentWounds = Math.Max(0, defenderCurrentWounds - dmg);
                    totalAttackerDamageDealt += dmg;
                }
                else
                {
                    attackerCurrentWounds = Math.Max(0, attackerCurrentWounds - dmg);
                    totalDefenderDamageDealt += dmg;
                }

                activePool = activePool with
                {
                    Remaining = activePool.Remaining.Where(d => d.Id != actionChoice.ActiveDie.Id).ToList()
                };
            }
            else
            {
                eventStream?.Emit((seq, ts) => new FightBlockResolvedEvent(eventStream.GameSessionId, seq, ts, activeOp.TeamId, activeOp.Name, actionChoice.ActiveDie.RolledValue, actionChoice.ActiveDie.Result == DieResult.Crit ? "CRIT" : "HIT", actionChoice.TargetDie!.RolledValue, actionChoice.TargetDie!.Result == DieResult.Crit ? "CRIT" : "HIT"));
                (activePool, opponentPool) = fightResolutionService.ApplySingleBlock(
                    actionChoice.ActiveDie, actionChoice.TargetDie!, activePool, opponentPool);
            }

            if (activeOwner == DieOwner.Attacker)
            {
                attackerPool = activePool;
                defenderPool = opponentPool;
            }
            else
            {
                defenderPool = activePool;
                attackerPool = opponentPool;
            }

            var nextOwner = activeOwner == DieOwner.Attacker ? DieOwner.Defender : DieOwner.Attacker;
            var nextHasDice = nextOwner == DieOwner.Attacker ? attackerPool.Remaining.Count > 0 : defenderPool.Remaining.Count > 0;

            if (nextHasDice)
            {
                currentOwner = nextOwner;
            }
        }

        var attackerCausedIncapacitation = defenderCurrentWounds <= 0 && !targetState.IsIncapacitated;
        var defenderCausedIncapacitation = attackerCurrentWounds <= 0 && !attackerState.IsIncapacitated;

        attackerState.CurrentWounds = attackerCurrentWounds;
        targetState.CurrentWounds = defenderCurrentWounds;

        await stateRepository.UpdateWoundsAsync(attackerState.Id, attackerCurrentWounds);
        await stateRepository.UpdateWoundsAsync(targetState.Id, defenderCurrentWounds);

        if (attackerCausedIncapacitation)
        {
            targetState.IsIncapacitated = true;
            await stateRepository.SetIncapacitatedAsync(targetState.Id, true);
            await stateRepository.UpdateGuardAsync(targetState.Id, false);
            targetState.IsOnGuard = false;
            eventStream?.Emit((seq, ts) => new IncapacitationEvent(eventStream.GameSessionId, seq, ts, isAttackerTeamId, targetOp.Name, "Fight"));
        }
        if (defenderCausedIncapacitation)
        {
            attackerState.IsIncapacitated = true;
            await stateRepository.SetIncapacitatedAsync(attackerState.Id, true);
            await stateRepository.UpdateGuardAsync(attackerState.Id, false);
            attackerState.IsOnGuard = false;
            eventStream?.Emit((seq, ts) => new IncapacitationEvent(eventStream.GameSessionId, seq, ts, defenderTeamId, attacker.Name, "Fight"));
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
            DefenderDice = defenderRolls,
            NormalDamageDealt = totalAttackerDamageDealt,
            CriticalDamageDealt = 0,
            CausedIncapacitation = attackerCausedIncapacitation
        };
        await actionRepository.CreateAsync(action);
        eventStream?.Emit((seq, ts) => new FightResolvedEvent(eventStream.GameSessionId, seq, ts, isAttackerTeamId, attacker.Name, targetOp.Name, totalAttackerDamageDealt, totalDefenderDamageDealt, attackerCausedIncapacitation, defenderCausedIncapacitation));

        var note = await inputProvider.GetNarrativeNoteAsync();

        if (!string.IsNullOrWhiteSpace(note))
        {
            await actionRepository.UpdateNarrativeAsync(action.Id, note);
        }

        return new FightSessionResult(attackerCausedIncapacitation, defenderCausedIncapacitation, totalAttackerDamageDealt, totalDefenderDamageDealt, targetState.OperativeId);
    }
}
