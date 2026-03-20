using KillTeam.DataSlate.Domain.Engine.Input;
using KillTeam.DataSlate.Domain.Events;
using KillTeam.DataSlate.Domain.Models;
using KillTeam.DataSlate.Domain.Repositories;
using Microsoft.Extensions.Logging;

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
    IActivationRepository activationRepository,
    ILogger<GuardInterruptEngine> logger)
{
    /// <summary>
    /// Checks each eligible guard operative on the side opposing the activating operative.
    /// Returns the updated sequence counter after any guard interrupt activations.
    /// </summary>
    public async Task<int> CheckAndRunInterruptsAsync(
        GameContext context,
        TurningPoint turningPoint,
        Operative activatingOperative,
        int sequenceCounter)
    {
        logger.LogDebug("Checking guard interrupts for game {GameId}", context.Game.Id);

        var activatingTeamId = activatingOperative.TeamId;

        var friendlyStates = context.OperativeStates
            .Where(s => context.Operatives.TryGetValue(s.OperativeId, out var o) && o.TeamId != activatingTeamId)
            .ToList();

        var eligibleGuards = friendlyStates
            .Where(s => s is { IsOnGuard: true, IsIncapacitated: false })
            .ToList();

        if (eligibleGuards.Count == 0)
        {
            return sequenceCounter;
        }

        foreach (var guardState in eligibleGuards)
        {
            if (!context.Operatives.TryGetValue(guardState.OperativeId, out var guardOp))
            {
                continue;
            }

            var inControlRange = await inputProvider.ConfirmInControlRangeAsync(
                activatingOperative.Name, guardOp.Name);

            if (inControlRange)
            {
                await (context.EventStream?.EmitAsync((gameSessionId, sequenceNumber, timestamp) =>
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
                activatingOperative.Name, guardOp.Name);

            if (!isVisible)
            {
                continue;
            }

            var action = await inputProvider.SelectGuardActionAsync(guardOp.Name);

            if (action == "Skip")
            {
                continue;
            }

            sequenceCounter++;
            var interruptActivation = new Activation
            {
                Id = Guid.NewGuid(),
                TurningPointId = turningPoint.Id,
                SequenceNumber = sequenceCounter,
                OperativeId = guardOp.Id,
                TeamId = guardOp.TeamId,
                OrderSelected = guardState.Order,
                IsGuardInterrupt = true,
            };
            await activationRepository.CreateAsync(interruptActivation);

            if (action == "Shoot")
            {
                await shootEngine.RunAsync(context, interruptActivation, guardOp, guardState);
            }
            else
            {
                await fightEngine.RunAsync(context, interruptActivation, guardOp, guardState);
            }

            await (context.EventStream?.EmitAsync((gameSessionId, sequenceNumber, timestamp) =>
                new OperativeGuardClearedEvent(
                    gameSessionId,
                    sequenceNumber,
                    timestamp,
                    guardOp.Name,
                    guardState.Id)) ?? ValueTask.CompletedTask);

            guardState.IsOnGuard = false;
        }

        return sequenceCounter;
    }
}
