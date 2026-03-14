namespace KillTeam.DataSlate.Domain.Models;

public class KillTeam
{
    public required string Name { get; set; }

    public required string Faction { get; set; }

    public List<Operative> Operatives { get; set; } = [];
}
