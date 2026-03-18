using KillTeam.DataSlate.Domain.Services;

namespace KillTeam.DataSlate.Domain.Models;

public record WeaponSpecialRule(SpecialRuleKind Kind, int? Param, string RawText)
{
    public SpecialRuleDefinition? Definition => SpecialRuleRegistry.ByKind.GetValueOrDefault(Kind);
}

