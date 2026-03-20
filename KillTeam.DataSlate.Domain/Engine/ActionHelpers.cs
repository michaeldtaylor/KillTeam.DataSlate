using KillTeam.DataSlate.Domain.Models;

namespace KillTeam.DataSlate.Domain.Engine;

public static class ActionHelpers
{
    public static GameOperativeState[] GetActiveTargetOperativeStates(
        Operative attacker,
        IReadOnlyList<GameOperativeState> allOperativeStates,
        IReadOnlyDictionary<Guid, Operative> allOperatives)
    {
        return allOperativeStates
            .Where(s => !s.IsIncapacitated
                        && allOperatives.TryGetValue(s.OperativeId, out var operative)
                        && operative.TeamId != attacker.TeamId)
            .ToArray();
    }
}
