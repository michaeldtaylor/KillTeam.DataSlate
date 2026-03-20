namespace KillTeam.DataSlate.Domain.Models;

public class GameParticipant
{
    public required Guid PlayerId { get; init; }

    public required TeamSummary Team { get; init; }

    public int CommandPoints { get; set; } = 2;

    public int VictoryPoints { get; set; }
}
