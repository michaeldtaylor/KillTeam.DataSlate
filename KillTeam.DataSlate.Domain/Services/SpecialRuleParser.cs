using KillTeam.DataSlate.Domain.Models;

namespace KillTeam.DataSlate.Domain.Services;

public static class SpecialRuleParser
{
    public static List<WeaponSpecialRule> Parse(string raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
            return [];

        var results = new List<WeaponSpecialRule>();
        var tokens = raw.Split([',', ';'], StringSplitOptions.RemoveEmptyEntries);

        foreach (var token in tokens)
        {
            var t = token.Trim();
            results.Add(ParseToken(t));
        }

        return results;
    }

    private static WeaponSpecialRule ParseToken(string token)
    {
        // Try to match "Name N" patterns (e.g. "Lethal 5", "Piercing 1", "Devastating 3")
        var parts = token.Split(' ', 2, StringSplitOptions.RemoveEmptyEntries);
        var name = parts[0];
        int? param = parts.Length > 1 && int.TryParse(parts[1], out var p) ? p : null;

        // Special cases
        if (token.Equals("Heavy (Dash only)", StringComparison.OrdinalIgnoreCase))
            return new WeaponSpecialRule(SpecialRuleKind.HeavyDashOnly, null, token);

        if (token.StartsWith("D.", StringComparison.OrdinalIgnoreCase) ||
            token.StartsWith("D ", StringComparison.OrdinalIgnoreCase))
            return new WeaponSpecialRule(SpecialRuleKind.DDevastating, param, token);

        if (token.Equals("PiercingCrits", StringComparison.OrdinalIgnoreCase) ||
            token.Equals("Piercing Crits", StringComparison.OrdinalIgnoreCase))
        {
            var pcParts = token.Split(' ');
            int? pcParam = pcParts.Length > 2 && int.TryParse(pcParts[2], out var pcp) ? pcp : null;
            return new WeaponSpecialRule(SpecialRuleKind.PiercingCrits, pcParam, token);
        }

        // Try direct enum parse
        if (Enum.TryParse<SpecialRuleKind>(name, ignoreCase: true, out var kind))
            return new WeaponSpecialRule(kind, param, token);

        return new WeaponSpecialRule(SpecialRuleKind.Unknown, null, token);
    }
}
