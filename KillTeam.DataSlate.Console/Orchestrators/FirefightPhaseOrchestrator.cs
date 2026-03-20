using KillTeam.DataSlate.Domain.Engine;
using KillTeam.DataSlate.Domain.Models;
using KillTeam.DataSlate.Domain.Repositories;
using Microsoft.Extensions.Logging;

namespace KillTeam.DataSlate.Console.Orchestrators;

public class FirefightPhaseOrchestrator(
    ITeamRepository teamRepository,
    IGameOperativeStateRepository stateRepository,
    FirefightPhaseEngine firefightPhaseEngine,
    ILogger<FirefightPhaseOrchestrator> logger)
{
    public async Task RunAsync(Game game, TurningPoint currentTurningPoint)
    {
        logger.LogDebug(
            "Firefight phase TP{TpNumber} started for game {GameId}",
            currentTurningPoint.Number,
            game.Id);

        var team1 = await teamRepository.GetByIdAsync(game.Participant1.TeamId);
        var team2 = await teamRepository.GetByIdAsync(game.Participant2.TeamId);

        var allOperatives = (team1?.Operatives ?? [])
            .Concat(team2?.Operatives ?? [])
            .ToDictionary(o => o.Id);

        var allStates = (await stateRepository.GetByGameAsync(game.Id)).ToList();

        await firefightPhaseEngine.RunAsync(
            game,
            currentTurningPoint,
            allOperatives,
            allStates);
    }
}