using KillTeam.DataSlate.Domain.Engine.Input;
using KillTeam.DataSlate.Domain.Models;
using KillTeam.DataSlate.Domain.Repositories;

namespace KillTeam.DataSlate.Domain.Engine;

/// <summary>
/// Encapsulates all game logic for the Strategy Phase:
/// initiative determination, CP gains, ploy eligibility and deduction,
/// turn ordering (non-initiative team records ploys first).
/// </summary>
public class StrategyPhaseEngine(
    IStrategyPhaseInputProvider inputProvider,
    IGameRepository gameRepository,
    ITurningPointRepository turningPointRepository,
    IPloyRepository ployRepository)
{
    public async Task<TurningPoint> RunAsync(
        Game game,
        int tpNumber,
        string teamAName,
        string teamBName)
    {
        var winnerName = await inputProvider.SelectInitiativeWinnerAsync(teamAName, teamBName);
        var initiativeTeamId = winnerName == teamAName
            ? game.Participant1.TeamId
            : game.Participant2.TeamId;

        var turningPoint = await turningPointRepository.CreateAsync(new TurningPoint
        {
            Id = Guid.NewGuid(),
            GameId = game.Id,
            Number = tpNumber,
            TeamWithInitiativeId = initiativeTeamId,
        });

        var (cpA, cpB) = ApplyCpGains(game, tpNumber, initiativeTeamId);

        await gameRepository.UpdateCpAsync(game.Id, cpA, cpB);
        game.Participant1.CommandPoints = cpA;
        game.Participant2.CommandPoints = cpB;

        var (nonInitId, nonInitName) = initiativeTeamId == game.Participant1.TeamId
            ? (game.Participant2.TeamId, teamBName)
            : (game.Participant1.TeamId, teamAName);

        var (initId, initName) = initiativeTeamId == game.Participant1.TeamId
            ? (game.Participant1.TeamId, teamAName)
            : (game.Participant2.TeamId, teamBName);

        (cpA, cpB) = await RunPloyLoopAsync(
            turningPoint, game.Id, game.Participant1.TeamId, nonInitId, nonInitName, cpA, cpB);

        (cpA, cpB) = await RunPloyLoopAsync(
            turningPoint, game.Id, game.Participant1.TeamId, initId, initName, cpA, cpB);

        await turningPointRepository.CompleteStrategyPhaseAsync(turningPoint.Id);

        return turningPoint;
    }

    private static (int cpA, int cpB) ApplyCpGains(Game game, int tpNumber, string initiativeTeamId)
    {
        var cpA = game.Participant1.CommandPoints;
        var cpB = game.Participant2.CommandPoints;

        if (tpNumber == 1)
        {
            cpA += 1;
            cpB += 1;
        }
        else if (initiativeTeamId == game.Participant1.TeamId)
        {
            cpA += 1;
            cpB += 2;
        }
        else
        {
            cpA += 2;
            cpB += 1;
        }

        return (cpA, cpB);
    }

    private async Task<(int cpA, int cpB)> RunPloyLoopAsync(
        TurningPoint turningPoint,
        Guid gameId,
        string teamAId,
        string activeTeamId,
        string activeTeamName,
        int cpA,
        int cpB)
    {
        while (true)
        {
            var currentCp = activeTeamId == teamAId ? cpA : cpB;
            var ploy = await inputProvider.GetPloyDetailsAsync(activeTeamName, currentCp);

            if (ploy is null)
            {
                break;
            }

            if (ploy.CpCost > currentCp)
            {
                continue;
            }

            await ployRepository.RecordPloyUseAsync(new PloyUse
            {
                Id = Guid.NewGuid(),
                TurningPointId = turningPoint.Id,
                TeamId = activeTeamId,
                PloyName = ploy.Name,
                Description = ploy.Description,
                CpCost = ploy.CpCost,
            });

            if (activeTeamId == teamAId)
            {
                cpA -= ploy.CpCost;
            }
            else
            {
                cpB -= ploy.CpCost;
            }

            await gameRepository.UpdateCpAsync(gameId, cpA, cpB);
        }

        return (cpA, cpB);
    }
}

