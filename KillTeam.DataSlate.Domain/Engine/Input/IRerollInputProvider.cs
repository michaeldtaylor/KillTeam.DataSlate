namespace KillTeam.DataSlate.Domain.Engine.Input;

public interface IRerollInputProvider
{
    Task<RollableDie> SelectBalancedRerollDieAsync(IList<RollableDie> pool, string label);

    Task<int> GetCeaselessRerollValueAsync(string label);

    Task<IList<RollableDie>> SelectRelentlessRerollDiceAsync(IList<RollableDie> pool, string label);

    Task<bool> ConfirmCpRerollAsync(string label, int currentCp);

    Task<RollableDie> SelectCpRerollDieAsync(IList<RollableDie> pool);
}
