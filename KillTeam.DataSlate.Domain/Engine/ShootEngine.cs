using KillTeam.DataSlate.Domain.Engine.Input;
using KillTeam.DataSlate.Domain.Engine.WeaponRules;
using KillTeam.DataSlate.Domain.Engine.WeaponRules.Context;
using KillTeam.DataSlate.Domain.Events;
using KillTeam.DataSlate.Domain.Models;
using KillTeam.DataSlate.Domain.Repositories;
using KillTeam.DataSlate.Domain.Services;

namespace KillTeam.DataSlate.Domain.Engine;

public class ShootEngine(
    IShootInputProvider inputProvider,
    RerollEngine rerollEngine,
    BlastEngine blastEngine,
    IActionRepository actionRepository,
    ShootWeaponRuleApplicator weaponRuleApplicator)
{
    public async Task<ShootSessionResult> RunAsync(
        Game game,
        Activation activation,
        Operative attacker,
        GameOperativeState attackerState,
        IReadOnlyList<GameOperativeState> allOperativeStates,
        IReadOnlyDictionary<Guid, Operative> allOperatives,
        bool hasMovedNonDash = false,
        GameEventStream? eventStream = null)
    {
        var isAttackerTeam1 = attacker.TeamId == game.Participant1.TeamId;
        var attackerTeamId = attacker.TeamId;

        // ── Conceal order check (Silent rule) ────────────────────────────────────
        var isOnConceal = await inputProvider.IsOnConcealOrderAsync();

        var targetOperativeStates = ActionHelpers.GetActiveTargetOperativeStates(attacker, allOperativeStates, allOperatives);

        if (targetOperativeStates.Length == 0)
        {
            await (eventStream?.EmitAsync((gameSessionId, sequenceNumber, timestamp) =>
                new CombatWarningEvent(
                    gameSessionId,
                    sequenceNumber,
                    timestamp,
                    attackerTeamId,
                    CombatWarningKind.NoValidTargets,
                    "No valid targets available.")) ?? ValueTask.CompletedTask);

            return new ShootSessionResult(false, 0, null);
        }

        GameOperativeState targetOperativeState;

        if (targetOperativeStates.Length == 1)
        {
            targetOperativeState = targetOperativeStates[0];

            if (allOperatives.TryGetValue(targetOperativeState.OperativeId, out var autoTarget))
            {
                await (eventStream?.EmitAsync((gameSessionId, sequenceNumber, timestamp) =>
                    new ShootTargetSelectedEvent(
                        gameSessionId,
                        sequenceNumber,
                        timestamp,
                        attackerTeamId,
                        autoTarget.Name,
                        targetOperativeState.CurrentWounds,
                        autoTarget.Wounds,
                        true)) ?? ValueTask.CompletedTask);
            }
        }
        else
        {
            targetOperativeState = await inputProvider.SelectTargetAsync(targetOperativeStates, allOperatives);
        }

        if (!allOperatives.TryGetValue(targetOperativeState.OperativeId, out var target))
        {
            await (eventStream?.EmitAsync((gameSessionId, sequenceNumber, timestamp) =>
                new CombatWarningEvent(
                    gameSessionId,
                    sequenceNumber,
                    timestamp,
                    attackerTeamId,
                    CombatWarningKind.TargetNotFound,
                    "Target operative not found.")) ?? ValueTask.CompletedTask);

            return new ShootSessionResult(false, 0, null);
        }

        var targetTeamId = target.TeamId;
        var targetDistance = await inputProvider.GetTargetDistanceAsync(target.Name);

        if (targetDistance <= 1)
        {
            await (eventStream?.EmitAsync((gameSessionId, sequenceNumber, timestamp) =>
                new CombatWarningEvent(
                    gameSessionId,
                    sequenceNumber,
                    timestamp,
                    attackerTeamId,
                    CombatWarningKind.NoWeaponsAvailable,
                    "Cannot shoot — operative is within Engagement Range of an enemy.")) ?? ValueTask.CompletedTask);

            return new ShootSessionResult(false, 0, null);
        }

        var availabilityContext = new ShootWeaponAvailabilityContext(hasMovedNonDash, isOnConceal, targetDistance);
        var rangedWeapons = weaponRuleApplicator
            .FilterAvailableWeapons(attacker.Weapons.Where(w => w.Type == WeaponType.Ranged).ToList(), availabilityContext)
            .Where(w => inputProvider.HasRemainingUses(w))
            .ToList();

        if (rangedWeapons.Count == 0)
        {
            var noWeaponsMsg = isOnConceal
                ? "Cannot shoot — operative is on a Conceal order and no weapons have the Silent rule."
                : $"No ranged weapons can reach {target.Name} at {targetDistance}\".";

            await (eventStream?.EmitAsync((gameSessionId, sequenceNumber, timestamp) =>
                new CombatWarningEvent(
                    gameSessionId,
                    sequenceNumber,
                    timestamp,
                    attackerTeamId,
                    CombatWarningKind.NoWeaponsAvailable,
                    noWeaponsMsg)) ?? ValueTask.CompletedTask);

            return new ShootSessionResult(false, 0, null);
        }

        Weapon weapon;

        if (rangedWeapons.Count == 1)
        {
            weapon = rangedWeapons[0];
            await (eventStream?.EmitAsync((gameSessionId, sequenceNumber, timestamp) =>
                new WeaponSelectedEvent(
                    gameSessionId,
                    sequenceNumber,
                    timestamp,
                    attackerTeamId,
                    weapon.Name,
                    weapon.Atk,
                    weapon.Hit,
                    weapon.NormalDmg,
                    weapon.CriticalDmg,
                    "Attacker",
                    true,
                    false,
                    weapon.Hit)) ?? ValueTask.CompletedTask);
        }
        else
        {
            weapon = await inputProvider.SelectWeaponAsync(rangedWeapons, hasMovedNonDash);
        }

        // ── Record Limited weapon use ─────────────────────────────────────────────
        inputProvider.RecordWeaponFired(weapon);

        if (weaponRuleApplicator.RequiresAoEResolution(weapon))
        {
            var blastResult = await blastEngine.RunAsync(
                attacker,
                attackerState,
                target,
                targetOperativeState,
                weapon,
                allOperativeStates,
                allOperatives,
                game,
                activation,
                eventStream);

            return new ShootSessionResult(blastResult.AnyIncapacitation, blastResult.TotalDamage, targetOperativeState.OperativeId);
        }

        // ── Cover status ──────────────────────────────────────────────────────────
        var coverContext = new WeaponCoverContext
        {
            Attacker = attacker,
            Target = target,
            InputProvider = inputProvider,
            EventStream = eventStream,
        };

        await weaponRuleApplicator.DetermineCoverAsync(weapon, coverContext);

        var inCover = coverContext.InCover;
        var isObscured = coverContext.IsObscured;

        var fightAssist = await inputProvider.GetFriendlyAllyCountAsync();

        var attackDice = await inputProvider.RollOrEnterDiceAsync(weapon.Atk, $"{attacker.Name} attack dice (Attack: {weapon.Atk})", attacker.Name, "Attacker", "Shoot", attackerTeamId, eventStream);

        attackDice = await rerollEngine.ApplyAttackerRerollsAsync(
            attackDice,
            weapon.Rules.ToList(),
            game.Id,
            isAttackerTeam1,
            attacker.Name,
            attackerTeamId,
            eventStream);

        var targetDiceCount = target.Defence + targetOperativeState.DefenceDiceModifier;

        if (inCover)
        {
            await (eventStream?.EmitAsync((gameSessionId, sequenceNumber, timestamp) =>
                new CoverSaveNotifiedEvent(
                    gameSessionId,
                    sequenceNumber,
                    timestamp,
                    attackerTeamId,
                    target.Name)) ?? ValueTask.CompletedTask);
        }

        var targetDice = targetDiceCount == 0
            ? []
            : await inputProvider.RollOrEnterDiceAsync(targetDiceCount, $"{target.Name} defence dice", target.Name, "Target", "Shoot", targetTeamId, eventStream);

        var isTargetTeam1 = target.TeamId == game.Participant1.TeamId;

        targetDice = await rerollEngine.ApplyTargetRerollAsync(targetDice, game.Id, isTargetTeam1, target.Name, targetTeamId, eventStream);

        var effectiveSave = inCover ? target.Save - 1 : target.Save;
        var attackSnapshots = attackDice.Select(d => new FightDieSnapshot(d >= 6 ? DieResult.Crit : d >= weapon.Hit ? DieResult.Hit : DieResult.Miss, d)).ToList();
        var defenceSnapshots = targetDice.Select(d => new FightDieSnapshot(d >= effectiveSave ? DieResult.Save : DieResult.Fail, d)).ToList();

        await (eventStream?.EmitAsync((gameSessionId, sequenceNumber, timestamp) =>
            new ShootPoolsDisplayedEvent(
                gameSessionId,
                sequenceNumber,
                timestamp,
                attackerTeamId,
                attacker.Name,
                attackerState.CurrentWounds,
                attacker.Wounds,
                attackSnapshots,
                target.Name,
                targetOperativeState.CurrentWounds,
                target.Wounds,
                defenceSnapshots)) ?? ValueTask.CompletedTask);

        var shootContext = new ShootContext(
            AttackerDice: attackDice,
            TargetDice: targetDice,
            InCover: inCover,
            IsObscured: isObscured,
            HitThreshold: weapon.Hit,
            SaveThreshold: target.Save,
            NormalDmg: weapon.NormalDmg,
            CritDmg: weapon.CriticalDmg,
            FightAssistBonus: fightAssist
        );

        var result = await weaponRuleApplicator.ResolveShootAsync(weapon, shootContext);

        var newWounds = Math.Max(0, targetOperativeState.CurrentWounds - result.TotalDamage);

        await (eventStream?.EmitAsync((gameSessionId, sequenceNumber, timestamp) =>
            new ShootResultDisplayedEvent(
                gameSessionId,
                sequenceNumber,
                timestamp,
                attackerTeamId,
                attacker.Name,
                attackerState.CurrentWounds,
                attacker.Wounds,
                target.Name,
                result.UnblockedCrits,
                result.UnblockedNormals,
                result.TotalDamage,
                newWounds,
                target.Wounds,
                inCover,
                isObscured)) ?? ValueTask.CompletedTask);

        var causedIncap = newWounds <= 0 && !targetOperativeState.IsIncapacitated;

        targetOperativeState.CurrentWounds = newWounds;

        await (eventStream?.EmitAsync((gameSessionId, sequenceNumber, timestamp) =>
            new OperativeWoundsChangedEvent(
                gameSessionId,
                sequenceNumber,
                timestamp,
                targetTeamId,
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
                    targetTeamId,
                    targetOperativeState.Id)) ?? ValueTask.CompletedTask);

            await (eventStream?.EmitAsync((gameSessionId, sequenceNumber, timestamp) =>
                new OperativeGuardClearedEvent(
                    gameSessionId,
                    sequenceNumber,
                    timestamp,
                    targetTeamId,
                    targetOperativeState.Id)) ?? ValueTask.CompletedTask);

            await (eventStream?.EmitAsync((gameSessionId, sequenceNumber, timestamp) =>
                new IncapacitationEvent(
                    gameSessionId,
                    sequenceNumber,
                    timestamp,
                    attackerTeamId,
                    target.Name,
                    "Shoot")) ?? ValueTask.CompletedTask);
        }

        var stunApplied = result.StunApplied;

        if (stunApplied)
        {
            targetOperativeState.AplModifier -= 1;

            await (eventStream?.EmitAsync((gameSessionId, sequenceNumber, timestamp) =>
                new OperativeAplModifiedEvent(
                    gameSessionId,
                    sequenceNumber,
                    timestamp,
                    targetTeamId,
                    targetOperativeState.Id,
                    targetOperativeState.AplModifier)) ?? ValueTask.CompletedTask);

            await (eventStream?.EmitAsync((gameSessionId, sequenceNumber, timestamp) =>
                new StunAppliedEvent(
                    gameSessionId,
                    sequenceNumber,
                    timestamp,
                    attackerTeamId,
                    target.Name,
                    1)) ?? ValueTask.CompletedTask);
        }

        var effectContext = new WeaponEffectContext
        {
            Attacker = attacker,
            AttackerState = attackerState,
            ResolutionResult = result,
            EventStream = eventStream,
        };

        await weaponRuleApplicator.ApplyEffectsAsync(weapon, effectContext);

        var selfDamage = effectContext.SelfDamageApplied;

        if (selfDamage > 0)
        {
            await (eventStream?.EmitAsync((gameSessionId, sequenceNumber, timestamp) =>
                new OperativeWoundsChangedEvent(
                    gameSessionId,
                    sequenceNumber,
                    timestamp,
                    attackerTeamId,
                    attackerState.Id,
                    attackerState.CurrentWounds)) ?? ValueTask.CompletedTask);

            if (effectContext.AttackerBecameIncapacitated)
            {
                await (eventStream?.EmitAsync((gameSessionId, sequenceNumber, timestamp) =>
                    new OperativeIncapacitatedEvent(
                        gameSessionId,
                        sequenceNumber,
                        timestamp,
                        attackerTeamId,
                        attackerState.Id)) ?? ValueTask.CompletedTask);
            }
        }

        var action = new GameAction
        {
            Id = Guid.NewGuid(),
            ActivationId = activation.Id,
            Type = ActionType.Shoot,
            ApCost = 1,
            TargetOperativeId = targetOperativeState.OperativeId,
            WeaponId = weapon.Id,
            AttackerDice = attackDice,
            TargetDice = targetDice,
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

        await (eventStream?.EmitAsync((gameSessionId, sequenceNumber, timestamp) =>
            new ShootResolvedEvent(
                gameSessionId,
                sequenceNumber,
                timestamp,
                attackerTeamId,
                attacker.Name,
                target.Name,
                result.TotalDamage,
                causedIncap)) ?? ValueTask.CompletedTask);

        var note = await inputProvider.GetNarrativeNoteAsync();

        if (!string.IsNullOrWhiteSpace(note))
        {
            await actionRepository.UpdateNarrativeAsync(action.Id, note);
        }

        return new ShootSessionResult(causedIncap, result.TotalDamage, targetOperativeState.OperativeId);
    }
}
