namespace KillTeam.DataSlate.Domain.Models;

public class TurningPoint
{
    public Guid Id { get; init; } = Guid.NewGuid();

    public Guid GameId { get; init; }

    public int Number { get; init; }

    public string? TeamWithInitiativeId { get; init; }

    public int CommandPointsParticipant1 { get; init; }

    public int CommandPointsParticipant2 { get; init; }

    public bool IsStrategyPhaseComplete { get; set; }
}
