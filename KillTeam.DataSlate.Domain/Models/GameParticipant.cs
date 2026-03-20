namespace KillTeam.DataSlate.Domain.Models;

public class GameParticipant
{
    public required TeamSummary Team { get; init; }

    public required Guid PlayerId { get; init; }

    public int CommandPoints { get; set; } = 2;

    public int VictoryPoints { get; set; }
}
