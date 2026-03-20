using KillTeam.DataSlate.Domain.Engine.Input;
using KillTeam.DataSlate.Domain.Events;
using KillTeam.DataSlate.Domain.Models;
using KillTeam.DataSlate.Domain.Repositories;

namespace KillTeam.DataSlate.Domain.Engine;

/// <summary>
/// Encapsulates all game logic for guard interrupts:
/// eligibility determination, control-range clear, action dispatch to
/// ShootEngine / FightEngine, and sequence counter management.
/// </summary>
public class GuardInterruptEngine(
    IGuardInterruptInputProvider inputProvider,
    ShootEngine shootEngine,
    FightEngine fightEngine,
    IActivationRepository activationRepository)
{
    /// <summary>
    /// Checks each eligible guard operative on the friendly side (i.e. not the acting enemy's team).
    /// Returns the updated sequence counter after any guard interrupt activations.
    /// </summary>
    public async Task<int> CheckAndRunInterruptsAsync(
        Operative actingEnemy,
        IReadOnlyList<GameOperativeState> allOperativeStates,
        IReadOnlyDictionary<Guid, Operative> allOperatives,
        Game game,
        TurningPoint tp,
        int seqCounter,
        GameEventStream? eventStream = null)
    {
        var enemyTeamId = actingEnemy.TeamId;

        var friendlyStates = allOperativeStates
            .Where(s => allOperatives.TryGetValue(s.OperativeId, out var o) && o.TeamId != enemyTeamId)
            .ToList();

        var eligibleGuards = friendlyStates
            .Where(s => s is { IsOnGuard: true, IsIncapacitated: false })
            .ToList();

        if (eligibleGuards.Count == 0)
        {
            return seqCounter;
        }

        foreach (var guardState in eligibleGuards)
        {
            if (!allOperatives.TryGetValue(guardState.OperativeId, out var guardOp))
            {
                continue;
            }

            var inControlRange = await inputProvider.ConfirmInControlRangeAsync(
                actingEnemy.Name, guardOp.Name);

            if (inControlRange)
            {
                await (eventStream?.EmitAsync((gameSessionId, sequenceNumber, timestamp) =>
                    new OperativeGuardClearedEvent(
                        gameSessionId,
                        sequenceNumber,
                        timestamp,
                        guardOp.Name,
                        guardState.Id)) ?? ValueTask.CompletedTask);
                guardState.IsOnGuard = false;
                continue;
            }

            var isVisible = await inputProvider.ConfirmVisibleAsync(
                actingEnemy.Name, guardOp.Name);

            if (!isVisible)
            {
                continue;
            }

            var action = await inputProvider.SelectGuardActionAsync(guardOp.Name);

            if (action == "Skip")
            {
                continue;
            }

            seqCounter++;
            var interruptActivation = new Activation
            {
                Id = Guid.NewGuid(),
                TurningPointId = tp.Id,
                SequenceNumber = seqCounter,
                OperativeId = guardOp.Id,
                TeamId = guardOp.TeamId,
                OrderSelected = guardState.Order,
                IsGuardInterrupt = true,
            };
            await activationRepository.CreateAsync(interruptActivation);

            if (action == "Shoot")
            {
                await shootEngine.RunAsync(game, interruptActivation, guardOp, guardState, allOperativeStates, allOperatives, eventStream: eventStream);
            }
            else
            {
                await fightEngine.RunAsync(game, interruptActivation, guardOp, guardState, allOperativeStates, allOperatives, eventStream);
            }

            await (eventStream?.EmitAsync((gameSessionId, sequenceNumber, timestamp) =>
                new OperativeGuardClearedEvent(
                    gameSessionId,
                    sequenceNumber,
                    timestamp,
                    guardOp.Name,
                    guardState.Id)) ?? ValueTask.CompletedTask);
            guardState.IsOnGuard = false;
        }

        return seqCounter;
    }
}
