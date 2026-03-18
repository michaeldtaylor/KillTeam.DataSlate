namespace KillTeam.DataSlate.Domain.Models;

public class Game
{
    public Guid Id { get; init; } = Guid.NewGuid();

    public DateTime StartedAt { get; init; }

    public string? MissionName { get; init; }

    public required GameParticipant Participant1 { get; init; }

    public required GameParticipant Participant2 { get; init; }

    public GameStatus Status { get; init; } = GameStatus.InProgress;

    public string? WinnerTeamId { get; init; }
}
