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

        var team1 = await teamRepository.GetByIdAsync(game.Participant1.Team.Id);
        var team2 = await teamRepository.GetByIdAsync(game.Participant2.Team.Id);

        var allOperatives = (team1?.Operatives ?? [])
            .Concat(team2?.Operatives ?? [])
            .ToList();

        var statesByOperativeId = (await stateRepository.GetByGameAsync(game.Id))
            .ToDictionary(s => s.OperativeId);

        var operatives = allOperatives
            .ToDictionary(o => o.Id, o => new OperativeContext(o, statesByOperativeId[o.Id]));

        var context = new GameContext(game, operatives);

        await firefightPhaseEngine.RunAsync(context, currentTurningPoint);
    }
}
