namespace KillTeam.DataSlate.Domain.Models;

public class TurningPoint
{
    public Guid Id { get; init; } = Guid.NewGuid();

    public Guid GameId { get; init; }

    public int Number { get; init; }

    public string? TeamWithInitiativeId { get; init; }

    public int CommandPointsTeamA { get; init; }

    public int CommandPointsTeamB { get; init; }

    public bool IsStrategyPhaseComplete { get; set; }
}
