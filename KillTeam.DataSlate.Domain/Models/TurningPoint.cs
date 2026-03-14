namespace KillTeam.DataSlate.Domain.Models;

public class TurningPoint
{
    public Guid Id { get; init; } = Guid.NewGuid();

    public Guid GameId { get; init; }

    public int Number { get; init; }

    public string? TeamWithInitiativeName { get; init; }

    public int CpTeamA { get; init; }

    public int CpTeamB { get; init; }

    public bool IsStrategyPhaseComplete { get; set; }
}
