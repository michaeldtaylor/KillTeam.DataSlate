using System.Text;

namespace KillTeam.TeamExtractor.Models;

/// <summary>The result of extracting a team from PDFs, before writing to YAML.</summary>
public class ExtractedTeam
{
    /// <summary>URL-safe slug derived from the team name, used as the YAML file name and id field.</summary>
    public required string Id { get; init; }

    /// <summary>The human-readable team name.</summary>
    public required string Name { get; init; }

    /// <summary>The grand faction (e.g. Imperium, Chaos, Aeldari) from the second keyword.</summary>
    public required string GrandFaction { get; init; }

    /// <summary>The team faction name in title case (e.g. Adeptus Astartes).</summary>
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
    public string SupplementaryInfo { get; init; } = string.Empty;

    /// <summary>
    /// Serialises the team to the YAML format used by the teams/ folder.
    /// Field order follows kt_extractor_parsing_rules.md Rule 6.
    /// All string values are normalised via <see cref="TextHelpers.NormaliseText"/>.
    /// Name fields use title case via <see cref="TextHelpers.ToTitleCase"/>.
    /// </summary>
    public string ToYaml()
    {
        var sb = new StringBuilder();

        // ── Metadata header ────────────────────────────────────────────────────
        YamlWriter.WriteKeyValue(sb, 0, "id", N(Id));
        YamlWriter.WriteKeyValue(sb, 0, "name", N(Name));
        YamlWriter.WriteKeyValue(sb, 0, "grandFaction", N(GrandFaction));
        YamlWriter.WriteKeyValue(sb, 0, "faction", N(Faction));

        // ── datacards ──────────────────────────────────────────────────────────
        sb.AppendLine("datacards:");

        foreach (var datacard in Datacards)
        {
            WriteDatacard(sb, datacard);
        }

        // ── factionEquipment ───────────────────────────────────────────────────
        if (FactionEquipment.Count > 0)
        {
            sb.AppendLine("factionEquipment:");

            foreach (var e in FactionEquipment)
            {
                WriteEquipmentItem(sb, e);
            }
        }

        // ── factionRules ───────────────────────────────────────────────────────
        if (FactionRules.Count > 0)
        {
            sb.AppendLine("factionRules:");

            foreach (var r in FactionRules)
            {
                WriteNamedRule(sb, r);
            }
        }

        // ── firefightPloys ─────────────────────────────────────────────────────
        if (FirefightPloys.Count > 0)
        {
            sb.AppendLine("firefightPloys:");

            foreach (var r in FirefightPloys)
            {
                WriteNamedRule(sb, r);
            }
        }

        // ── operativeSelection ─────────────────────────────────────────────────
        if (OperativeSelection != null)
        {
            sb.AppendLine("operativeSelection:");
            YamlWriter.WriteKeyValue(sb, 2, "archetype", N(OperativeSelection.Archetype));
            YamlWriter.WriteTextField(sb, 2, "text", N(OperativeSelection.Text));
        }

        // ── strategyPloys ──────────────────────────────────────────────────────
        if (StrategyPloys.Count > 0)
        {
            sb.AppendLine("strategyPloys:");

            foreach (var r in StrategyPloys)
            {
                WriteNamedRule(sb, r);
            }
        }

        // ── supplementaryInfo ──────────────────────────────────────────────────
        if (!string.IsNullOrEmpty(SupplementaryInfo))
        {
            YamlWriter.WriteTextField(sb, 0, "supplementaryInformation", N(SupplementaryInfo));
        }

        // ── universalEquipment ─────────────────────────────────────────────────
        if (UniversalEquipment.Count > 0)
        {
            sb.AppendLine("universalEquipment:");

            foreach (var e in UniversalEquipment)
            {
                WriteEquipmentItem(sb, e);
            }
        }

        return sb.ToString();
    }

    // ─── Section writers ──────────────────────────────────────────────────────

    private static void WriteNamedRule(StringBuilder sb, ExtractedRule rule)
    {
        if (rule.Category != null)
        {
            sb.AppendLine("  - category: " + YamlWriter.Scalar(T(N(rule.Category))));
            sb.AppendLine("    name: " + YamlWriter.Scalar(T(N(rule.Name))));
        }
        else
        {
            sb.AppendLine("  - name: " + YamlWriter.Scalar(T(N(rule.Name))));
        }

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

    private static void WriteDatacard(StringBuilder sb, ExtractedOperative operative)
    {
        // First item in the operative mapping uses "- " prefix
        sb.AppendLine("  - name: " + YamlWriter.Scalar(T(N(operative.Name))));
        sb.AppendLine("    operativeType: " + YamlWriter.Scalar(T(N(operative.Name))));
        sb.AppendLine("    primaryKeyword: " + YamlWriter.Scalar(T(N(operative.PrimaryKeyword))));

        if (operative.Keywords.Count > 0)
        {
            sb.AppendLine("    keywords:");

            foreach (var kw in operative.Keywords)
            {
                sb.AppendLine("      - " + YamlWriter.Scalar(T(N(kw))));
            }
        }

        sb.AppendLine("    stats:");
        YamlWriter.WriteKeyInt(sb, 6, "apl", operative.Apl);
        YamlWriter.WriteKeyInt(sb, 6, "move", operative.Move);
        sb.AppendLine("      save: " + YamlWriter.Scalar(N(operative.Save)));
        YamlWriter.WriteKeyInt(sb, 6, "wounds", operative.Wounds);

        sb.AppendLine("    weapons:");

        foreach (var w in operative.Weapons)
        {
            WriteWeapon(sb, w);
        }

        if (operative.Abilities.Count > 0)
        {
            sb.AppendLine("    abilities:");

            foreach (var a in operative.Abilities)
            {
                WriteAbility(sb, a);
            }
        }

        if (operative.SpecialActions.Count > 0)
        {
            sb.AppendLine("    specialActions:");

            foreach (var a in operative.SpecialActions)
            {
                WriteSpecialAction(sb, a);
            }
        }

        if (operative.SpecialRules.Count > 0)
        {
            sb.AppendLine("    specialRules:");

            foreach (var sr in operative.SpecialRules)
            {
                sb.AppendLine("      - name: " + YamlWriter.Scalar(T(N(sr.Name))));
                YamlWriter.WriteTextField(sb, 8, "text", N(sr.Text));
            }
        }
    }

    private static void WriteWeapon(StringBuilder sb, ExtractedWeapon w)
    {
        sb.AppendLine("      - name: " + YamlWriter.Scalar(N(w.Name)));
        sb.AppendLine("        weaponType: " + YamlWriter.Scalar(w.Type.ToString()));
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

