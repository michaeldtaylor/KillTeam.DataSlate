namespace KillTeam.DataSlate.Domain.Models;

public class Player
{
    public Guid Id { get; init; } = Guid.NewGuid();

    public required string Username { get; init; }

    public required string FirstName { get; init; }

    public required string LastName { get; init; }

    public PlayerColour Colour { get; init; } = PlayerColour.Cyan;
}
