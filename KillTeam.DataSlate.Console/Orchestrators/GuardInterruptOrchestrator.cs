using KillTeam.DataSlate.Domain.Engine;
using KillTeam.DataSlate.Domain.Events;
using KillTeam.DataSlate.Domain.Models;
using Microsoft.Extensions.Logging;

namespace KillTeam.DataSlate.Console.Orchestrators;

public class GuardInterruptOrchestrator(GuardInterruptEngine engine, ILogger<GuardInterruptOrchestrator> logger)
{
    /// <summary>
    /// Checks each eligible guard operative on the friendly side.
    /// Returns the updated sequence counter after any guard interrupt activations.
    /// </summary>
    public async Task<int> CheckAndRunInterruptsAsync(
        Operative actingEnemy,
        IReadOnlyList<GameOperativeState> allOperativeStates,
        IReadOnlyDictionary<Guid, Operative> allOperatives,
        Game game,
        TurningPoint turningPoint,
        int sequenceCounter,
        GameEventStream? eventStream = null)
    {
        logger.LogDebug("Checking guard interrupts for game {GameId}", game.Id);

        return await engine.CheckAndRunInterruptsAsync(
            actingEnemy,
            allOperativeStates,
            allOperatives,
            game,
            turningPoint,
            sequenceCounter,
            eventStream);
    }
}
