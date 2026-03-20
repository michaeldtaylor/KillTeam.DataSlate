using KillTeam.DataSlate.Domain.Models;

namespace KillTeam.DataSlate.Domain.Engine;

public static class ActionHelpers
{
    public static OperativeContext[] GetTargets(
        OperativeContext attacker,
        IReadOnlyDictionary<Guid, OperativeContext> allOperatives)
    {
        return allOperatives.Values
            .Where(oc => !oc.State.IsIncapacitated && oc.Operative.TeamId != attacker.Operative.TeamId)
            .ToArray();
    }

    public static OperativeContext[] GetAoECandidates(
        OperativeContext target,
        IReadOnlyDictionary<Guid, OperativeContext> allOperatives)
    {
        return allOperatives.Values
            .Where(oc => oc.Operative.Id != target.Operative.Id && !oc.State.IsIncapacitated)
            .ToArray();
    }
}
