using System.Diagnostics;
using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using KillTeam.DataSlate.Domain.Models;

namespace KillTeam.DataSlate.Console.Services;

/// <summary>
/// Extracts Kill Team operative data from official GW PDF sources.
/// Uses pdftotext for text layout extraction and PdfPig for weapon type detection.
/// </summary>
public partial class PdfTeamExtractor
{
    private static readonly HashSet<string> SectionHeaders = new(StringComparer.Ordinal)
    {
        "FACTION EQUIPMENT",
        "UNIVERSAL EQUIPMENT",
    };

    private static readonly string[] EquipmentSkipPatterns =
    [
        @"^RULES? CONTINUES? ON OTHER SIDE$",
        @".*\d+AP$",
        @"^(NAME|ATK|HIT|DMG|WR|APL|MOVE|SAVE|WOUNDS)$",
    ];

    private readonly PdfWeaponTypeDetector _weaponTypeDetector;

    public PdfTeamExtractor(PdfWeaponTypeDetector weaponTypeDetector)
    {
        this._weaponTypeDetector = weaponTypeDetector;
    }

    /// <summary>Extracts a team from the PDFs in the given folder and returns the JSON string.</summary>
    public ExtractedTeam Extract(string teamName, string teamFolder)
    {
        var datacardsPath = FindPdf(teamFolder, "*Datacards*");
        var factionEquipPath = FindPdf(teamFolder, "*Faction Equipment*");
        var universalEquipPath = FindPdf(teamFolder, "*Universal Equipment*");

        if (datacardsPath == null)
        {
            throw new InvalidOperationException($"No Datacards PDF found in {teamFolder}");
        }

        var weaponTypes = this._weaponTypeDetector.Detect(datacardsPath);
        var (operatives, faction) = this.ParseDatacards(datacardsPath, weaponTypes);
        var equipmentPaths = new List<string>();

        if (factionEquipPath != null)
        {
            equipmentPaths.Add(factionEquipPath);
        }

        if (universalEquipPath != null)
        {
            equipmentPaths.Add(universalEquipPath);
        }

        var equipment = this.ParseEquipment(equipmentPaths);

        if (operatives.Count == 0)
        {
            throw new InvalidOperationException(
                $"No operatives extracted from '{teamName}'. The PDF layout may differ from expected.");
        }

        return new ExtractedTeam
        {
            Id = Slugify(teamName),
            Name = teamName,
            Faction = faction ?? "UNKNOWN — UPDATE ME",
            Operatives = operatives,
            Equipment = equipment,
        };
    }

    private (List<ExtractedOperative> Operatives, string? Faction) ParseDatacards(
        string pdfPath,
        Dictionary<string, WeaponType> weaponTypes)
    {
        var lines = GetPdfLines(pdfPath);
        var count = lines.Count;
        var operatives = new List<ExtractedOperative>();
        var processed = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        string? faction = null;
        var i = 0;

        while (i < count)
        {
            if (!StatsHeaderRegex().IsMatch(lines[i]))
            {
                i++;
                continue;
            }

            i++;
            i = SkipBlankLines(lines, i);

            if (i >= count)
            {
                break;
            }

            var nameRaw = lines[i].Trim();

            if (!AllCapsNameRegex().IsMatch(nameRaw))
            {
                i++;
                continue;
            }

            var operativeName = ToTitleCase(nameRaw);

            if (processed.Contains(operativeName))
            {
                i++;
                continue;
            }

            i++;
            i = SkipBlankLines(lines, i);

            if (i >= count)
            {
                break;
            }

            var statsLine = lines[i];

            if (StatsLineSplitRegex().IsMatch(statsLine) && i + 1 < count && lines[i + 1].TrimStart().StartsWith('+'))
            {
                statsLine += lines[i + 1];
                i++;
            }

            var (apl, move, save, wounds) = ParseStats(statsLine);

            i++;

            var foundTable = false;

            while (i < count)
            {
                if (WeaponTableHeaderRegex().IsMatch(lines[i]))
                {
                    foundTable = true;
                    break;
                }

                if (StatsHeaderRegex().IsMatch(lines[i]))
                {
                    i--;
                    break;
                }

                i++;
            }

            if (foundTable == false)
            {
                continue;
            }

            i++;

            var weapons = new List<ExtractedWeapon>();

            while (i < count)
            {
                var wLine = lines[i];

                if (StatsHeaderRegex().IsMatch(wLine))
                {
                    break;
                }

                if (FactionKeywordLineRegex().IsMatch(wLine) && wLine.Split(',').Length >= 3)
                {
                    if (faction == null)
                    {
                        var parts = wLine.Trim().Split(',');

                        if (parts.Length >= 3)
                        {
                            var raw = PageNumberSuffixRegex().Replace(parts[2].Trim(), "");
                            faction = ToTitleCase(raw.Trim());
                        }
                    }

                    break;
                }

                if (string.IsNullOrWhiteSpace(wLine) || wLine.Contains("RULES CONTINUE") || wLine.Contains("RULE CONTINUES"))
                {
                    i++;
                    continue;
                }

                var wm = WeaponRowRegex().Match(wLine);

                if (wm.Success)
                {
                    var wName = wm.Groups[1].Value.Trim().TrimStart('\x07').Trim();
                    var wAtk = int.Parse(wm.Groups[2].Value, CultureInfo.InvariantCulture);
                    var wHit = wm.Groups[3].Value + "+";
                    var wDmg = $"{wm.Groups[4].Value}/{wm.Groups[5].Value}";
                    var wRules = wm.Groups[6].Value.Trim();

                    while (i + 1 < count)
                    {
                        var next = lines[i + 1];

                        if (next.Length >= 15 && next[..15].All(c => c == ' ') && !ContinuationExcludeRegex().IsMatch(next))
                        {
                            wRules = (wRules + " " + next.Trim()).Trim();
                            i++;
                        }
                        else
                        {
                            break;
                        }
                    }

                    if (wRules == "-")
                    {
                        wRules = "";
                    }

                    var weaponType = ResolveWeaponType(wName, wRules, weaponTypes);

                    weapons.Add(new ExtractedWeapon
                    {
                        Name = wName,
                        Type = weaponType,
                        Atk = wAtk,
                        Hit = wHit,
                        Dmg = wDmg,
                        SpecialRules = wRules,
                    });
                }

                i++;
            }

            if (weapons.Count > 0)
            {
                processed.Add(operativeName);

                operatives.Add(new ExtractedOperative
                {
                    Name = operativeName,
                    Apl = apl,
                    Move = move,
                    Wounds = wounds,
                    Save = save,
                    Weapons = weapons,
                });
            }
        }

        return (operatives, faction);
    }

