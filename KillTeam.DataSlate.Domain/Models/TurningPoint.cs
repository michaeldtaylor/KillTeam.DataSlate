namespace KillTeam.DataSlate.Domain.Models;
public class TurningPoint
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public Guid GameId { get; set; }
    public int Number { get; set; }
    public string? TeamWithInitiativeName { get; set; }
    public int CpTeamA { get; set; }
    public int CpTeamB { get; set; }
    public bool IsStrategyPhaseComplete { get; set; }
}
