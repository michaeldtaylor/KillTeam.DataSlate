namespace KillTeam.DataSlate.Domain.Models;

public class Player
{
    public Guid Id { get; init; } = Guid.NewGuid();

    public required string Name { get; init; }

    public string Colour { get; init; } = "cyan";

    public bool IsInternal { get; init; }
}
