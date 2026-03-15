using System.Text;

namespace KillTeam.TeamExtractor.Models;

/// <summary>The result of extracting a team from PDFs, before writing to YAML.</summary>
public class ExtractedTeam
{
    /// <summary>URL-safe slug derived from the team name, used as the YAML file name and id field.</summary>
    public required string Id { get; init; }

    /// <summary>The human-readable team name.</summary>
    public required string Name { get; init; }

    /// <summary>The team faction name in title case.</summary>
    public required string Faction { get; init; }

    /// <summary>Operatives extracted from the datacards PDF.</summary>
    public required List<ExtractedOperative> Datacards { get; init; }

    /// <summary>Equipment items with descriptions extracted from the Faction Equipment PDF.</summary>
    public List<ExtractedEquipmentItem> FactionEquipment { get; init; } = [];

    /// <summary>Equipment items with descriptions extracted from the Universal Equipment PDF.</summary>
    public List<ExtractedEquipmentItem> UniversalEquipment { get; init; } = [];

    /// <summary>Faction-specific rules extracted from the Faction Rules PDF.</summary>
    public List<ExtractedRule> FactionRules { get; init; } = [];

    /// <summary>Strategy ploys extracted from the Strategy Ploys PDF.</summary>
    public List<ExtractedRule> StrategyPloys { get; init; } = [];

    /// <summary>Firefight ploys extracted from the Firefight Ploys PDF.</summary>
    public List<ExtractedRule> FirefightPloys { get; init; } = [];

    /// <summary>Operative selection rules, if a selection PDF was found.</summary>
    public ExtractedOperativeSelection? OperativeSelection { get; init; }

    /// <summary>Supplementary information text, if a supplementary PDF was found.</summary>
    public string SupplementaryInfo { get; init; } = "";

    /// <summary>
    /// Serialises the team to the YAML format used by the teams/ folder.
    /// Field order follows PARSING_RULES.md Rule 6.
    /// All string values are normalised via <see cref="TextHelpers.NormaliseText"/>.
    /// Name fields use title case via <see cref="TextHelpers.ToTitleCase"/>.
    /// </summary>
    public string ToYaml()
    {
        var sb = new StringBuilder();

        // ── Metadata header ────────────────────────────────────────────────────
        YamlWriter.WriteKeyValue(sb, 0, "id", N(this.Id));
        YamlWriter.WriteKeyValue(sb, 0, "name", N(this.Name));
        YamlWriter.WriteKeyValue(sb, 0, "faction", N(this.Faction));

        // ── datacards ──────────────────────────────────────────────────────────
        sb.AppendLine("datacards:");

        foreach (var op in this.Datacards)
        {
            WriteDatacard(sb, op);
        }

        // ── factionEquipment ───────────────────────────────────────────────────
        if (this.FactionEquipment.Count > 0)
        {
            sb.AppendLine("factionEquipment:");

            foreach (var e in this.FactionEquipment)
            {
                WriteEquipmentItem(sb, e);
            }
        }

        // ── factionRules ───────────────────────────────────────────────────────
        if (this.FactionRules.Count > 0)
        {
            sb.AppendLine("factionRules:");

            foreach (var r in this.FactionRules)
            {
                WriteNamedRule(sb, r);
            }
        }

        // ── firefightPloys ─────────────────────────────────────────────────────
        if (this.FirefightPloys.Count > 0)
        {
            sb.AppendLine("firefightPloys:");

            foreach (var r in this.FirefightPloys)
            {
                WriteNamedRule(sb, r);
            }
        }

        // ── operativeSelection ─────────────────────────────────────────────────
        if (this.OperativeSelection != null)
        {
            sb.AppendLine("operativeSelection:");
            YamlWriter.WriteKeyValue(sb, 2, "archetype", N(this.OperativeSelection.Archetype));
            YamlWriter.WriteTextField(sb, 2, "text", N(this.OperativeSelection.Text));
        }

        // ── strategyPloys ──────────────────────────────────────────────────────
        if (this.StrategyPloys.Count > 0)
        {
            sb.AppendLine("strategyPloys:");

            foreach (var r in this.StrategyPloys)
            {
                WriteNamedRule(sb, r);
            }
        }

        // ── supplementaryInfo ──────────────────────────────────────────────────
        if (!string.IsNullOrEmpty(this.SupplementaryInfo))
        {
            YamlWriter.WriteTextField(sb, 0, "supplementaryInfo", N(this.SupplementaryInfo));
        }

        // ── universalEquipment ─────────────────────────────────────────────────
        if (this.UniversalEquipment.Count > 0)
        {
            sb.AppendLine("universalEquipment:");

            foreach (var e in this.UniversalEquipment)
            {
                WriteEquipmentItem(sb, e);
            }
        }

        return sb.ToString();
    }

    // ─── Section writers ──────────────────────────────────────────────────────

    private static void WriteNamedRule(StringBuilder sb, ExtractedRule rule)
    {
        sb.AppendLine("  - name: " + YamlWriter.Scalar(T(N(rule.Name))));

        var text = N(rule.Text);

        if (text.Contains('\n'))
        {
            YamlWriter.WriteLiteralBlock(sb, 4, "text", text);
        }
        else
        {
            sb.AppendLine("    text: " + YamlWriter.Scalar(text));
        }
    }

