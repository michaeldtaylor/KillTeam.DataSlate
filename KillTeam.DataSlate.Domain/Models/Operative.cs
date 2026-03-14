namespace KillTeam.DataSlate.Domain.Models;

public class Operative
{
    public Guid Id { get; init; } = Guid.NewGuid();

    public required string TeamName { get; set; }

    public required string Name { get; init; }

    public required string OperativeType { get; init; }

    public int Move { get; init; }

    public int Apl { get; init; }

    public int Wounds { get; init; }

    public int Save { get; init; }

    public string[] Equipment { get; init; } = [];

    public List<Weapon> Weapons { get; init; } = [];
}
