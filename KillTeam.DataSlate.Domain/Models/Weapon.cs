namespace KillTeam.DataSlate.Domain.Models;

public class Weapon
{
    public Guid Id { get; init; } = Guid.NewGuid();

    public Guid OperativeId { get; set; }

    public required string Name { get; set; }

    public WeaponType Type { get; set; }

    public int Atk { get; set; }

    public int Hit { get; set; }

    public int NormalDmg { get; set; }

    public int CriticalDmg { get; set; }

    public string SpecialRules { get; set; } = string.Empty;

    // Parsed at use-time, not stored
    public IReadOnlyList<WeaponSpecialRule> ParsedRules { get; set; } = [];
}