    private static void WriteEquipmentItem(StringBuilder sb, ExtractedEquipmentItem item)
    {
        sb.AppendLine("  - name: " + YamlWriter.Scalar(T(N(item.Name))));

        var text = M(item.Text);

        if (text.Length > 0)
        {
            if (text.Contains('\n'))
            {
                YamlWriter.WriteLiteralBlock(sb, 4, "text", text);
            }
            else
            {
                sb.AppendLine("    text: " + YamlWriter.Scalar(text));
            }
        }
    }

    private static void WriteDatacard(StringBuilder sb, ExtractedOperative op)
    {
        // First item in the operative mapping uses "- " prefix
        sb.AppendLine("  - name: " + YamlWriter.Scalar(T(N(op.Name))));
        sb.AppendLine("    operativeType: " + YamlWriter.Scalar(T(N(op.Name))));
        sb.AppendLine("    primaryKeyword: " + YamlWriter.Scalar(T(N(op.PrimaryKeyword))));

        if (op.Keywords.Count > 0)
        {
            sb.AppendLine("    keywords:");

            foreach (var kw in op.Keywords)
            {
                sb.AppendLine("      - " + YamlWriter.Scalar(T(N(kw))));
            }
        }

        sb.AppendLine("    stats:");
        YamlWriter.WriteKeyInt(sb, 6, "move", op.Move);
        YamlWriter.WriteKeyInt(sb, 6, "apl", op.Apl);
        YamlWriter.WriteKeyInt(sb, 6, "wounds", op.Wounds);
        sb.AppendLine("      save: " + YamlWriter.Scalar(N(op.Save)));

        sb.AppendLine("    weapons:");

        foreach (var w in op.Weapons)
        {
            WriteWeapon(sb, w);
        }

        if (op.Abilities.Count > 0)
        {
            sb.AppendLine("    abilities:");

            foreach (var a in op.Abilities)
            {
                WriteAbility(sb, a);
            }
        }

        if (op.SpecialActions.Count > 0)
        {
            sb.AppendLine("    specialActions:");

            foreach (var a in op.SpecialActions)
            {
                WriteSpecialAction(sb, a);
            }
        }

        if (op.SpecialRules.Count > 0)
        {
            sb.AppendLine("    specialRules:");

            foreach (var sr in op.SpecialRules)
            {
                sb.AppendLine("      - name: " + YamlWriter.Scalar(T(N(sr.Name))));
                sb.AppendLine("        text: " + YamlWriter.Scalar(N(sr.Text)));
            }
        }
    }

    private static void WriteWeapon(StringBuilder sb, ExtractedWeapon w)
    {
        sb.AppendLine("      - name: " + YamlWriter.Scalar(N(w.Name)));
        sb.AppendLine("        type: " + YamlWriter.Scalar(w.Type.ToString()));
        YamlWriter.WriteKeyInt(sb, 8, "atk", w.Atk);
        sb.AppendLine("        hit: " + YamlWriter.Scalar(N(w.Hit)));
        sb.AppendLine("        dmg:");
        YamlWriter.WriteKeyInt(sb, 10, "normal", w.DmgNormal);
        YamlWriter.WriteKeyInt(sb, 10, "crit", w.DmgCrit);

        if (w.WeaponRules.Count > 0)
        {
            sb.AppendLine("        weaponRules:");

            foreach (var rule in w.WeaponRules)
            {
                sb.AppendLine("          - " + YamlWriter.Scalar(N(rule)));
            }
        }
    }

    private static void WriteAbility(StringBuilder sb, ExtractedAbility a)
    {
        sb.AppendLine("      - name: " + YamlWriter.Scalar(T(N(a.Name))));

        var text = N(a.Text);

        if (text.Contains('\n'))
        {
            YamlWriter.WriteLiteralBlock(sb, 8, "text", text);
        }
        else
        {
            sb.AppendLine("        text: " + YamlWriter.Scalar(text));
        }

        // No apCost: abilities are passive rules only
    }

    /// <summary>Writes a single special action (active, has AP cost) as a YAML mapping item.</summary>
    private static void WriteSpecialAction(StringBuilder sb, ExtractedAbility a)
    {
        sb.AppendLine("      - name: " + YamlWriter.Scalar(T(N(a.Name))));

        var text = N(a.Text);

        if (text.Contains('\n'))
        {
            YamlWriter.WriteLiteralBlock(sb, 8, "text", text);
        }
        else
        {
            sb.AppendLine("        text: " + YamlWriter.Scalar(text));
        }

        // apCost is required for specialActions
        YamlWriter.WriteKeyInt(sb, 8, "apCost", a.ApCost ?? 1);
    }

    // ─── Helpers ──────────────────────────────────────────────────────────────

    /// <summary>Applies <see cref="TextHelpers.NormaliseText"/> — short alias for readability.</summary>
    private static string N(string s) => TextHelpers.NormaliseText(s);

    /// <summary>Applies <see cref="TextHelpers.ToTitleCase"/> — short alias for readability.</summary>
    private static string T(string s) => TextHelpers.ToTitleCase(s);

    /// <summary>Applies <see cref="TextHelpers.StructureToMarkdown"/> — short alias for readability.</summary>
    private static string M(string s) => TextHelpers.StructureToMarkdown(s);
}
