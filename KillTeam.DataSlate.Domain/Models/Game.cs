namespace KillTeam.DataSlate.Domain.Models;

public class Game
{
    public Guid Id { get; init; } = Guid.NewGuid();

    public DateTime PlayedAt { get; init; }

    public string? MissionName { get; init; }

    public required string TeamAName { get; init; }

    public required string TeamBName { get; init; }

    public Guid PlayerAId { get; init; }

    public Guid PlayerBId { get; init; }

    public GameStatus Status { get; init; } = GameStatus.InProgress;

    public string? WinnerTeamName { get; init; }

    public int VictoryPointsTeamA { get; set; }

    public int VictoryPointsTeamB { get; set; }

    public int CpTeamA { get; set; } = 2;

    public int CpTeamB { get; set; } = 2;
}
