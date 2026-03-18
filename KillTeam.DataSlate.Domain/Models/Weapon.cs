namespace KillTeam.DataSlate.Domain.Models;

public class Weapon
{
    public Guid Id { get; init; } = Guid.NewGuid();

    public required string Name { get; init; }

    public WeaponType Type { get; init; }

    public int Atk { get; init; }

    public int Hit { get; init; }

    public int NormalDmg { get; init; }

    public int CriticalDmg { get; init; }

    public string WeaponRules { get; init; } = string.Empty;

    public IReadOnlyList<WeaponRule> Rules { get; init; } = [];
}
