namespace KillTeam.TeamExtractor.Models;

/// <summary>An equipment item with its description text.</summary>
public class ExtractedEquipmentItem
{
    /// <summary>The equipment item name in title case.</summary>
    public required string Name { get; init; }

    /// <summary>The description text for the item, or empty string if none found.</summary>
    public string Description { get; init; } = "";
}
