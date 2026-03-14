using KillTeam.DataSlate.Domain.Models;

namespace KillTeam.DataSlate.Domain.Services;

public class GuardResolutionService
{
    public IReadOnlyList<GameOperativeState> GetEligibleGuards(
        IEnumerable<GameOperativeState> friendlyStates)
        => friendlyStates.Where(s => s.IsOnGuard && !s.IsIncapacitated).ToList();

    public bool IsGuardStillValid(GameOperativeState guard, bool enemyIsInControlRange)
        => !enemyIsInControlRange;

    public IReadOnlyList<GameOperativeState> ClearAllGuards(IEnumerable<GameOperativeState> states)
    {
        var list = states.ToList();
        foreach (var s in list)
        {
            s.IsOnGuard = false;
        }
        return list;
    }
}
