namespace KillTeam.DataSlate.Domain.Models;

public class Weapon
{
    public Guid Id { get; init; } = Guid.NewGuid();

    public Guid OperativeId { get; set; }

    public required string Name { get; init; }

    public WeaponType Type { get; init; }

    public int Atk { get; init; }

    public int Hit { get; init; }

    public int NormalDmg { get; init; }

    public int CriticalDmg { get; init; }

    public string SpecialRules { get; init; } = string.Empty;

    // Parsed at use-time, not stored
    public IReadOnlyList<WeaponSpecialRule> ParsedRules { get; init; } = [];
}
