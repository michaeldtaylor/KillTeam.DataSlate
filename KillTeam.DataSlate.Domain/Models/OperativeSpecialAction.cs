namespace KillTeam.DataSlate.Domain.Models;

/// <summary>An active special action on an operative's datacard (has AP cost).</summary>
public class OperativeSpecialAction
{
    public required string Name { get; init; }

    public required string Text { get; init; }

    public int ApCost { get; init; } = 1;
}
