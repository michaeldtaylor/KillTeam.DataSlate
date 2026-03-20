namespace KillTeam.DataSlate.Domain.Models;

public static class WeaponExtensions
{
    public static bool HasRule(this Weapon weapon, WeaponRuleKind kind)
    {
        return weapon.Rules.Any(r => r.Kind == kind);
    }

    public static WeaponRule? GetRule(this Weapon weapon, WeaponRuleKind kind)
    {
        return weapon.Rules.FirstOrDefault(r => r.Kind == kind);
    }
}
