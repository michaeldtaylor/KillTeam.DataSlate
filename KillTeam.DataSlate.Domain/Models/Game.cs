namespace KillTeam.DataSlate.Domain.Models;

public class Game
{
    public Guid Id { get; init; } = Guid.NewGuid();

    public DateTime PlayedAt { get; init; }

    public string? MissionName { get; init; }

    public required GameParticipant TeamA { get; init; }

    public required GameParticipant TeamB { get; init; }

    public GameStatus Status { get; init; } = GameStatus.InProgress;

    public string? WinnerTeamId { get; init; }
}
