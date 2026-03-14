namespace KillTeam.DataSlate.Domain.Models;

public class Operative
{
    public Guid Id { get; init; } = Guid.NewGuid();

    public required string KillTeamName { get; set; }

    public required string Name { get; set; }

    public required string OperativeType { get; set; }

    public int Move { get; set; }

    public int Apl { get; set; }

    public int Wounds { get; set; }

    public int Save { get; set; }

    public string[] Equipment { get; set; } = [];

    public List<Weapon> Weapons { get; set; } = [];
}
