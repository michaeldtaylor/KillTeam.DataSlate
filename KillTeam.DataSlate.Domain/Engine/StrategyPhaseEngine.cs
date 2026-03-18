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
        string team1Name,
        string team2Name)
    {
        var winnerName = await inputProvider.SelectInitiativeWinnerAsync(team1Name, team2Name);
        var initiativeTeamId = winnerName == team1Name
            ? game.Participant1.TeamId
            : game.Participant2.TeamId;

        var turningPoint = await turningPointRepository.CreateAsync(new TurningPoint
        {
            Id = Guid.NewGuid(),
            GameId = game.Id,
            Number = tpNumber,
            TeamWithInitiativeId = initiativeTeamId,
        });

        var (cp1, cp2) = ApplyCpGains(game, tpNumber, initiativeTeamId);

        await gameRepository.UpdateCpAsync(game.Id, cp1, cp2);
        game.Participant1.CommandPoints = cp1;
        game.Participant2.CommandPoints = cp2;

        var (nonInitId, nonInitName) = initiativeTeamId == game.Participant1.TeamId
            ? (game.Participant2.TeamId, team2Name)
            : (game.Participant1.TeamId, team1Name);

        var (initId, initName) = initiativeTeamId == game.Participant1.TeamId
            ? (game.Participant1.TeamId, team1Name)
            : (game.Participant2.TeamId, team2Name);

        (cp1, cp2) = await RunPloyLoopAsync(
            turningPoint, game.Id, game.Participant1.TeamId, nonInitId, nonInitName, cp1, cp2);

        (cp1, cp2) = await RunPloyLoopAsync(
            turningPoint, game.Id, game.Participant1.TeamId, initId, initName, cp1, cp2);

        await turningPointRepository.CompleteStrategyPhaseAsync(turningPoint.Id);

        return turningPoint;
    }

    private static (int cp1, int cp2) ApplyCpGains(Game game, int tpNumber, string initiativeTeamId)
    {
        var cp1 = game.Participant1.CommandPoints;
        var cp2 = game.Participant2.CommandPoints;

        if (tpNumber == 1)
        {
            cp1 += 1;
            cp2 += 1;
        }
        else if (initiativeTeamId == game.Participant1.TeamId)
        {
            cp1 += 1;
            cp2 += 2;
        }
        else
        {
            cp1 += 2;
            cp2 += 1;
        }

        return (cp1, cp2);
    }

    private async Task<(int cp1, int cp2)> RunPloyLoopAsync(
        TurningPoint turningPoint,
        Guid gameId,
        string teamAId,
        string activeTeamId,
        string activeTeamName,
        int cp1,
        int cp2)
    {
        while (true)
        {
            var currentCp = activeTeamId == teamAId ? cp1 : cp2;
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
                cp1 -= ploy.CpCost;
            }
            else
            {
                cp2 -= ploy.CpCost;
            }

            await gameRepository.UpdateCpAsync(gameId, cp1, cp2);
        }

        return (cp1, cp2);
    }
}

