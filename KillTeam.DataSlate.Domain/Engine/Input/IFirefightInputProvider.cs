using KillTeam.DataSlate.Domain.Models;

namespace KillTeam.DataSlate.Domain.Engine.Input;

public interface IFirefightInputProvider
{
    Task DisplayTurningPointHeaderAsync(int tpNumber);

    Task DisplayBoardStateAsync(
        GameContext context,
        TurningPoint turningPoint);

    Task DisplayActivationHeaderAsync(string operativeName);

    Task DisplayGuardSetAsync(string operativeName);

    Task DisplayCounteractAvailableAsync(string operativeName);

    Task DisplayTurningPointCompleteAsync(int tpNumber);

    Task DisplayGameOverAsync();

    Task DisplayWinnerAsync(
        string? winnerTeamName,
        int winnerVp,
        string team1Name,
        int team1Vp,
        string team2Name,
        int team2Vp);

    Task<(Operative operative, GameOperativeState state)> SelectActivatingOperativeAsync(
        IReadOnlyList<(Operative operative, GameOperativeState state)> candidates);

    Task<Order> SelectOrderAsync(string operativeName);

    Task<string> SelectActionAsync(
        string operativeName,
        int remainingAp,
        IReadOnlyList<string> availableActions);

    Task<string?> GetMoveDistanceAsync(string operativeName);

    Task<string?> SelectCounteractOperativeAsync(IReadOnlyList<string> candidateNames);

    Task<string> SelectCounteractActionAsync(string operativeName);

    Task<int> GetFinalVpAsync(string teamLabel);
}
