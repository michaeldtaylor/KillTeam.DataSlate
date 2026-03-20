using KillTeam.DataSlate.Domain.Events;
using KillTeam.DataSlate.Domain.Models;

namespace KillTeam.DataSlate.Domain.Engine.Input;

public interface IShootInputProvider
{
    Task<OperativeContext> SelectTargetAsync(IList<OperativeContext> candidates);

    Task<int> GetTargetDistanceAsync(string targetName);

    Task<Weapon> SelectWeaponAsync(IList<Weapon> weapons, bool hasMovedNonDash);

    Task<string> GetCoverStatusAsync(string targetName, bool lightCoverBlocked = false);

    Task<int> GetFriendlyAllyCountAsync();

    Task<string> GetNarrativeNoteAsync();

    bool HasRemainingUses(Weapon weapon);

    void RecordWeaponFired(Weapon weapon);

    Task<int[]> RollOrEnterDiceAsync(
        int count, string label,
        string operativeName, string role, string phase,
        string participant, GameEventStream? eventStream);
}
