using KillTeam.DataSlate.Domain.Events;
using KillTeam.DataSlate.Domain.Models;

namespace KillTeam.DataSlate.Domain.Engine.Input;

public interface IFightInputProvider
{
    Task<OperativeContext> SelectTargetAsync(IList<OperativeContext> candidates);

    Task<Weapon> SelectAttackerWeaponAsync(IList<Weapon> weapons, bool isInjured);

    Task<Weapon> SelectTargetWeaponAsync(IList<Weapon> weapons);

    Task<int> GetFightAssistCountAsync();

    Task<FightAction> SelectActionAsync(IList<FightAction> actions, string operativeName);

    Task<string> GetNarrativeNoteAsync();

    Task<int[]> RollOrEnterDiceAsync(
        int count,
        string label,
        string operativeName,
        string role,
        string phase,
        string participant,
        GameEventStream? eventStream);
}
