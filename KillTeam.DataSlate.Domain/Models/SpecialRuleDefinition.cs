namespace KillTeam.DataSlate.Domain.Models;

public class SpecialRuleDefinition
{
    public required SpecialRuleKind Kind { get; init; }

    public required string Description { get; init; }

    public required SpecialRulePhase Phase { get; init; }
}
