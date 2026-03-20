using KillTeam.DataSlate.Domain.Models;

namespace KillTeam.DataSlate.Domain.Engine;

public static class ActionHelpers
{
    public static GameOperativeState[] GetTargetOperativeStates(
        Operative attacker,
        IReadOnlyList<GameOperativeState> allOperativeStates,
        IReadOnlyDictionary<Guid, Operative> allOperatives)
    {
        return allOperativeStates
            .Where(s => !s.IsIncapacitated && allOperatives.TryGetValue(s.OperativeId, out var operative) && operative.TeamId != attacker.TeamId)
            .ToArray();
    }

    public static GameOperativeState[] GetAoECandidateOperativeStates(
        Operative target,
        IReadOnlyList<GameOperativeState> allOperativeStates,
        IReadOnlyDictionary<Guid, Operative> allOperatives)
    {
        return allOperativeStates
            .Where(s => s.OperativeId != target.Id && !s.IsIncapacitated && allOperatives.ContainsKey(s.OperativeId))
            .ToArray();
    }
}
