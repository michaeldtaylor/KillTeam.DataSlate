using KillTeam.DataSlate.Domain.Events;
using KillTeam.DataSlate.Domain.Models;

namespace KillTeam.DataSlate.Domain.Engine.Input;

public interface IShootInputProvider
{
    Task<GameOperativeState> SelectTargetAsync(
        IList<GameOperativeState> candidates,
        IReadOnlyDictionary<Guid, Operative> allOperatives);

    Task<int> GetTargetDistanceAsync(string targetName);

    Task<Weapon> SelectWeaponAsync(IList<Weapon> weapons, bool hasMovedNonDash);

    Task<string> GetCoverStatusAsync(string targetName);

    Task<int> GetFriendlyAllyCountAsync();

    Task<string> GetNarrativeNoteAsync();

    Task<int[]> RollOrEnterDiceAsync(
        int count, string label,
        string operativeName, string role, string phase,
        string participant, GameEventStream? eventStream);
}
