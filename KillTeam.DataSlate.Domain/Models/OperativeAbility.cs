namespace KillTeam.DataSlate.Domain.Models;

/// <summary>A passive ability on an operative's datacard.</summary>
public class OperativeAbility
{
    public required string Name { get; init; }

    public required string Text { get; init; }
}
