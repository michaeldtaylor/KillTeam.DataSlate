namespace KillTeam.TeamExtractor.Models;

/// <summary>A weapon extracted from a datacards PDF row.</summary>
public class ExtractedWeapon
{
    /// <summary>The weapon display name.</summary>
    public required string Name { get; init; }

    /// <summary>Ranged or Melee, detected from the weapon-type icon in the PDF.</summary>
    public WeaponType Type { get; init; }

    /// <summary>Number of attack dice.</summary>
    public int Atk { get; init; }

    /// <summary>Hit value with trailing +, e.g. "3+".</summary>
    public required string Hit { get; init; }

    /// <summary>Damage string in normal/critical format, e.g. "3/4".</summary>
    public required string Dmg { get; init; }

    /// <summary>Comma-separated special rule names, or empty string if none.</summary>
    public string SpecialRules { get; init; } = "";
}
