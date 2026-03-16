namespace KillTeam.DataSlate.Domain.Models;

/// <summary>A custom weapon rule definition on an operative's datacard.</summary>
public class OperativeWeaponRule
{
    public required string Name { get; init; }

    public required string Text { get; init; }
}
