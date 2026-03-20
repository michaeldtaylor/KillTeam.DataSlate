namespace KillTeam.DataSlate.Domain.Models;

public record WeaponRule(WeaponRuleKind Kind, int? Param)
{
    public WeaponRuleDefinition? Definition => WeaponRuleRegistry.ByKind.GetValueOrDefault(Kind);
}

