using KillTeam.DataSlate.Domain.Engine.WeaponRules.Context;
using KillTeam.DataSlate.Domain.Engine.WeaponRules.Visitors;
using KillTeam.DataSlate.Domain.Events;
using KillTeam.DataSlate.Domain.Models;

namespace KillTeam.DataSlate.Domain.Engine.WeaponRules;

public sealed class FightWeaponRulePipeline
{
    private readonly IReadOnlyList<IFightWeaponRuleVisitor> _handlers =
    [
        new BrutalRuleVisitor(),
        new ShockRuleVisitor(),
    ];

    public async Task SetupAsync(Weapon weapon, FightSetupContext context)
    {
        foreach (var handler in _handlers)
        {
            await handler.SetupAsync(weapon, context);
        }
    }

    public async Task<FightLoopResult> ResolveFightAsync(FightResolutionContext context)
    {
        var attackerPool = context.AttackerPool;
        var targetPool = context.TargetPool;
        var attackerCurrentWounds = context.Attacker.State.CurrentWounds;
        var targetCurrentWounds = context.Target.State.CurrentWounds;
        var attackerTeamId = context.Attacker.Operative.TeamId;
        var totalAttackerDamageDealt = 0;
        var totalTargetDamageDealt = 0;
        var turnOrder = DieOwner.Attacker;

        while (attackerPool.Remaining.Count > 0 || targetPool.Remaining.Count > 0)
        {
            var fightTurn = FightTurn.Resolve(
                turnOrder,
                attackerPool,
                targetPool,
                context.Attacker.Operative,
                context.Target.Operative,
                context.AttackerWeapon,
                context.TargetWeapon,
                context.BlockRestrictedToCrits);

            var (actingPool, opposingPool, currentTurn, actingOperative, opposingOperative, actingWeapon, restrictBlocksToCrits) = fightTurn;

            var attackerWoundsNow = attackerCurrentWounds;
            var targetWoundsNow = targetCurrentWounds;
            var attackerPoolNow = attackerPool.Remaining.Select(d => new FightDieSnapshot(d.Result, d.RolledValue)).ToList();
            var targetPoolNow = targetPool.Remaining.Select(d => new FightDieSnapshot(d.Result, d.RolledValue)).ToList();

            await (context.EventStream?.EmitAsync((gameSessionId, sequenceNumber, timestamp) =>
                new FightPoolsDisplayedEvent(
                    gameSessionId,
                    sequenceNumber,
                    timestamp,
                    attackerTeamId,
                    context.Attacker.Operative.Name,
                    attackerWoundsNow,
                    context.Attacker.Operative.Wounds,
                    attackerPoolNow,
                    context.Target.Operative.Name,
                    targetWoundsNow,
                    context.Target.Operative.Wounds,
                    targetPoolNow)) ?? ValueTask.CompletedTask);

            var actions = FightResolution.GetAvailableActions(actingPool, opposingPool, restrictBlocksToCrits);

            var uniqueActions = actions
                .GroupBy(a => (a.Type, a.Die.Result, TargetResult: a.TargetDie?.Result))
                .Select(g => g.First())
                .ToList();

            var opposingHasCrits = opposingPool.Remaining.Any(d => d.Result == DieResult.Crit);

            if (opposingHasCrits)
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

            var actionChoice = await context.InputProvider.SelectActionAsync(uniqueActions, actingOperative.Name);

            if (actionChoice.Type == FightActionType.Strike)
            {
                var damageDealt = FightResolution.ApplyStrike(actionChoice.Die, actingWeapon.NormalDmg, actingWeapon.CriticalDmg);

                await (context.EventStream?.EmitAsync((gameSessionId, sequenceNumber, timestamp) =>
                    new FightStrikeResolvedEvent(
                        gameSessionId,
                        sequenceNumber,
                        timestamp,
                        actingOperative.TeamId,
                        actingOperative.Name,
                        opposingOperative.Name,
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

                actingPool = new FightDicePool(actingPool.Remaining.Where(d => d.Id != actionChoice.Die.Id).ToList());
            }
            else
            {
                await (context.EventStream?.EmitAsync((gameSessionId, sequenceNumber, timestamp) =>
                    new FightBlockResolvedEvent(
                        gameSessionId,
                        sequenceNumber,
                        timestamp,
                        actingOperative.TeamId,
                        actingOperative.Name,
                        actionChoice.Die.RolledValue,
                        actionChoice.Die.Result,
                        actionChoice.TargetDie!.RolledValue,
                        actionChoice.TargetDie!.Result)) ?? ValueTask.CompletedTask);

                (actingPool, opposingPool) = FightResolution.ApplySingleBlock(
                    actionChoice.Die,
                    actionChoice.TargetDie!,
                    actingPool,
                    opposingPool);
            }

            (attackerPool, targetPool) = fightTurn.Reintegrate(actingPool, opposingPool);

            var nextTurn = currentTurn == DieOwner.Attacker ? DieOwner.Target : DieOwner.Attacker;
            var nextHasDice = nextTurn == DieOwner.Attacker ? attackerPool.Remaining.Count > 0 : targetPool.Remaining.Count > 0;

            if (nextHasDice)
            {
                turnOrder = nextTurn;
            }
        }

        return new FightLoopResult(
            attackerCurrentWounds,
            targetCurrentWounds,
            totalAttackerDamageDealt,
            totalTargetDamageDealt);
    }
}
