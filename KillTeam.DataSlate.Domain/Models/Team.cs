namespace KillTeam.DataSlate.Domain.Models;

public class Team
{
    public required string Id { get; init; }

    public required string Name { get; init; }

    public required string Faction { get; init; }

    public List<Operative> Operatives { get; init; } = [];
}
