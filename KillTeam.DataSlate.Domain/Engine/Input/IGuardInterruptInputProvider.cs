namespace KillTeam.DataSlate.Domain.Engine.Input;

public interface IGuardInterruptInputProvider
{
    /// <summary>Asks whether the acting enemy is within 6" (control range) of the guard operative.</summary>
    Task<bool> ConfirmInControlRangeAsync(string enemyName, string guardOpName);

    /// <summary>Asks whether the acting enemy is visible to the guard operative.</summary>
    Task<bool> ConfirmVisibleAsync(string enemyName, string guardOpName);

    /// <summary>Asks whether to Shoot, Fight, or Skip the guard interrupt.</summary>
    Task<string> SelectGuardActionAsync(string guardOpName);
}
