using KillTeam.DataSlate.Domain.Engine.Input;
using KillTeam.DataSlate.Domain.Events;
using KillTeam.DataSlate.Domain.Models;
using KillTeam.DataSlate.Domain.Repositories;
using KillTeam.DataSlate.Domain.Services;

namespace KillTeam.DataSlate.Domain.Engine;

public class BlastEngine(
    IBlastInputProvider inputProvider,
    CombatResolutionService combatResolutionService,
    RerollEngine rerollEngine,
    IGameOperativeStateRepository stateRepository,
    IActionRepository actionRepository,
    IBlastTargetRepository blastTargetRepository)
{
    public async Task<BlastSessionResult> RunAsync(
        Operative attacker,
        GameOperativeState attackerState,
        Operative primaryTarget,
        GameOperativeState primaryTargetState,
        Weapon weapon,
        IReadOnlyList<GameOperativeState> allOperativeStates,
        IReadOnlyDictionary<Guid, Operative> allOperatives,
        Game game,
        TurningPoint tp,
        Activation activation,
        GameEventStream? eventStream = null)
    {
        var isAttackerTeamA = attacker.TeamId == game.Participant1.TeamId;

        var additionalCandidates = allOperativeStates
            .Where(s => s.OperativeId != primaryTarget.Id && !s.IsIncapacitated && allOperatives.ContainsKey(s.OperativeId))
            .ToList();

        var additionalTargetStates = new List<GameOperativeState>();

        if (additionalCandidates.Count > 0)
        {
            additionalTargetStates = await inputProvider.SelectAdditionalTargetsAsync(
                additionalCandidates, allOperatives, attacker.TeamId);
        }

        var allTargetStates = new List<GameOperativeState> { primaryTargetState }.Concat(additionalTargetStates).ToList();

        var friendlyCount = allTargetStates.Count(s =>
            allOperatives.TryGetValue(s.OperativeId, out var o) && o.TeamId == attacker.TeamId);

        if (friendlyCount > 0)
        {
            if (!await inputProvider.ConfirmFriendlyFireAsync(friendlyCount))
            {
                return new BlastSessionResult(false, 0);
            }
        }

        var attackDice = await inputProvider.RollOrEnterDiceAsync(weapon.Atk, $"{attacker.Name} attack dice (Attack: {weapon.Atk})");
        attackDice = await rerollEngine.ApplyAttackerRerollsAsync(
            attackDice, weapon.ParsedRules.ToList(), game.Id, isAttackerTeamA, attacker.Name);

        var effectiveHit = weapon.Hit;

        var action = new GameAction
        {
            Id = Guid.NewGuid(),
            ActivationId = activation.Id,
            Type = ActionType.Shoot,
            ApCost = 1,
            TargetOperativeId = primaryTarget.Id,
            WeaponId = weapon.Id,
            AttackerDice = attackDice
        };

        var anyIncapacitation = false;
        var totalDamage = 0;
        var primaryActionPersisted = false;

        for (var i = 0; i < allTargetStates.Count; i++)
        {
            var targetState = allTargetStates[i];

            if (!allOperatives.TryGetValue(targetState.OperativeId, out var targetOp))
            {
                continue;
            }

            var coverChoice = await inputProvider.GetCoverStatusAsync(targetOp.Name);
            var inCover = coverChoice == "In cover";
            var isObscured = coverChoice == "Obscured";

            var defenderDiceCount = await inputProvider.GetDefenceDiceCountAsync(targetOp.Name);

            int[] defenderDice = defenderDiceCount == 0
                ? []
                : await inputProvider.RollOrEnterDiceAsync(defenderDiceCount, $"{targetOp.Name} defence dice");

            var isDefenderTeamA = targetOp.TeamId == game.Participant1.TeamId;

            defenderDice = await rerollEngine.ApplyDefenderRerollAsync(defenderDice, game.Id, isDefenderTeamA, targetOp.Name);

            var ctx = new ShootContext(
                AttackDice: attackDice,
                DefenceDice: defenderDice,
                InCover: inCover,
                IsObscured: isObscured,
                HitThreshold: effectiveHit,
                SaveThreshold: targetOp.Save,
                NormalDmg: weapon.NormalDmg,
                CritDmg: weapon.CriticalDmg,
                WeaponRules: weapon.ParsedRules.ToList()
            );

            var result = combatResolutionService.ResolveShoot(ctx);
            var dmg = result.TotalDamage;

            eventStream?.Emit((seq, ts) => new ShootResultDisplayedEvent(eventStream.GameSessionId, seq, ts, attacker.TeamId, targetOp.Name, result.UnblockedCrits, result.UnblockedNormals, result.TotalDamage, inCover, isObscured));

            var newWounds = Math.Max(0, targetState.CurrentWounds - dmg);
            var causedIncap = newWounds <= 0 && !targetState.IsIncapacitated;

            targetState.CurrentWounds = newWounds;
            await stateRepository.UpdateWoundsAsync(targetState.Id, newWounds);

            if (causedIncap)
            {
                targetState.IsIncapacitated = true;
                await stateRepository.SetIncapacitatedAsync(targetState.Id, true);
                await stateRepository.UpdateGuardAsync(targetState.Id, false);
                targetState.IsOnGuard = false;
                anyIncapacitation = true;
                eventStream?.Emit((seq, ts) => new IncapacitationEvent(eventStream.GameSessionId, seq, ts, attacker.TeamId, targetOp.Name, "Shoot"));
            }

            totalDamage += dmg;

            if (!primaryActionPersisted)
            {
                action.DefenderDice = defenderDice;
                action.TargetInCover = inCover;
                action.IsObscured = isObscured;
                action.NormalHits = result.UnblockedNormals;
                action.CriticalHits = result.UnblockedCrits;
                action.NormalDamageDealt = result.UnblockedNormals * weapon.NormalDmg;
                action.CriticalDamageDealt = result.UnblockedCrits * weapon.CriticalDmg;
                action.CausedIncapacitation = causedIncap;
                await actionRepository.CreateAsync(action);
                primaryActionPersisted = true;
            }
            else
            {
                var blastTarget = new BlastTarget
                {
                    Id = Guid.NewGuid(),
                    ActionId = action.Id,
                    TargetOperativeId = targetState.OperativeId,
                    OperativeName = targetOp.Name,
                    DefenderDice = defenderDice,
                    NormalHits = result.UnblockedNormals,
                    CriticalHits = result.UnblockedCrits,
                    NormalDamageDealt = result.UnblockedNormals * weapon.NormalDmg,
                    CriticalDamageDealt = result.UnblockedCrits * weapon.CriticalDmg,
                    CausedIncapacitation = causedIncap
                };
                await blastTargetRepository.CreateAsync(blastTarget);
            }
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

        return new BlastSessionResult(anyIncapacitation, totalDamage);
    }
}
