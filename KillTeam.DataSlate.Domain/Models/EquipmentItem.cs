namespace KillTeam.DataSlate.Domain.Models;

/// <summary>An equipment item with name and optional descriptive text.</summary>
public class EquipmentItem
{
    public required string Name { get; init; }

    public string Text { get; init; } = string.Empty;
}
