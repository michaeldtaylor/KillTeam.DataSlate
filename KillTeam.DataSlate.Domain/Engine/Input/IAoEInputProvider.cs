using KillTeam.DataSlate.Domain.Models;

namespace KillTeam.DataSlate.Domain.Engine.Input;

public interface IAoEInputProvider
{
    Task<List<GameOperativeState>> SelectAdditionalTargetsAsync(
        IList<GameOperativeState> candidates,
        IReadOnlyDictionary<Guid, Operative> allOperatives,
        Weapon weapon,
        string attackerName,
        string targetName,
        string attackerTeamId);

    Task<bool> ConfirmFriendlyFireAsync(int friendlyCount);

    Task<string> GetCoverStatusAsync(string targetName);

    Task<string> GetNarrativeNoteAsync();

    Task<int[]> RollOrEnterDiceAsync(int count, string label);
}
