using KillTeam.DataSlate.Domain.Engine;
using KillTeam.DataSlate.Domain.Events;
using KillTeam.DataSlate.Domain.Models;
using Spectre.Console;

namespace KillTeam.DataSlate.Console.Orchestrators;

public class GuardInterruptOrchestrator(IAnsiConsole console, GuardInterruptEngine engine)
{
    /// <summary>
    /// Checks each eligible guard operative on the friendly side.
    /// Returns the updated sequence counter after any guard interrupt activations.
    /// </summary>
    public async Task<int> CheckAndRunInterruptsAsync(
        Operative actingEnemy,
        GameOperativeState actingEnemyState,
        IReadOnlyList<GameOperativeState> allOperativeStates,
        IReadOnlyDictionary<Guid, Operative> allOperatives,
        Game game,
        TurningPoint tp,
        int seqCounter,
        GameEventStream? eventStream = null)
    {
        return await engine.CheckAndRunInterruptsAsync(
            actingEnemy, actingEnemyState, allOperativeStates, allOperatives,
            game, tp, seqCounter, eventStream);
    }
}