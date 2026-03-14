namespace KillTeam.TeamExtractor.Models;

/// <summary>
/// A custom weapon rule definition referenced by * in weapon special rules.
/// These appear as footnotes on back-of-card pages.
/// </summary>
public class ExtractedWeaponRule
{
    /// <summary>The rule name as printed after the * marker.</summary>
    public required string Name { get; init; }

    /// <summary>The rule description text.</summary>
    public required string Text { get; init; }
}
