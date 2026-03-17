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

        var atkMeleeWeapons = attacker.Weapons.Where(w => w.Type == WeaponType.Melee).ToList();
        if (atkMeleeWeapons.Count == 0)
        {
            eventStream?.Emit((seq, ts) => new CombatWarningEvent(eventStream.GameSessionId, seq, ts, isAttackerTeamId, CombatWarningKind.NoWeaponsAvailable, $"{attacker.Name} has no melee weapons!"));
            return new FightSessionResult(false, false, 0, 0, targetState.OperativeId);
        }

        var atkIsInjured = attackerState.CurrentWounds < attacker.Wounds / 2;
        Weapon atkWeapon;
        if (atkMeleeWeapons.Count == 1)
        {
            atkWeapon = atkMeleeWeapons[0];
            var atkEffHit = atkIsInjured ? atkWeapon.Hit + 1 : atkWeapon.Hit;
            eventStream?.Emit((seq, ts) => new WeaponSelectedEvent(eventStream.GameSessionId, seq, ts, isAttackerTeamId, atkWeapon.Name, atkWeapon.Atk, atkWeapon.Hit, atkWeapon.NormalDmg, atkWeapon.CriticalDmg, "Attacker", true, atkIsInjured, atkEffHit));
        }
        else
        {
            atkWeapon = await inputProvider.SelectAttackerWeaponAsync(atkMeleeWeapons, atkIsInjured);
        }

        var atkEffectiveHit = atkIsInjured ? atkWeapon.Hit + 1 : atkWeapon.Hit;

        var defMeleeWeapons = targetOp.Weapons.Where(w => w.Type == WeaponType.Melee).ToList();
        Weapon? defWeapon = null;
        var defEffectiveHit = 3;

        if (defMeleeWeapons.Count == 1)
        {
            defWeapon = defMeleeWeapons[0];
            var defIsInjured = targetState.CurrentWounds < targetOp.Wounds / 2;
            defEffectiveHit = defIsInjured ? defWeapon.Hit + 1 : defWeapon.Hit;
            eventStream?.Emit((seq, ts) => new WeaponSelectedEvent(eventStream.GameSessionId, seq, ts, defenderTeamId, defWeapon.Name, defWeapon.Atk, defWeapon.Hit, defWeapon.NormalDmg, defWeapon.CriticalDmg, "Defender", true, defIsInjured, defEffectiveHit));
        }
        else if (defMeleeWeapons.Count > 1)
        {
            defWeapon = await inputProvider.SelectDefenderWeaponAsync(defMeleeWeapons);
            var defIsInjured = targetState.CurrentWounds < targetOp.Wounds / 2;
            defEffectiveHit = defIsInjured ? defWeapon.Hit + 1 : defWeapon.Hit;
        }
        else
        {
            eventStream?.Emit((seq, ts) => new DefenderNoMeleeWeaponsEvent(eventStream.GameSessionId, seq, ts, defenderTeamId, targetOp.Name));
        }

        var fightAssist = await inputProvider.GetFightAssistCountAsync();
        atkEffectiveHit = Math.Max(2, atkEffectiveHit - fightAssist);

        int[] atkRolls = await inputProvider.RollOrEnterDiceAsync(atkWeapon.Atk, $"{attacker.Name} attack dice (Attack: {atkWeapon.Atk})", attacker.Name, "Attacker", "Fight", isAttackerTeamId, eventStream);
        atkRolls = await rerollEngine.ApplyAttackerRerollsAsync(
            atkRolls, atkWeapon.ParsedRules.ToList(), game.Id, isAttackerTeamA, attacker.Name, isAttackerTeamId, eventStream);

        var defAtkCount = defWeapon?.Atk ?? 0;
        int[] defRolls = [];
        if (defAtkCount > 0)
        {
            defRolls = await inputProvider.RollOrEnterDiceAsync(defAtkCount, $"{targetOp.Name} fight-back dice (Attack: {defAtkCount})", targetOp.Name, "Defender", "Fight", defenderTeamId, eventStream);
            defRolls = await rerollEngine.ApplyDefenderRerollAsync(defRolls, game.Id, isDefenderTeamA, targetOp.Name, defenderTeamId, eventStream);
        }

        var atkPool = fightResolutionService.CalculateDice(atkRolls, atkEffectiveHit, DieOwner.Attacker);
        var defPool = defWeapon is not null
            ? fightResolutionService.CalculateDice(defRolls, defEffectiveHit, DieOwner.Defender)
            : new FightDicePool(DieOwner.Defender, []);

        if (atkWeapon.ParsedRules.Any(r => r.Kind == SpecialRuleKind.Shock) && atkPool.Remaining.Any(d => d.Result == DieResult.Crit))
        {
            var lowestDefSuccess = defPool.Remaining.OrderBy(d => d.RolledValue).FirstOrDefault(d => d.Result != DieResult.Miss);
            if (lowestDefSuccess is not null)
            {
                defPool = defPool with { Remaining = defPool.Remaining.Where(d => d.Id != lowestDefSuccess.Id).ToList() };
                eventStream?.Emit((seq, ts) => new ShockAppliedEvent(eventStream.GameSessionId, seq, ts, isAttackerTeamId, targetOp.Name, lowestDefSuccess.RolledValue));
            }
        }

        var brutalWeapon = atkWeapon.ParsedRules.Any(r => r.Kind == SpecialRuleKind.Brutal);
        var atkCurrentWounds = attackerState.CurrentWounds;
        var defCurrentWounds = targetState.CurrentWounds;
        var totalAtkDmgDealt = 0;
        var totalDefDmgDealt = 0;
        var currentOwner = DieOwner.Attacker;

        while (atkPool.Remaining.Count > 0 || defPool.Remaining.Count > 0)
        {
            FightDicePool activePool, opponentPool;
            DieOwner activeOwner;

            if (currentOwner == DieOwner.Attacker)
            {
                if (atkPool.Remaining.Count > 0)
                {
                    activePool = atkPool; opponentPool = defPool; activeOwner = DieOwner.Attacker;
                }
                else
                {
                    activePool = defPool; opponentPool = atkPool; activeOwner = DieOwner.Defender;
                }
            }
            else
            {
                if (defPool.Remaining.Count > 0)
                {
                    activePool = defPool; opponentPool = atkPool; activeOwner = DieOwner.Defender;
                }
                else
                {
                    activePool = atkPool; opponentPool = defPool; activeOwner = DieOwner.Attacker;
                }
            }

            Operative activeOp = activeOwner == DieOwner.Attacker ? attacker : targetOp;
            Operative opponentOp = activeOwner == DieOwner.Attacker ? targetOp : attacker;
            Weapon activeWeapon = activeOwner == DieOwner.Attacker ? atkWeapon : (defWeapon ?? atkWeapon);

            var poolEvt = new FightPoolsDisplayedEvent(
                eventStream?.GameSessionId ?? Guid.Empty, 0, DateTime.UtcNow, isAttackerTeamId,
                attacker.Name, atkCurrentWounds, attacker.Wounds,
                atkPool.Remaining.Select(d => new FightDieSnapshot(d.Result == DieResult.Crit ? "CRIT" : "HIT", d.RolledValue)).ToList(),
                targetOp.Name, defCurrentWounds, targetOp.Wounds,
                defPool.Remaining.Select(d => new FightDieSnapshot(d.Result == DieResult.Crit ? "CRIT" : "HIT", d.RolledValue)).ToList());
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
                    defCurrentWounds = Math.Max(0, defCurrentWounds - dmg);
                    totalAtkDmgDealt += dmg;
                }
                else
                {
                    atkCurrentWounds = Math.Max(0, atkCurrentWounds - dmg);
                    totalDefDmgDealt += dmg;
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
                atkPool = activePool;
                defPool = opponentPool;
            }
            else
            {
                defPool = activePool;
                atkPool = opponentPool;
            }

            var nextOwner = activeOwner == DieOwner.Attacker ? DieOwner.Defender : DieOwner.Attacker;
            var nextHasDice = nextOwner == DieOwner.Attacker ? atkPool.Remaining.Count > 0 : defPool.Remaining.Count > 0;
            if (nextHasDice)
            {
                currentOwner = nextOwner;
            }
        }

        var atkCausedIncap = defCurrentWounds <= 0 && !targetState.IsIncapacitated;
        var defCausedIncap = atkCurrentWounds <= 0 && !attackerState.IsIncapacitated;

        attackerState.CurrentWounds = atkCurrentWounds;
        targetState.CurrentWounds = defCurrentWounds;

        await stateRepository.UpdateWoundsAsync(attackerState.Id, atkCurrentWounds);
        await stateRepository.UpdateWoundsAsync(targetState.Id, defCurrentWounds);

        if (atkCausedIncap)
        {
            targetState.IsIncapacitated = true;
            await stateRepository.SetIncapacitatedAsync(targetState.Id, true);
            await stateRepository.UpdateGuardAsync(targetState.Id, false);
            targetState.IsOnGuard = false;
            eventStream?.Emit((seq, ts) => new IncapacitationEvent(eventStream.GameSessionId, seq, ts, isAttackerTeamId, targetOp.Name, "Fight"));
        }
        if (defCausedIncap)
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
            WeaponId = atkWeapon.Id,
            AttackerDice = atkRolls,
            DefenderDice = defRolls,
            NormalDamageDealt = totalAtkDmgDealt,
            CriticalDamageDealt = 0,
            CausedIncapacitation = atkCausedIncap
        };
        await actionRepository.CreateAsync(action);
        eventStream?.Emit((seq, ts) => new FightResolvedEvent(eventStream.GameSessionId, seq, ts, isAttackerTeamId, attacker.Name, targetOp.Name, totalAtkDmgDealt, totalDefDmgDealt, atkCausedIncap, defCausedIncap));

        var note = await inputProvider.GetNarrativeNoteAsync();

        if (!string.IsNullOrWhiteSpace(note))
        {
            await actionRepository.UpdateNarrativeAsync(action.Id, note);
        }

        return new FightSessionResult(atkCausedIncap, defCausedIncap, totalAtkDmgDealt, totalDefDmgDealt, targetState.OperativeId);
    }
}