    private List<string> ParseEquipment(List<string> pdfPaths)
    {
        var result = new List<string>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var pdfPath in pdfPaths)
        {
            var allLines = GetPdfLines(pdfPath);
            var total = allLines.Count;

            for (var j = 0; j < total; j++)
            {
                var trimmed = allLines[j].Trim();

                if (trimmed.Length < 4)
                {
                    continue;
                }

                if (SectionHeaders.Contains(trimmed))
                {
                    continue;
                }

                if (!AllCapsEquipmentRegex().IsMatch(trimmed))
                {
                    continue;
                }

                if (WeaponTableHeaderRegex().IsMatch(trimmed))
                {
                    continue;
                }

                if (IsEquipmentSkip(trimmed))
                {
                    continue;
                }

                var nextNonBlank = "";

                for (var k = j + 1; k < total; k++)
                {
                    var kLine = allLines[k].Trim();

                    if (kLine.Length > 0)
                    {
                        nextNonBlank = kLine;
                        break;
                    }
                }

                if (SectionHeaders.Contains(nextNonBlank))
                {
                    continue;
                }

                var itemName = QuantityPrefixRegex().Replace(trimmed, "");

                if (itemName.Length < 4)
                {
                    continue;
                }

                var display = ToTitleCase(itemName);

                if (seen.Add(display))
                {
                    result.Add(display);
                }
            }
        }

