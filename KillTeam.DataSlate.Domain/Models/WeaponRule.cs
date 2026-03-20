namespace KillTeam.DataSlate.Domain.Models;

public record WeaponRule(WeaponRuleKind Kind, int? Param, string RawText)
{
    public WeaponRuleDefinition? Definition => WeaponRuleRegistry.ByKind.GetValueOrDefault(Kind);
}

