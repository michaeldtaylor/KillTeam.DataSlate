namespace KillTeam.DataSlate.Domain.Models;

public class Operative
{
    public Guid Id { get; init; } = Guid.NewGuid();

    public required string TeamId { get; set; }

    public required string Name { get; init; }

    public required string OperativeType { get; init; }

    public string PrimaryKeyword { get; init; } = string.Empty;

    public string[] Keywords { get; init; } = [];

    public int Move { get; init; }

    public int Apl { get; init; }

    public int Wounds { get; init; }

    public int Save { get; init; }

    public string[] Equipment { get; init; } = [];

    public List<Weapon> Weapons { get; init; } = [];

    public List<OperativeAbility> Abilities { get; init; } = [];

    public List<OperativeSpecialAction> SpecialActions { get; init; } = [];

    public List<OperativeWeaponRule> SpecialRules { get; init; } = [];
}
