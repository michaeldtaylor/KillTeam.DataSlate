namespace KillTeam.DataSlate.Domain.Engine.Input;

public interface IStrategyPhaseInputProvider
{
    /// <summary>
    /// Prompts for initiative winner. Handles tie re-rolls internally.
    /// Returns the name of the winning team.
    /// </summary>
    Task<string> SelectInitiativeWinnerAsync(string team1Name, string team2Name);

    /// <summary>
    /// Prompts whether the team wants to record a ploy, and if so collects details.
    /// Returns null when the team is done recording ploys.
    /// </summary>
    Task<PloyEntry?> GetPloyDetailsAsync(string teamName, int currentCp);
}
