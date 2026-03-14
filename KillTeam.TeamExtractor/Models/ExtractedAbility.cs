namespace KillTeam.TeamExtractor.Models;

/// <summary>
/// A named ability or action.
/// <c>ApCost</c> is null for passive rules and strategic gambits;
/// set to 1 for 1AP actions on back-of-card pages.
/// </summary>
public class ExtractedAbility
{
    /// <summary>The ability name in title case or as printed.</summary>
    public required string Name { get; init; }

    /// <summary>The AP cost, or null if the ability is passive.</summary>
    public int? ApCost { get; init; }

    /// <summary>The full ability description text.</summary>
    public required string Text { get; init; }
}
