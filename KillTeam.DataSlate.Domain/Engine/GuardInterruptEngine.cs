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

        var friendlyOperatives = context.Operatives.Values
            .Where(oc => oc.Operative.TeamId != activatingTeamId)
            .ToList();

        var eligibleGuards = friendlyOperatives
            .Where(oc => oc.State is { IsOnGuard: true, IsIncapacitated: false })
            .ToList();

        if (eligibleGuards.Count == 0)
        {
            return sequenceCounter;
        }

        foreach (var guardOperative in eligibleGuards)
        {
            var inControlRange = await inputProvider.ConfirmInControlRangeAsync(
                activatingOperative.Name, guardOperative.Operative.Name);

            if (inControlRange)
            {
                await (context.EventStream?.EmitAsync((gameSessionId, sequenceNumber, timestamp) =>
                    new OperativeGuardClearedEvent(
                        gameSessionId,
                        sequenceNumber,
                        timestamp,
                        guardOperative.Operative.Name,
                        guardOperative.State.Id)) ?? ValueTask.CompletedTask);
                guardOperative.State.IsOnGuard = false;
                continue;
            }

            var isVisible = await inputProvider.ConfirmVisibleAsync(
                activatingOperative.Name, guardOperative.Operative.Name);

            if (!isVisible)
            {
                continue;
            }

            var action = await inputProvider.SelectGuardActionAsync(guardOperative.Operative.Name);

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
                OperativeId = guardOperative.Operative.Id,
                TeamId = guardOperative.Operative.TeamId,
                OrderSelected = guardOperative.State.Order,
                IsGuardInterrupt = true,
            };
            await activationRepository.CreateAsync(interruptActivation);

            if (action == "Shoot")
            {
                await shootEngine.RunAsync(context, interruptActivation, guardOperative);
            }
            else
            {
                await fightEngine.RunAsync(context, interruptActivation, guardOperative);
            }

            await (context.EventStream?.EmitAsync((gameSessionId, sequenceNumber, timestamp) =>
                new OperativeGuardClearedEvent(
                    gameSessionId,
                    sequenceNumber,
                    timestamp,
                    guardOperative.Operative.Name,
                    guardOperative.State.Id)) ?? ValueTask.CompletedTask);

            guardOperative.State.IsOnGuard = false;
        }

        return sequenceCounter;
    }
}
