using KillTeam.DataSlate.Domain.Engine.Input;
using KillTeam.DataSlate.Domain.Events;
using KillTeam.DataSlate.Domain.Models;
using KillTeam.DataSlate.Domain.Repositories;
using KillTeam.DataSlate.Domain.Services;

namespace KillTeam.DataSlate.Domain.Engine;

public class ShootEngine(
    IShootInputProvider inputProvider,
    CombatResolutionService combatResolutionService,
    RerollEngine rerollEngine,
    BlastEngine blastEngine,
    IGameOperativeStateRepository stateRepository,
    IActionRepository actionRepository)
{
    public async Task<ShootSessionResult> RunAsync(
        Operative attacker,
        GameOperativeState attackerState,
        IReadOnlyList<GameOperativeState> allOperativeStates,
        IReadOnlyDictionary<Guid, Operative> allOperatives,
        Game game,
        TurningPoint tp,
        Activation activation,
        bool hasMovedNonDash = false,
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
            eventStream?.Emit((seq, ts) => new CombatWarningEvent(eventStream.GameSessionId, seq, ts, isAttackerTeamId, CombatWarningKind.NoValidTargets, "No valid targets available."));
            return new ShootSessionResult(false, 0, null);
        }

        GameOperativeState targetState;
        if (enemyStates.Count == 1)
        {
            targetState = enemyStates[0];
            if (allOperatives.TryGetValue(targetState.OperativeId, out var autoTarget))
            {
                eventStream?.Emit((seq, ts) => new ShootTargetSelectedEvent(eventStream.GameSessionId, seq, ts, isAttackerTeamId, autoTarget.Name, targetState.CurrentWounds, autoTarget.Wounds, true));
            }
        }
        else
        {
            targetState = await inputProvider.SelectTargetAsync(enemyStates, allOperatives);
        }

        if (!allOperatives.TryGetValue(targetState.OperativeId, out var targetOp))
        {
            eventStream?.Emit((seq, ts) => new CombatWarningEvent(eventStream.GameSessionId, seq, ts, isAttackerTeamId, CombatWarningKind.TargetNotFound, "Target operative not found."));
            return new ShootSessionResult(false, 0, null);
        }
        var defenderTeamId = targetOp.TeamId;

        var rangedWeapons = attacker.Weapons
            .Where(w => w.Type == WeaponType.Ranged)
            .Where(w => !hasMovedNonDash || !w.ParsedRules.Any(r => r.Kind == SpecialRuleKind.Heavy))
            .ToList();

        if (rangedWeapons.Count == 0)
        {
            eventStream?.Emit((seq, ts) => new CombatWarningEvent(eventStream.GameSessionId, seq, ts, isAttackerTeamId, CombatWarningKind.NoWeaponsAvailable, "No ranged weapons available."));
            return new ShootSessionResult(false, 0, null);
        }

        Weapon weapon;
        if (rangedWeapons.Count == 1)
        {
            weapon = rangedWeapons[0];
            eventStream?.Emit((seq, ts) => new WeaponSelectedEvent(eventStream.GameSessionId, seq, ts, isAttackerTeamId, weapon.Name, weapon.Atk, weapon.Hit, weapon.NormalDmg, weapon.CriticalDmg, "Attacker", true, false, weapon.Hit));
        }
        else
        {
            weapon = await inputProvider.SelectWeaponAsync(rangedWeapons, hasMovedNonDash);
        }

        if (weapon.ParsedRules.Any(r => r.Kind == SpecialRuleKind.Blast || r.Kind == SpecialRuleKind.Torrent))
        {
            var blastResult = await blastEngine.RunAsync(
                attacker, attackerState,
                targetOp, targetState,
                weapon,
                allOperativeStates,
                allOperatives,
                game, tp, activation, eventStream);
            return new ShootSessionResult(blastResult.AnyIncapacitation, blastResult.TotalDamage, targetState.OperativeId);
        }

        var coverChoice = await inputProvider.GetCoverStatusAsync(targetOp.Name);
        var inCover = coverChoice == "In cover";
        var isObscured = coverChoice == "Obscured";

        var fightAssist = await inputProvider.GetFriendlyAllyCountAsync();

        var attackDice = await inputProvider.RollOrEnterDiceAsync(weapon.Atk, $"{attacker.Name} attack dice (Attack: {weapon.Atk})", attacker.Name, "Attacker", "Shoot", isAttackerTeamId, eventStream);

        attackDice = await rerollEngine.ApplyAttackerRerollsAsync(
            attackDice, weapon.ParsedRules.ToList(), game.Id, isAttackerTeamA, attacker.Name, isAttackerTeamId, eventStream);

        var defenderDiceCount = targetOp.Defence + targetState.DefenceDiceModifier;

        if (inCover)
        {
            eventStream?.Emit((seq, ts) => new CoverSaveNotifiedEvent(eventStream.GameSessionId, seq, ts, isAttackerTeamId, targetOp.Name));
        }

        var defenderDice = defenderDiceCount == 0
            ? []
            : await inputProvider.RollOrEnterDiceAsync(defenderDiceCount, $"{targetOp.Name} defence dice", targetOp.Name, "Defender", "Shoot", defenderTeamId, eventStream);

        var isDefenderTeamA = targetOp.TeamId == game.Participant1.TeamId;

        defenderDice = await rerollEngine.ApplyDefenderRerollAsync(defenderDice, game.Id, isDefenderTeamA, targetOp.Name, defenderTeamId, eventStream);

        var ctx = new ShootContext(
            AttackDice: attackDice,
            DefenceDice: defenderDice,
            InCover: inCover,
            IsObscured: isObscured,
            HitThreshold: weapon.Hit,
            SaveThreshold: targetOp.Save,
            NormalDmg: weapon.NormalDmg,
            CritDmg: weapon.CriticalDmg,
            WeaponRules: weapon.ParsedRules.ToList(),
            FightAssistBonus: fightAssist
        );

        var result = combatResolutionService.ResolveShoot(ctx);

        eventStream?.Emit((seq, ts) => new ShootResultDisplayedEvent(eventStream.GameSessionId, seq, ts, isAttackerTeamId, targetOp.Name, result.UnblockedCrits, result.UnblockedNormals, result.TotalDamage, inCover, isObscured));

        var newWounds = Math.Max(0, targetState.CurrentWounds - result.TotalDamage);
        var causedIncap = newWounds <= 0 && !targetState.IsIncapacitated;

        targetState.CurrentWounds = newWounds;
        await stateRepository.UpdateWoundsAsync(targetState.Id, newWounds);

        if (causedIncap)
        {
            targetState.IsIncapacitated = true;
            await stateRepository.SetIncapacitatedAsync(targetState.Id, true);
            await stateRepository.UpdateGuardAsync(targetState.Id, false);
            targetState.IsOnGuard = false;
            eventStream?.Emit((seq, ts) => new IncapacitationEvent(eventStream.GameSessionId, seq, ts, isAttackerTeamId, targetOp.Name, "Shoot"));
        }

        var stunApplied = result.StunApplied;
        var selfDamage = 0;

        if (stunApplied)
        {
            await stateRepository.SetAplModifierAsync(targetState.Id, -1);
            targetState.AplModifier -= 1;
            eventStream?.Emit((seq, ts) => new StunAppliedEvent(eventStream.GameSessionId, seq, ts, isAttackerTeamId, targetOp.Name, 1));
        }

        if (weapon.ParsedRules.Any(r => r.Kind == SpecialRuleKind.Hot) && result.SelfDamageDealt > 0)
        {
            selfDamage = result.SelfDamageDealt;
            var newAttackerWounds = Math.Max(0, attackerState.CurrentWounds - selfDamage);
            attackerState.CurrentWounds = newAttackerWounds;
            await stateRepository.UpdateWoundsAsync(attackerState.Id, newAttackerWounds);
            eventStream?.Emit((seq, ts) => new SelfDamageDealtEvent(eventStream.GameSessionId, seq, ts, isAttackerTeamId, attacker.Name, selfDamage, newAttackerWounds));
            if (newAttackerWounds <= 0 && !attackerState.IsIncapacitated)
            {
                attackerState.IsIncapacitated = true;
                await stateRepository.SetIncapacitatedAsync(attackerState.Id, true);
                eventStream?.Emit((seq, ts) => new IncapacitationEvent(eventStream.GameSessionId, seq, ts, isAttackerTeamId, attacker.Name, "SelfDamage"));
            }
        }

        var action = new GameAction
        {
            Id = Guid.NewGuid(),
            ActivationId = activation.Id,
            Type = ActionType.Shoot,
            ApCost = 1,
            TargetOperativeId = targetState.OperativeId,
            WeaponId = weapon.Id,
            AttackerDice = attackDice,
            DefenderDice = defenderDice,
            TargetInCover = inCover,
            IsObscured = isObscured,
            NormalHits = result.UnblockedNormals,
            CriticalHits = result.UnblockedCrits,
            NormalDamageDealt = result.UnblockedNormals * weapon.NormalDmg,
            CriticalDamageDealt = result.UnblockedCrits * weapon.CriticalDmg,
            CausedIncapacitation = causedIncap,
            SelfDamageDealt = selfDamage,
            StunApplied = stunApplied
        };
        await actionRepository.CreateAsync(action);
        eventStream?.Emit((seq, ts) => new ShootResolvedEvent(eventStream.GameSessionId, seq, ts, isAttackerTeamId, attacker.Name, targetOp.Name, result.TotalDamage, causedIncap));

        var note = await inputProvider.GetNarrativeNoteAsync();

        if (!string.IsNullOrWhiteSpace(note))
        {
            await actionRepository.UpdateNarrativeAsync(action.Id, note);
        }

        return new ShootSessionResult(causedIncap, result.TotalDamage, targetState.OperativeId);
    }
}
