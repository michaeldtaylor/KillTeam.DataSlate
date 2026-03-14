namespace KillTeam.DataSlate.Domain.Models;

public class Player
{
    public Guid Id { get; init; } = Guid.NewGuid();

    public required string Name { get; init; }
}
