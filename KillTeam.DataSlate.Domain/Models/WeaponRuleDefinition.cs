namespace KillTeam.DataSlate.Domain.Models;

public class WeaponRuleDefinition
{
    public required WeaponRuleKind Kind { get; init; }

    public required string Description { get; init; }

    public required WeaponRulePhase Phase { get; init; }
}
