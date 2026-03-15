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

    /// <summary>Normal (non-critical) damage value.</summary>
    public int DmgNormal { get; init; }

    /// <summary>Critical damage value.</summary>
    public int DmgCrit { get; init; }

    /// <summary>Individual weapon rule names (e.g. "Range 8\"", "Piercing 1"). Omitted from JSON when empty.</summary>
    public List<string> WeaponRules { get; init; } = [];
}
