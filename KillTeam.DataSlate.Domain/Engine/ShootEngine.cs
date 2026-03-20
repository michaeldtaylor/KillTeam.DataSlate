using KillTeam.DataSlate.Domain.Engine.Input;
using KillTeam.DataSlate.Domain.Engine.WeaponRules;
using KillTeam.DataSlate.Domain.Engine.WeaponRules.Context;
using KillTeam.DataSlate.Domain.Events;
using KillTeam.DataSlate.Domain.Models;
using KillTeam.DataSlate.Domain.Repositories;

namespace KillTeam.DataSlate.Domain.Engine;

public class ShootEngine(
    IShootInputProvider inputProvider,
    RerollEngine rerollEngine,
    AoEEngine aoeEngine,
    IActionRepository actionRepository,
    ShootWeaponRulePipeline shootWeaponRulePipeline)
{
    public async Task<ShootResult> RunAsync(
        GameContext context,
        Activation activation,
        OperativeContext attacker,
        bool hasMovedNonDash = false)
    {
        var isAttackerTeam1 = attacker.Operative.TeamId == context.Game.Participant1.Team.Id;
        var attackerTeamId = attacker.Operative.TeamId;

        var isOnConceal = attacker.State.Order == Order.Conceal;

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
                    "No valid targets available.")) ?? ValueTask.CompletedTask);

            return new ShootResult(false, 0, null);
        }

        OperativeContext target;

        if (targets.Length == 1)
        {
            target = targets[0];

            await (context.EventStream?.EmitAsync((gameSessionId, sequenceNumber, timestamp) =>
                new ShootTargetSelectedEvent(
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
        var targetDistance = await inputProvider.GetTargetDistanceAsync(target.Operative.Name);

        if (targetDistance <= 1)
        {
            await (context.EventStream?.EmitAsync((gameSessionId, sequenceNumber, timestamp) =>
                new CombatWarningEvent(
                    gameSessionId,
                    sequenceNumber,
                    timestamp,
                    attackerTeamId,
                    CombatWarningKind.NoWeaponsAvailable,
                    "Cannot shoot — operative is within Engagement Range of an enemy.")) ?? ValueTask.CompletedTask);

            return new ShootResult(false, 0, null);
        }

        var availabilityContext = new AvailabilityContext(hasMovedNonDash, isOnConceal, targetDistance);
        var rangedWeapons = shootWeaponRulePipeline
            .FilterAvailableWeapons(attacker.Operative.Weapons.Where(w => w.Type == WeaponType.Ranged).ToList(), availabilityContext)
            .Where(w => inputProvider.HasRemainingUses(w))
            .ToList();

        if (rangedWeapons.Count == 0)
        {
            var noWeaponsMessage = isOnConceal
                ? "Cannot shoot — operative is on a Conceal order and no weapons have the Silent rule."
                : $"No ranged weapons can reach {target.Operative.Name} at {targetDistance}\".";

            await (context.EventStream?.EmitAsync((gameSessionId, sequenceNumber, timestamp) =>
                new CombatWarningEvent(
                    gameSessionId,
                    sequenceNumber,
                    timestamp,
                    attackerTeamId,
                    CombatWarningKind.NoWeaponsAvailable,
                    noWeaponsMessage)) ?? ValueTask.CompletedTask);

            return new ShootResult(false, 0, null);
        }

        Weapon weapon;

        if (rangedWeapons.Count == 1)
        {
            weapon = rangedWeapons[0];
            await (context.EventStream?.EmitAsync((gameSessionId, sequenceNumber, timestamp) =>
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

        if (shootWeaponRulePipeline.RequiresAoEResolution(weapon))
        {
            var aoeResult = await aoeEngine.RunAsync(
                context,
                activation,
                attacker,
                target,
                weapon);

            return new ShootResult(aoeResult.AnyIncapacitation, aoeResult.TotalDamage, target.Operative.Id);
        }

        // ── Cover status ──────────────────────────────────────────────────────────
        var coverContext = new CoverContext
        {
            Attacker = attacker.Operative,
            Target = target.Operative,
            InputProvider = inputProvider,
            EventStream = context.EventStream,
        };

        await shootWeaponRulePipeline.DetermineCoverAsync(weapon, coverContext);

        var inCover = coverContext.InCover;
        var isObscured = coverContext.IsObscured;

        var fightAssist = await inputProvider.GetFriendlyAllyCountAsync();

        var attackerDice = await inputProvider.RollOrEnterDiceAsync(weapon.Atk, $"{attacker.Operative.Name} attack dice (Attack: {weapon.Atk})", attacker.Operative.Name, "Attacker", "Shoot", attackerTeamId, context.EventStream);

        attackerDice = await rerollEngine.ApplyAttackerRerollsAsync(
            attackerDice,
            weapon.Rules.ToList(),
            context.Game.Id,
            isAttackerTeam1,
            attacker.Operative.Name,
            attackerTeamId,
            context.EventStream);

        var targetDiceCount = target.Operative.Defence + target.State.DefenceDiceModifier;

        if (inCover)
        {
            await (context.EventStream?.EmitAsync((gameSessionId, sequenceNumber, timestamp) =>
                new CoverSaveNotifiedEvent(
                    gameSessionId,
                    sequenceNumber,
                    timestamp,
                    attackerTeamId,
                    target.Operative.Name)) ?? ValueTask.CompletedTask);
        }

        var targetDice = targetDiceCount == 0
            ? []
            : await inputProvider.RollOrEnterDiceAsync(targetDiceCount, $"{target.Operative.Name} defence dice", target.Operative.Name, "Target", "Shoot", targetTeamId, context.EventStream);

        var isTargetTeam1 = target.Operative.TeamId == context.Game.Participant1.Team.Id;

        targetDice = await rerollEngine.ApplyTargetRerollAsync(targetDice, context.Game.Id, isTargetTeam1, target.Operative.Name, targetTeamId, context.EventStream);

        var effectiveSave = inCover ? target.Operative.Save - 1 : target.Operative.Save;
        var attackSnapshots = attackerDice.Select(d => new FightDieSnapshot(d >= 6 ? DieResult.Crit : d >= weapon.Hit ? DieResult.Hit : DieResult.Miss, d)).ToList();
        var defenceSnapshots = targetDice.Select(d => new FightDieSnapshot(d >= effectiveSave ? DieResult.Save : DieResult.Fail, d)).ToList();

        await (context.EventStream?.EmitAsync((gameSessionId, sequenceNumber, timestamp) =>
            new ShootPoolsDisplayedEvent(
                gameSessionId,
                sequenceNumber,
                timestamp,
                attackerTeamId,
                attacker.Operative.Name,
                attacker.State.CurrentWounds,
                attacker.Operative.Wounds,
                attackSnapshots,
                target.Operative.Name,
                target.State.CurrentWounds,
                target.Operative.Wounds,
                defenceSnapshots)) ?? ValueTask.CompletedTask);

        var ShootResolutionContext = new ShootResolutionContext(
            attackerDice,
            targetDice,
            inCover,
            isObscured,
            weapon.Hit,
            target.Operative.Save,
            weapon.NormalDmg,
            weapon.CriticalDmg,
            fightAssist
        );

        var result = await shootWeaponRulePipeline.ResolveShootAsync(weapon, ShootResolutionContext);

        var newWounds = Math.Max(0, target.State.CurrentWounds - result.TotalDamage);

        await (context.EventStream?.EmitAsync((gameSessionId, sequenceNumber, timestamp) =>
            new ShootResultDisplayedEvent(
                gameSessionId,
                sequenceNumber,
                timestamp,
                attackerTeamId,
                attacker.Operative.Name,
                attacker.State.CurrentWounds,
                attacker.Operative.Wounds,
                target.Operative.Name,
                result.UnblockedCrits,
                result.UnblockedNormals,
                result.TotalDamage,
                newWounds,
                target.Operative.Wounds,
                inCover,
                isObscured)) ?? ValueTask.CompletedTask);

        var causedIncap = newWounds <= 0 && !target.State.IsIncapacitated;

        target.State.CurrentWounds = newWounds;

        await (context.EventStream?.EmitAsync((gameSessionId, sequenceNumber, timestamp) =>
            new OperativeWoundsChangedEvent(
                gameSessionId,
                sequenceNumber,
                timestamp,
                targetTeamId,
                target.State.Id,
                newWounds)) ?? ValueTask.CompletedTask);

        if (causedIncap)
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
                    "Shoot")) ?? ValueTask.CompletedTask);
        }

        var stunApplied = result.StunApplied;

        if (stunApplied)
        {
            target.State.AplModifier -= 1;

            await (context.EventStream?.EmitAsync((gameSessionId, sequenceNumber, timestamp) =>
                new OperativeAplModifiedEvent(
                    gameSessionId,
                    sequenceNumber,
                    timestamp,
                    targetTeamId,
                    target.State.Id,
                    target.State.AplModifier)) ?? ValueTask.CompletedTask);

            await (context.EventStream?.EmitAsync((gameSessionId, sequenceNumber, timestamp) =>
                new StunAppliedEvent(
                    gameSessionId,
                    sequenceNumber,
                    timestamp,
                    attackerTeamId,
                    target.Operative.Name,
                    1)) ?? ValueTask.CompletedTask);
        }

        var effectContext = new EffectsContext
        {
            Attacker = attacker,
            ResolutionResult = result,
            EventStream = context.EventStream,
        };

        await shootWeaponRulePipeline.ApplyEffectsAsync(weapon, effectContext);

        var selfDamage = effectContext.SelfDamageApplied;

        if (selfDamage > 0)
        {
            await (context.EventStream?.EmitAsync((gameSessionId, sequenceNumber, timestamp) =>
                new OperativeWoundsChangedEvent(
                    gameSessionId,
                    sequenceNumber,
                    timestamp,
                    attackerTeamId,
                    attacker.State.Id,
                    attacker.State.CurrentWounds)) ?? ValueTask.CompletedTask);

            if (effectContext.AttackerBecameIncapacitated)
            {
                await (context.EventStream?.EmitAsync((gameSessionId, sequenceNumber, timestamp) =>
                    new OperativeIncapacitatedEvent(
                        gameSessionId,
                        sequenceNumber,
                        timestamp,
                        attackerTeamId,
                        attacker.State.Id)) ?? ValueTask.CompletedTask);
            }
        }

        var action = new GameAction
        {
            Id = Guid.NewGuid(),
            ActivationId = activation.Id,
            Type = ActionType.Shoot,
            ApCost = 1,
            TargetOperativeId = target.Operative.Id,
            WeaponId = weapon.Id,
            AttackerDice = attackerDice,
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

        await (context.EventStream?.EmitAsync((gameSessionId, sequenceNumber, timestamp) =>
            new ShootResolvedEvent(
                gameSessionId,
                sequenceNumber,
                timestamp,
                attackerTeamId,
                attacker.Operative.Name,
                target.Operative.Name,
                result.TotalDamage,
                causedIncap)) ?? ValueTask.CompletedTask);

        var note = await inputProvider.GetNarrativeNoteAsync();

        if (!string.IsNullOrWhiteSpace(note))
        {
            await actionRepository.UpdateNarrativeAsync(action.Id, note);
        }

        return new ShootResult(causedIncap, result.TotalDamage, target.Operative.Id);
    }
}
