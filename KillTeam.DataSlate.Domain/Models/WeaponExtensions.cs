namespace KillTeam.DataSlate.Domain.Models;

public static class WeaponExtensions
{
    extension(Weapon weapon)
    {
        public bool HasRule(WeaponRuleKind kind)
        {
            return weapon.Rules.Any(r => r.Kind == kind);
        }

        public WeaponRule? GetRule(WeaponRuleKind kind)
        {
            return weapon.Rules.FirstOrDefault(r => r.Kind == kind);
        }
    }
}
