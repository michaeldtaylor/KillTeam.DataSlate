namespace KillTeam.DataSlate.Domain.Models;

public static class WeaponRuleParser
{
    public static List<WeaponRule> Parse(string raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            return [];
        }

        var tokens = raw.Split([',', ';'], StringSplitOptions.RemoveEmptyEntries);

        return tokens.Select(t => ParseToken(t.Trim())).ToList();
    }

    private static WeaponRule ParseToken(string token)
    {
        // Try to match "Name N" patterns (e.g. "Lethal 5", "Piercing 1", "Range 8\"")
        var parts = token.Split(' ', 2, StringSplitOptions.RemoveEmptyEntries);
        var name = parts[0];
        var paramRaw = parts.Length > 1 ? parts[1].TrimEnd('"', '\'') : string.Empty;

        int? param = !string.IsNullOrEmpty(paramRaw) && int.TryParse(paramRaw, out var p) ? p : null;

        // Special cases
        if (token.Equals("Heavy (Dash only)", StringComparison.OrdinalIgnoreCase))
        {
            return new WeaponRule(WeaponRuleKind.HeavyDashOnly, null);
        }

        if (token.StartsWith("Seek Light", StringComparison.OrdinalIgnoreCase))
        {
            return new WeaponRule(WeaponRuleKind.SeekLight, null);
        }

        if (token.StartsWith("Piercing Crits", StringComparison.OrdinalIgnoreCase))
        {
            var pcParts = token.Split(' ');

            int? pcParam = pcParts.Length > 2 && int.TryParse(pcParts[2], out var pcp) ? pcp : null;

            return new WeaponRule(WeaponRuleKind.PiercingCrits, pcParam);
        }

        // Try direct enum parse
        if (Enum.TryParse<WeaponRuleKind>(name, ignoreCase: true, out var kind))
        {
            return new WeaponRule(kind, param);
        }

        return new WeaponRule(WeaponRuleKind.Unknown, null);
    }
}
