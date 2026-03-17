namespace KillTeam.TeamExtractor.Models;

/// <summary>An operative extracted from a datacards PDF.</summary>
public class ExtractedOperative
{
    /// <summary>The operative display name in title case.</summary>
    public required string Name { get; init; }

    /// <summary>Move characteristic in inches.</summary>
    public int Move { get; init; }

    /// <summary>Action Point Limit.</summary>
    public int Apl { get; init; }

    /// <summary>Wound track value.</summary>
    public int Wounds { get; init; }

    /// <summary>Save value with trailing +, e.g. "3+".</summary>
    public required string Save { get; init; }

    /// <summary>Weapons listed on the operative's datacard.</summary>
    public List<ExtractedWeapon> Weapons { get; init; } = [];

    /// <summary>All faction keywords in title case, including duplicates across operatives.</summary>
    public List<string> Keywords { get; init; } = [];

    /// <summary>The first keyword (primary faction keyword) in title case.</summary>
    public string PrimaryKeyword { get; init; } = string.Empty;

    /// <summary>
    /// Abilities from front-of-card (passive) and back-of-card (passive) pages.
    /// Mutable so that back-of-card parsing can append to the same instance.
    /// Contains only passive rules (no AP cost).
    /// </summary>
    public List<ExtractedAbility> Abilities { get; init; } = [];

    /// <summary>
    /// Active 1AP actions from back-of-card pages (AP cost set).
    /// Mutable so that back-of-card parsing can append to the same instance.
    /// Emitted as "specialActions" in YAML; omitted when empty.
    /// </summary>
    public List<ExtractedAbility> SpecialActions { get; init; } = [];

    /// <summary>
    /// Custom weapon rules defined as footnotes (* lines) on back-of-card pages.
    /// Mutable so that back-of-card parsing can append to the same instance.
    /// Emitted as "specialRules" in JSON; omitted when empty.
    /// </summary>
    public List<ExtractedWeaponRule> SpecialRules { get; init; } = [];
}
