using KillTeam.DataSlate.Domain.Engine;
using KillTeam.DataSlate.Domain.Models;
using Microsoft.Extensions.Logging;

namespace KillTeam.DataSlate.Console.Orchestrators;

public class StrategyPhaseOrchestrator(StrategyPhaseEngine engine, ILogger<StrategyPhaseOrchestrator> logger)
{
    public async Task<TurningPoint> RunAsync(
        Game game,
        int turningPointNumber,
        string team1Name,
        string team2Name)
    {
        logger.LogDebug("Strategy phase TP{TpNumber} started for game {GameId}", turningPointNumber, game.Id);

        var turningPoint = await engine.RunAsync(game, turningPointNumber, team1Name, team2Name);

        logger.LogDebug("Strategy phase TP{TpNumber} complete", turningPointNumber);

        return turningPoint;
    }
}