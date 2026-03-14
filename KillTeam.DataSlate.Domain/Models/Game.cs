namespace KillTeam.DataSlate.Domain.Models;

public class Game
{
    public Guid Id { get; init; } = Guid.NewGuid();

    public DateTime PlayedAt { get; set; }

    public string? MissionName { get; set; }

    public required string TeamAName { get; set; }

    public required string TeamBName { get; set; }

    public Guid PlayerAId { get; set; }

    public Guid PlayerBId { get; set; }

    public GameStatus Status { get; set; } = GameStatus.InProgress;

    public string? WinnerTeamName { get; set; }

    public int VictoryPointsTeamA { get; set; }

    public int VictoryPointsTeamB { get; set; }

    public int CpTeamA { get; set; } = 2;

    public int CpTeamB { get; set; } = 2;
}
