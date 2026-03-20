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
        OperativeContext attacker)
    {
        var isAttackerTeam1 = attacker.Operative.TeamId == context.Game.Participant1.Team.Id;
        var attackerTeamId = attacker.Operative.TeamId;

        var targets = ActionHelpers.GetTargets(attacker, context.Operatives);

        if (targets.Length == 0)
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

        OperativeContext target;

        if (targets.Length == 1)
        {
            target = targets[0];

            await (context.EventStream?.EmitAsync((gameSessionId, sequenceNumber, timestamp) =>
                new FightTargetSelectedEvent(
                    gameSessionId,
                    sequenceNumber,
                    timestamp,
                    attackerTeamId,
                    target.Operative.Name,
                    target.State.CurrentWounds,
                    target.Operative.Wounds,
                    true)) ?? ValueTask.CompletedTask);
        }
        else
        {
            target = await inputProvider.SelectTargetAsync(targets);
        }

        var targetTeamId = target.Operative.TeamId;
        var isTargetTeam1 = target.Operative.TeamId == context.Game.Participant1.Team.Id;

        var attackerMeleeWeapons = attacker.Operative.Weapons.Where(w => w.Type == WeaponType.Melee).ToList();

        if (attackerMeleeWeapons.Count == 0)
        {
            await (context.EventStream?.EmitAsync((gameSessionId, sequenceNumber, timestamp) =>
                new CombatWarningEvent(
                    gameSessionId,
                    sequenceNumber,
                    timestamp,
                    attackerTeamId,
                    CombatWarningKind.NoWeaponsAvailable,
                    $"{attacker.Operative.Name} has no melee weapons!")) ?? ValueTask.CompletedTask);

            return new FightResult(false, false, 0, 0, target.Operative.Id);
        }

        var attackerIsInjured = attacker.State.CurrentWounds < attacker.Operative.Wounds / 2;

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

        var targetMeleeWeapons = target.Operative.Weapons
            .Where(w => w.Type == WeaponType.Melee)
            .ToList();

        Weapon? targetWeapon = null;

        var targetEffectiveHit = 3;

        switch (targetMeleeWeapons.Count)
        {
            case 1:
            {
                targetWeapon = targetMeleeWeapons[0];

                var targetIsInjured = target.State.CurrentWounds < target.Operative.Wounds / 2;

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
                var targetIsInjured = target.State.CurrentWounds < target.Operative.Wounds / 2;

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
                        target.Operative.Name)) ?? ValueTask.CompletedTask);
                break;
        }

        var fightAssist = await inputProvider.GetFightAssistCountAsync();

        attackerEffectiveHit = Math.Max(2, attackerEffectiveHit - fightAssist);

        var attackerRolls = await inputProvider.RollOrEnterDiceAsync(attackerWeapon.Atk, $"{attacker.Operative.Name} attack dice (Attack: {attackerWeapon.Atk})", attacker.Operative.Name, "Attacker", "Fight", attackerTeamId, context.EventStream);

        attackerRolls = await rerollEngine.ApplyAttackerRerollsAsync(
            attackerRolls,
            attackerWeapon.Rules.ToList(),
            context.Game.Id,
            isAttackerTeam1,
            attacker.Operative.Name,
            attackerTeamId,
            context.EventStream);

        var targetAttackCount = targetWeapon?.Atk ?? 0;

        int[] targetRolls = [];

        if (targetAttackCount > 0)
        {
            targetRolls = await inputProvider.RollOrEnterDiceAsync(targetAttackCount, $"{target.Operative.Name} fight-back dice (Attack: {targetAttackCount})", target.Operative.Name, "Target", "Fight", targetTeamId, context.EventStream);
            targetRolls = await rerollEngine.ApplyTargetRerollAsync(targetRolls, context.Game.Id, isTargetTeam1, target.Operative.Name, targetTeamId, context.EventStream);
        }

        var attackerPool = FightResolution.CalculateDice(attackerRolls, attackerEffectiveHit);
        var targetPool = targetWeapon is not null
            ? FightResolution.CalculateDice(targetRolls, targetEffectiveHit)
            : new FightDicePool([]);

        var fightSetupContext = new FightSetupContext
        {
            Attacker = attacker.Operative,
            Target = target.Operative,
            AttackerPool = attackerPool,
            TargetPool = targetPool,
            EventStream = context.EventStream,
        };

        await fightWeaponRulePipeline.SetupAsync(attackerWeapon, fightSetupContext);

        attackerPool = fightSetupContext.AttackerPool;
        targetPool = fightSetupContext.TargetPool;

        var fightResolutionContext = new FightResolutionContext
        {
            Attacker = attacker,
            Target = target,
            AttackerWeapon = attackerWeapon,
            TargetWeapon = targetWeapon,
            AttackerPool = attackerPool,
            TargetPool = targetPool,
            BlockRestrictedToCrits = fightSetupContext.BlockRestrictedToCrits,
            InputProvider = inputProvider,
            EventStream = context.EventStream,
        };

        var loopResult = await fightWeaponRulePipeline.ResolveFightAsync(fightResolutionContext);

        var attackerCausedIncapacitation = loopResult.TargetCurrentWounds <= 0 && !target.State.IsIncapacitated;
        var targetCausedIncapacitation = loopResult.AttackerCurrentWounds <= 0 && !attacker.State.IsIncapacitated;

        attacker.State.CurrentWounds = loopResult.AttackerCurrentWounds;
        target.State.CurrentWounds = loopResult.TargetCurrentWounds;

        await (context.EventStream?.EmitAsync((gameSessionId, sequenceNumber, timestamp) =>
            new OperativeWoundsChangedEvent(
                gameSessionId,
                sequenceNumber,
                timestamp,
                attackerTeamId,
                attacker.State.Id,
                loopResult.AttackerCurrentWounds)) ?? ValueTask.CompletedTask);

        await (context.EventStream?.EmitAsync((gameSessionId, sequenceNumber, timestamp) =>
            new OperativeWoundsChangedEvent(
                gameSessionId,
                sequenceNumber,
                timestamp,
                targetTeamId,
                target.State.Id,
                loopResult.TargetCurrentWounds)) ?? ValueTask.CompletedTask);

        if (attackerCausedIncapacitation)
        {
            target.State.IsIncapacitated = true;
            target.State.IsOnGuard = false;

            await (context.EventStream?.EmitAsync((gameSessionId, sequenceNumber, timestamp) =>
                new OperativeIncapacitatedEvent(
                    gameSessionId,
                    sequenceNumber,
                    timestamp,
                    targetTeamId,
                    target.State.Id)) ?? ValueTask.CompletedTask);

            await (context.EventStream?.EmitAsync((gameSessionId, sequenceNumber, timestamp) =>
                new OperativeGuardClearedEvent(
                    gameSessionId,
                    sequenceNumber,
                    timestamp,
                    targetTeamId,
                    target.State.Id)) ?? ValueTask.CompletedTask);

            await (context.EventStream?.EmitAsync((gameSessionId, sequenceNumber, timestamp) =>
                new IncapacitationEvent(
                    gameSessionId,
                    sequenceNumber,
                    timestamp,
                    attackerTeamId,
                    target.Operative.Name,
                    "Fight")) ?? ValueTask.CompletedTask);
        }

        if (targetCausedIncapacitation)
        {
            attacker.State.IsIncapacitated = true;
            attacker.State.IsOnGuard = false;

            await (context.EventStream?.EmitAsync((gameSessionId, sequenceNumber, timestamp) =>
                new OperativeIncapacitatedEvent(
                    gameSessionId,
                    sequenceNumber,
                    timestamp,
                    attackerTeamId,
                    attacker.State.Id)) ?? ValueTask.CompletedTask);

            await (context.EventStream?.EmitAsync((gameSessionId, sequenceNumber, timestamp) =>
                new OperativeGuardClearedEvent(
                    gameSessionId,
                    sequenceNumber,
                    timestamp,
                    attackerTeamId,
                    attacker.State.Id)) ?? ValueTask.CompletedTask);

            await (context.EventStream?.EmitAsync((gameSessionId, sequenceNumber, timestamp) =>
                new IncapacitationEvent(
                    gameSessionId,
                    sequenceNumber,
                    timestamp,
                    targetTeamId,
                    attacker.Operative.Name,
                    "Fight")) ?? ValueTask.CompletedTask);
        }

        var action = new GameAction
        {
            Id = Guid.NewGuid(),
            ActivationId = activation.Id,
            Type = ActionType.Fight,
            ApCost = 1,
            TargetOperativeId = target.Operative.Id,
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
                attacker.Operative.Name,
                target.Operative.Name,
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
            target.Operative.Id);
    }
}
