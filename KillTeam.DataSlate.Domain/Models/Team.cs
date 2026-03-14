namespace KillTeam.DataSlate.Domain.Models;

public class Team
{
    public required string Name { get; init; }

    public required string Faction { get; init; }

    public List<Operative> Operatives { get; set; } = [];
}