        return result;
    }

    private static WeaponType ResolveWeaponType(
        string weaponName,
        string specialRules,
        Dictionary<string, WeaponType> detectedTypes)
    {
        if (detectedTypes.TryGetValue(weaponName, out var detected))
        {
            return detected;
        }

        return RangeInRulesRegex().IsMatch(specialRules) ? WeaponType.Ranged : WeaponType.Melee;
    }

    private static (int Apl, int Move, string Save, int Wounds) ParseStats(string statsLine)
    {
        var m = StatsValuesRegex().Match(statsLine);

        if (m.Success)
        {
            return (
                int.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture),
                int.Parse(m.Groups[2].Value, CultureInfo.InvariantCulture),
                $"{m.Groups[3].Value}+",
                int.Parse(m.Groups[4].Value, CultureInfo.InvariantCulture)
            );
        }

        return (0, 0, "?", 0);
    }

    private static bool IsEquipmentSkip(string text)
    {
        return EquipmentSkipPatterns.Any(p => Regex.IsMatch(text, p, RegexOptions.IgnoreCase));
    }

    private static List<string> GetPdfLines(string pdfPath)
    {
        var psi = new ProcessStartInfo
        {
            FileName = "pdftotext",
            ArgumentList = { "-layout", pdfPath, "-" },
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            StandardOutputEncoding = Encoding.UTF8,
        };

        using var process = Process.Start(psi)
            ?? throw new InvalidOperationException("Failed to start pdftotext. Is Poppler installed?");

        var lines = new List<string>();

        while (process.StandardOutput.ReadLine() is { } line)
        {
            lines.Add(line.Replace("\f", ""));
        }

        process.WaitForExit();

        if (process.ExitCode != 0)
        {
            throw new InvalidOperationException(
                $"pdftotext failed with exit code {process.ExitCode} for '{pdfPath}'");
        }

        return lines;
    }

    private static int SkipBlankLines(List<string> lines, int i)
    {
        while (i < lines.Count && string.IsNullOrWhiteSpace(lines[i]))
        {
            i++;
        }

        return i;
    }

    private static string? FindPdf(string folder, string pattern)
    {
        return Directory.GetFiles(folder, pattern).FirstOrDefault();
    }

    public static string Slugify(string name)
    {
        return name.ToLowerInvariant()
            .Replace(" ", "-")
            .Replace("'", "")
            .Replace("(", "")
            .Replace(")", "");
    }

    private static string ToTitleCase(string text)
    {
        return CultureInfo.InvariantCulture.TextInfo.ToTitleCase(text.ToLower());
    }

    [GeneratedRegex(@"\bAPL\b.*\bMOVE\b.*\bSAVE\b.*\bWOUNDS\b")]
    private static partial Regex StatsHeaderRegex();

    [GeneratedRegex(@"^[A-Z][A-Z\s\-]+$")]
    private static partial Regex AllCapsNameRegex();

    [GeneratedRegex(@"\bNAME\b.*\bATK\b.*\bHIT\b.*\bDMG\b")]
    private static partial Regex WeaponTableHeaderRegex();

    [GeneratedRegex(@"^[A-Z0-9\s,\-]+$")]
    private static partial Regex FactionKeywordLineRegex();

    [GeneratedRegex(@"\s*\d+\s*$")]
    private static partial Regex PageNumberSuffixRegex();

    [GeneratedRegex(@"^[\s\x07]*(.+?)\s{2,}(\d+)\s+(\d+)\+\s+(\d+)/(\d+)(.*)")]
    private static partial Regex WeaponRowRegex();

    [GeneratedRegex(@"\d+/\d+")]
    private static partial Regex ContinuationExcludeRegex();

    [GeneratedRegex(@"^(\d+X\s+)?[A-Z][A-Z\s\-']+$")]
    private static partial Regex AllCapsEquipmentRegex();

    [GeneratedRegex(@"^\d+X\s+", RegexOptions.IgnoreCase)]
    private static partial Regex QuantityPrefixRegex();

    [GeneratedRegex(@"\bRange\b")]
    private static partial Regex RangeInRulesRegex();

    [GeneratedRegex(@"(\d+)\s+(\d+)[""'\s]+(\d+)\s*\+\s+(\d+)")]
    private static partial Regex StatsValuesRegex();

    [GeneratedRegex(@"\d+\s*$")]
    private static partial Regex StatsLineSplitRegex();
}

/// <summary>The result of extracting a team from PDFs, before writing to JSON.</summary>
public class ExtractedTeam
{
    public required string Id { get; init; }

    public required string Name { get; init; }

    public required string Faction { get; init; }

    public required List<ExtractedOperative> Operatives { get; init; }

    public required List<string> Equipment { get; init; }

    /// <summary>Serialises the team to the JSON format used by the teams/ folder.</summary>
    public string ToJson()
    {
        var root = new JsonObject
        {
            ["$schema"] = "../schema/team.schema.json",
            ["id"] = this.Id,
            ["name"] = this.Name,
            ["faction"] = this.Faction,
            ["operatives"] = new JsonArray(this.Operatives.Select(op => (JsonNode)new JsonObject
            {
                ["name"] = op.Name,
                ["operativeType"] = op.Name,
                ["stats"] = new JsonObject
                {
                    ["move"] = op.Move,
                    ["apl"] = op.Apl,
                    ["wounds"] = op.Wounds,
                    ["save"] = op.Save,
                },
                ["weapons"] = new JsonArray(op.Weapons.Select(w => (JsonNode)new JsonObject
                {
                    ["name"] = w.Name,
                    ["type"] = w.Type.ToString(),
                    ["atk"] = w.Atk,
                    ["hit"] = w.Hit,
                    ["dmg"] = w.Dmg,
                    ["specialRules"] = w.SpecialRules,
                }).ToArray()),
                ["equipment"] = new JsonArray(this.Equipment.Select(e => (JsonNode)JsonValue.Create(e)!).ToArray()),
            }).ToArray()),
        };

        return root.ToJsonString(new JsonSerializerOptions { WriteIndented = true });
    }
}

/// <summary>An operative extracted from a datacards PDF.</summary>
public class ExtractedOperative
{
    public required string Name { get; init; }

    public int Move { get; init; }

    public int Apl { get; init; }

    public int Wounds { get; init; }

    public required string Save { get; init; }

    public List<ExtractedWeapon> Weapons { get; init; } = [];
}

/// <summary>A weapon extracted from a datacards PDF row.</summary>
public class ExtractedWeapon
{
    public required string Name { get; init; }

    public WeaponType Type { get; init; }

    public int Atk { get; init; }

    public required string Hit { get; init; }

    public required string Dmg { get; init; }

    public string SpecialRules { get; init; } = "";
}
