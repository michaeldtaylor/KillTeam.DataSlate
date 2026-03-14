namespace KillTeam.DataSlate.Domain.Models;
public class Game
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public DateTime PlayedAt { get; set; }
    public string? MissionName { get; set; }
    public Guid TeamAId { get; set; }
    public Guid TeamBId { get; set; }
    public Guid PlayerAId { get; set; }
    public Guid PlayerBId { get; set; }
    public GameStatus Status { get; set; } = GameStatus.InProgress;
    public Guid? WinnerTeamId { get; set; }
    public int VictoryPointsTeamA { get; set; }
    public int VictoryPointsTeamB { get; set; }
    public int CpTeamA { get; set; } = 2;
    public int CpTeamB { get; set; } = 2;
}
