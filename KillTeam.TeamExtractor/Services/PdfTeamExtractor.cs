using System.Diagnostics;
using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using KillTeam.TeamExtractor.Models;

namespace KillTeam.TeamExtractor.Services;

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

    /// <summary>Initialises a new instance of <see cref="PdfTeamExtractor"/>.</summary>
    public PdfTeamExtractor(PdfWeaponTypeDetector weaponTypeDetector)
    {
        this._weaponTypeDetector = weaponTypeDetector;
    }

    /// <summary>Extracts a team from the PDFs in the given folder and returns the structured result.</summary>
    public ExtractedTeam Extract(string teamName, string teamFolder)
    {
        var datacardsPath = FindPdf(teamFolder, "*Datacards*");
        var factionEquipPath = FindPdf(teamFolder, "*Faction Equipment*");
        var universalEquipPath = FindPdf(teamFolder, "*Universal Equipment*");
        var factionRulesPath = FindPdf(teamFolder, "*Faction Rules*");
        var strategyPloysPath = FindPdf(teamFolder, "*Strategy Ploys*");
        var firefightPloysPath = FindPdf(teamFolder, "*Firefight Ploys*");
        var operativeSelectionPath = FindPdf(teamFolder, "*Operative Selection*");
        var supplementaryInfoPath = FindPdf(teamFolder, "*Supplementary Information*");

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

        var equipment = this.ParseEquipmentWithDescriptions(equipmentPaths);
        var factionRules = factionRulesPath != null ? this.ParseRulesDoc(factionRulesPath) : [];
        var strategyPloys = strategyPloysPath != null ? this.ParseRulesDoc(strategyPloysPath) : [];
        var firefightPloys = firefightPloysPath != null ? this.ParseRulesDoc(firefightPloysPath) : [];
        var operativeSelection = operativeSelectionPath != null ? this.ParseOperativeSelection(operativeSelectionPath) : null;
        var supplementaryInfo = supplementaryInfoPath != null ? this.ParseSupplementaryInfo(supplementaryInfoPath) : "";

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
            FactionRules = factionRules,
            StrategyPloys = strategyPloys,
            FirefightPloys = firefightPloys,
            OperativeSelection = operativeSelection,
            SupplementaryInfo = supplementaryInfo,
        };
    }

    // ─── Datacard parsing ────────────────────────────────────────────────────────

    private (List<ExtractedOperative> Operatives, string? Faction) ParseDatacards(
        string pdfPath,
        Dictionary<string, WeaponType> weaponTypes)
    {
        var lines = GetPdfLines(pdfPath);
        var count = lines.Count;
        var operatives = new List<ExtractedOperative>();

        // Tracks operative instances for back-of-card lookup
        var operativeMap = new Dictionary<string, ExtractedOperative>(StringComparer.OrdinalIgnoreCase);
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

            // Back-of-card page: operative already extracted — parse additional content
            if (processed.Contains(operativeName))
            {
                if (operativeMap.TryGetValue(operativeName, out var existingOp))
                {
                    i = this.ParseBackOfCard(lines, i, existingOp);
                }
                else
                {
                    i++;
                }

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

            // Seek weapon table header
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

            i++; // past table header

            var weapons = new List<ExtractedWeapon>();

            // Lines after the last weapon row accumulate as potential front-of-card ability text
            var afterWeaponLines = new List<string>();

            while (i < count)
            {
                var wLine = lines[i];

                if (StatsHeaderRegex().IsMatch(wLine))
                {
                    break;
                }

                if (FactionKeywordLineRegex().IsMatch(wLine) && wLine.Split(',').Length >= 3)
                {
                    break;
                }

                if (string.IsNullOrWhiteSpace(wLine)
                    || wLine.Contains("RULES CONTINUE")
                    || wLine.Contains("RULE CONTINUES"))
                {
                    i++;
                    continue;
                }

                var wm = WeaponRowRegex().Match(wLine);

                if (wm.Success)
                {
                    // A new weapon row resets the candidate ability lines collected so far
                    afterWeaponLines.Clear();

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
                else
                {
                    // Non-weapon line after the weapon table has started; candidate for abilities
                    if (weapons.Count > 0)
                    {
                        afterWeaponLines.Add(wLine);
                    }
                }

                i++;
            }

            // Parse front-of-card weapon rules (*Name: text) and abilities from lines after the last weapon row
            var frontWeaponRules = ExtractFrontWeaponRules(afterWeaponLines);
            var frontAbilities = ParseFrontAbilityLines(afterWeaponLines);

            // Parse keywords from the faction keyword line (currently at position i)
            var keywords = new List<string>();
            var primaryKeyword = "";

            if (i < count && FactionKeywordLineRegex().IsMatch(lines[i]) && lines[i].Split(',').Length >= 3)
            {
                var kwLine = lines[i].Trim();
                var parts = kwLine.Split(',').Select(p => p.Trim()).ToList();

                // Strip trailing page number from the last token
                if (parts.Count > 0)
                {
                    parts[^1] = PageNumberSuffixRegex().Replace(parts[^1], "").Trim();
                }

                keywords = parts
                    .Where(p => p.Length > 0)
                    .Select(p => ToTitleCase(p))
                    .ToList();

                primaryKeyword = keywords.FirstOrDefault() ?? "";

                // Third keyword (index 2) is the faction
                if (faction == null && keywords.Count >= 3)
                {
                    faction = keywords[2];
                }
            }

            if (weapons.Count > 0)
            {
                processed.Add(operativeName);

                var operative = new ExtractedOperative
                {
                    Name = operativeName,
                    Apl = apl,
                    Move = move,
                    Wounds = wounds,
                    Save = save,
                    Weapons = weapons,
                    Keywords = keywords,
                    PrimaryKeyword = primaryKeyword,
                    Abilities = frontAbilities,
                    WeaponRules = frontWeaponRules,
                };

                operatives.Add(operative);
                operativeMap[operativeName] = operative;
            }
        }

        return (operatives, faction);
    }

    /// <summary>
    /// Parses a back-of-card page for an already-processed operative.
    /// Skips the repeated name, stats, and weapon table, then appends
    /// abilities and weapon rules to the existing operative instance.
    /// Returns the index of the next unprocessed line.
    /// </summary>
    private int ParseBackOfCard(List<string> lines, int nameLineIdx, ExtractedOperative operative)
    {
        var count = lines.Count;
        var i = nameLineIdx + 1; // past the name line

        i = SkipBlankLines(lines, i);

        // Skip the repeated stats values line
        if (i < count && StatsValuesRegex().IsMatch(lines[i]))
        {
            i++;
        }

        i = SkipBlankLines(lines, i);

        // Skip the repeated weapon table if present
        if (i < count && WeaponTableHeaderRegex().IsMatch(lines[i]))
        {
            i++; // past table header

            while (i < count)
            {
                if (StatsHeaderRegex().IsMatch(lines[i]))
                {
                    return i;
                }

                if (string.IsNullOrWhiteSpace(lines[i]))
                {
                    i++;
                    continue;
                }

                if (WeaponRowRegex().IsMatch(lines[i]))
                {
                    i++;

                    // Skip any continuation lines
                    while (i < count
                           && lines[i].Length >= 15
                           && lines[i][..15].All(c => c == ' ')
                           && !ContinuationExcludeRegex().IsMatch(lines[i]))
                    {
                        i++;
                    }
                }
                else
                {
                    // Non-weapon, non-blank: back-of-card content begins
                    break;
                }
            }
        }

        // Collect back-of-card content lines until the next card's stats header
        var backLines = new List<string>();

        while (i < count)
        {
            if (StatsHeaderRegex().IsMatch(lines[i]))
            {
                break;
            }

            backLines.Add(lines[i]);
            i++;
        }

        ParseBackContent(backLines, operative);

        return i;
    }

    /// <summary>
    /// Parses back-of-card content lines and appends abilities and weapon rules
    /// to the given operative.  Handles three formats:
    /// <list type="bullet">
    ///   <item>Footnote weapon rules: lines starting with <c>*</c></item>
    ///   <item>Two-column 1AP actions: a header line containing "1AP" twice</item>
    ///   <item>Single-column passive rules: <c>Name: description</c> blocks</item>
    /// </list>
    /// </summary>
    private static void ParseBackContent(List<string> backLines, ExtractedOperative operative)
    {
        var j = 0;
        var count = backLines.Count;

        while (j < count)
        {
            var rawLine = backLines[j];
            var stripped = rawLine.TrimStart('\x07').TrimStart();

            if (string.IsNullOrWhiteSpace(stripped))
            {
                j++;
                continue;
            }

            // ── 1. Footnote weapon rule: starts with * ────────────────────────
            if (stripped.StartsWith('*'))
            {
                var ruleContent = stripped[1..];
                var colonIdx = ruleContent.IndexOf(':');

                if (colonIdx > 0)
                {
                    var ruleName = ruleContent[..colonIdx].Trim();
                    var ruleDesc = ruleContent[(colonIdx + 1)..].Trim();

                    j++;

                    while (j < count)
                    {
                        var nextLine = backLines[j].TrimStart('\x07').TrimStart();

                        if (string.IsNullOrWhiteSpace(nextLine) || nextLine.StartsWith('*'))
                        {
                            break;
                        }

                        ruleDesc = (ruleDesc + " " + nextLine).Trim();
                        j++;
                    }

                    operative.WeaponRules.Add(new ExtractedWeaponRule
                    {
                        Name = ruleName,
                        Text = ruleDesc,
                    });
                }
                else
                {
                    j++;
                }

                continue;
            }

            // ── 2. Two-column 1AP header: "1AP" appears at least twice ────────
            var firstApIdx = rawLine.IndexOf("1AP", StringComparison.Ordinal);

            if (firstApIdx >= 0)
            {
                var secondApIdx = rawLine.IndexOf("1AP", firstApIdx + 3, StringComparison.Ordinal);

                if (secondApIdx >= 0)
                {
                    // The column boundary is the start index of the first "1AP"
                    var boundary = firstApIdx;

                    var leftName = rawLine[..boundary].TrimStart('\x07').Trim();

                    var rightPart = rawLine[(firstApIdx + 3)..];
                    var apInRight = rightPart.IndexOf("1AP", StringComparison.Ordinal);

                    var rightName = apInRight >= 0
                        ? rightPart[..apInRight].Trim()
                        : rightPart.Trim();

                    var leftText = new StringBuilder();
                    var rightText = new StringBuilder();

                    j++;

                    while (j < count)
                    {
                        var contentLine = backLines[j];
                        var contentStripped = contentLine.TrimStart('\x07').TrimStart();

                        if (string.IsNullOrWhiteSpace(contentStripped))
                        {
                            j++;
                            continue;
                        }

                        if (contentStripped.StartsWith('*'))
                        {
                            break;
                        }

                        // Another two-column header ends this block
                        var cFirst = contentLine.IndexOf("1AP", StringComparison.Ordinal);

                        if (cFirst >= 0 && contentLine.IndexOf("1AP", cFirst + 3, StringComparison.Ordinal) >= 0)
                        {
                            break;
                        }

                        // Distribute characters to left or right column based on boundary position
                        var safeLen = Math.Min(boundary, contentLine.Length);
                        var leftPart = contentLine[..safeLen].TrimStart('\x07').Trim();
                        var rightPartC = contentLine.Length > boundary
                            ? contentLine[boundary..].Trim()
                            : "";

                        if (leftPart.Length > 0)
                        {
                            if (leftText.Length > 0)
                            {
                                leftText.Append(' ');
                            }

                            leftText.Append(leftPart);
                        }

                        if (rightPartC.Length > 0)
                        {
                            if (rightText.Length > 0)
                            {
                                rightText.Append(' ');
                            }

                            rightText.Append(rightPartC);
                        }

                        j++;
                    }

                    if (leftName.Length > 0)
                    {
                        operative.Abilities.Add(new ExtractedAbility
                        {
                            Name = StripControlChars(ToTitleCase(leftName)),
                            ApCost = 1,
                            Text = leftText.ToString().Trim(),
                        });
                    }

                    if (rightName.Length > 0)
                    {
                        operative.Abilities.Add(new ExtractedAbility
                        {
                            Name = StripControlChars(ToTitleCase(rightName)),
                            ApCost = 1,
                            Text = rightText.ToString().Trim(),
                        });
                    }

                    continue;
                }
            }

            // ── 3. Single-column passive rule: mixed-case Name: description ───
            var singleColonIdx = stripped.IndexOf(':');

            if (singleColonIdx > 0)
            {
                var possibleName = stripped[..singleColonIdx].Trim();

                if (IsAbilityName(possibleName))
                {
                    var text = stripped[(singleColonIdx + 1)..].Trim();

                    j++;

                    while (j < count)
                    {
                        var nextLine = backLines[j].TrimStart('\x07').TrimStart();

                        if (string.IsNullOrWhiteSpace(nextLine))
                        {
                            j++;
                            break;
                        }

                        if (nextLine.StartsWith('*'))
                        {
                            break;
                        }

                        var nextColon = nextLine.IndexOf(':');

                        if (nextColon > 0 && IsAbilityName(nextLine[..nextColon].Trim()))
                        {
                            break;
                        }

                        text = (text + " " + nextLine).Trim();
                        j++;
                    }

                    operative.Abilities.Add(new ExtractedAbility
                    {
                        Name = possibleName,
                        ApCost = null,
                        Text = text,
                    });

                    continue;
                }
            }

            j++;
        }
    }

    /// <summary>
    /// Extracts <c>*Name: description</c> footnote weapon rules from lines collected
    /// after the last weapon row on a front-of-card page.
    /// </summary>
    private static List<ExtractedWeaponRule> ExtractFrontWeaponRules(List<string> lines)
    {
        var result = new List<ExtractedWeaponRule>();
        var i = 0;
        var count = lines.Count;

        while (i < count)
        {
            var stripped = lines[i].TrimStart('\x07').TrimStart();

            if (!stripped.StartsWith('*'))
            {
                i++;
                continue;
            }

            var ruleContent = stripped[1..];
            var colonIdx = ruleContent.IndexOf(':');

            if (colonIdx > 0)
            {
                var ruleName = ruleContent[..colonIdx].Trim();
                var ruleDesc = ruleContent[(colonIdx + 1)..].Trim();

                i++;

                while (i < count)
                {
                    var next = lines[i].TrimStart('\x07').TrimStart();

                    if (string.IsNullOrWhiteSpace(next) || next.StartsWith('*'))
                    {
                        break;
                    }

                    ruleDesc = (ruleDesc + " " + next).Trim();
                    i++;
                }

                result.Add(new ExtractedWeaponRule
                {
                    Name = ruleName,
                    Text = ruleDesc,
                });
            }
            else
            {
                i++;
            }
        }

        return result;
    }

    /// <summary>
    /// into passive abilities (no AP cost) using the <c>Name: description</c> format.
    /// </summary>
    private static List<ExtractedAbility> ParseFrontAbilityLines(List<string> lines)
    {
        var abilities = new List<ExtractedAbility>();
        var j = 0;
        var count = lines.Count;

        while (j < count)
        {
            var line = lines[j].TrimStart('\x07').Trim();

            if (string.IsNullOrWhiteSpace(line))
            {
                j++;
                continue;
            }

            var colonIdx = line.IndexOf(':');

            if (colonIdx > 0)
            {
                var name = line[..colonIdx].Trim();

                if (IsAbilityName(name))
                {
                    var text = line[(colonIdx + 1)..].Trim();

                    j++;

                    while (j < count)
                    {
                        var nextLine = lines[j].TrimStart('\x07').Trim();

                        if (string.IsNullOrWhiteSpace(nextLine))
                        {
                            j++;
                            break;
                        }

                        var nextColon = nextLine.IndexOf(':');

                        if (nextColon > 0 && IsAbilityName(nextLine[..nextColon].Trim()))
                        {
                            break;
                        }

                        text = (text + " " + nextLine).Trim();
                        j++;
                    }

                    abilities.Add(new ExtractedAbility
                    {
                        Name = name,
                        ApCost = null,
                        Text = text,
                    });

                    continue;
                }
            }

            j++;
        }

        return abilities;
    }

    // ─── Equipment parsing ────────────────────────────────────────────────────────

    /// <summary>
    /// Parses equipment PDFs and returns items with their description text.
    /// Collects the description lines that follow each ALL CAPS item name.
    /// </summary>
    private List<ExtractedEquipmentItem> ParseEquipmentWithDescriptions(List<string> pdfPaths)
    {
        var result = new List<ExtractedEquipmentItem>();
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

                // Check the next non-blank line is not a section header (would mean this is a category label)
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

                if (!seen.Add(display))
                {
                    continue;
                }

                // Collect description lines until the next item name or section header
                var descParts = new List<string>();

                for (var k = j + 1; k < total; k++)
                {
                    var descLine = allLines[k].Trim();

                    if (string.IsNullOrWhiteSpace(descLine))
                    {
                        continue;
                    }

                    if (SectionHeaders.Contains(descLine))
                    {
                        break;
                    }

                    // Next ALL CAPS item name ends the description block
                    if (AllCapsEquipmentRegex().IsMatch(descLine) && !IsEquipmentSkip(descLine))
                    {
                        var stripped = QuantityPrefixRegex().Replace(descLine, "");

                        if (stripped.Length >= 4 && !WeaponTableHeaderRegex().IsMatch(descLine))
                        {
                            break;
                        }
                    }

                    descParts.Add(descLine);
                }

                result.Add(new ExtractedEquipmentItem
                {
                    Name = display,
                    Description = string.Join(" ", descParts).Trim(),
                });
            }
        }

        return result;
    }

    // ─── Rules document parsing ───────────────────────────────────────────────────

    /// <summary>
    /// Parses a rules PDF (Faction Rules, Strategy Ploys, or Firefight Ploys) and
    /// returns a list of named rules with their description text.
    ///
    /// The PDF format is:
    /// <code>
    ///         TEAM NAME       (indented — skip)
    ///         RULE TYPE       (indented — skip)
    /// RULE NAME IN ALL CAPS   (left-aligned — starts a new rule)
    /// Description text...
    /// </code>
    /// "CONTINUES ON OTHER SIDE" causes the description to merge with the next page.
    /// </summary>
    public List<ExtractedRule> ParseRulesDoc(string path)
    {
        var lines = GetPdfLines(path);
        var result = new List<ExtractedRule>();
        var currentName = "";
        var currentText = new StringBuilder();
        var pendingContinuation = false;

        foreach (var line in lines)
        {
            var trimmed = line.Trim();

            if (string.IsNullOrWhiteSpace(trimmed))
            {
                continue;
            }

            // Indented lines are team-name or rule-type headers — skip them
            if (line.StartsWith("    ", StringComparison.Ordinal) || line.StartsWith("\t", StringComparison.Ordinal))
            {
                continue;
            }

            // "CONTINUES ON OTHER SIDE" — the next page repeats the rule name and continues text
            if (trimmed.Contains("CONTINUES ON OTHER SIDE") || trimmed.Contains("CONTINUE ON OTHER SIDE"))
            {
                pendingContinuation = true;
                continue;
            }

            // ALL CAPS line — new rule name (or continuation of same rule after page turn)
            if (AllCapsRuleNameRegex().IsMatch(trimmed))
            {
                if (pendingContinuation)
                {
                    pendingContinuation = false;

                    // If the name matches (same rule continuing), keep accumulating
                    var candidate = ToTitleCase(trimmed);

                    if (!string.Equals(candidate, currentName, StringComparison.OrdinalIgnoreCase))
                    {
                        // Different name — save current and start fresh
                        if (currentName.Length > 0)
                        {
                            result.Add(new ExtractedRule { Name = currentName, Text = currentText.ToString().Trim() });
                        }

                        currentName = candidate;
                        currentText.Clear();
                    }
                }
                else
                {
                    // Save the previous rule
                    if (currentName.Length > 0)
                    {
                        result.Add(new ExtractedRule { Name = currentName, Text = currentText.ToString().Trim() });
                    }

                    currentName = ToTitleCase(trimmed);
                    currentText.Clear();
                }
            }
            else
            {
                // Description text for the current rule
                if (currentName.Length > 0)
                {
                    if (currentText.Length > 0)
                    {
                        currentText.Append(' ');
                    }

                    currentText.Append(trimmed);
                }
            }
        }

        // Flush the last rule
        if (currentName.Length > 0)
        {
            result.Add(new ExtractedRule { Name = currentName, Text = currentText.ToString().Trim() });
        }

        // Filter out fragment entries (all-caps sentence fragments with no description text)
        // and deduplicate by name (concatenate text if the same rule name appears on multiple pages)
        var deduped = new List<ExtractedRule>();
        var seen = new Dictionary<string, ExtractedRule>(StringComparer.OrdinalIgnoreCase);

        foreach (var rule in result.Where(r => r.Text.Length > 0))
        {
            if (seen.TryGetValue(rule.Name, out var existing))
            {
                var merged = new ExtractedRule
                {
                    Name = existing.Name,
                    Text = (existing.Text + " " + rule.Text).Trim(),
                };

                seen[rule.Name] = merged;
                deduped[deduped.IndexOf(existing)] = merged;
            }
            else
            {
                seen[rule.Name] = rule;
                deduped.Add(rule);
            }
        }

        return deduped;
    }

    // ─── Operative selection parsing ─────────────────────────────────────────────

    /// <summary>
    /// Parses an Operative Selection PDF, extracting the archetype and the selection rules text.
    /// </summary>
    public ExtractedOperativeSelection ParseOperativeSelection(string path)
    {
        var lines = GetPdfLines(path);
        var archetype = "";
        var textBuilder = new StringBuilder();
        var foundArchetype = false;

        foreach (var line in lines)
        {
            var trimmed = line.Trim();

            if (!foundArchetype)
            {
                var m = ArchetypeRegex().Match(trimmed);

                if (m.Success)
                {
                    archetype = m.Groups[1].Value.Trim();
                    foundArchetype = true;
                }
            }
            else
            {
                if (textBuilder.Length > 0)
                {
                    textBuilder.Append('\n');
                }

                textBuilder.Append(trimmed);
            }
        }

        return new ExtractedOperativeSelection
        {
            Archetype = archetype,
            Text = textBuilder.ToString().Trim(),
        };
    }

    // ─── Supplementary information parsing ───────────────────────────────────────

    /// <summary>
    /// Parses a Supplementary Information PDF and returns all text content joined with newlines.
    /// </summary>
    public string ParseSupplementaryInfo(string path)
    {
        var lines = GetPdfLines(path);

        return string.Join(
            '\n',
            lines
                .Select(l => l.Trim())
                .Where(l => l.Length > 0));
    }

    // ─── Shared helpers ───────────────────────────────────────────────────────────

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

    /// <summary>Returns true when the name is a plausible mixed-case ability name rather than an all-caps heading.</summary>
    private static bool IsAbilityName(string name)
    {
        return name.Length > 0 && name.Length < 60 && !AllCapsNameRegex().IsMatch(name);
    }

    /// <summary>
    /// Strips ASCII control characters (NUL–US, i.e. code points 0–31) from a name string.
    /// pdftotext emits characters such as BEL (0x07) and BS (0x08) as PDF rendering artefacts;
    /// these must be removed before storing names.
    /// </summary>
    private static string StripControlChars(string s)
    {
        return new string(s.Where(c => c >= 32).ToArray()).Trim();
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

    /// <summary>Converts a team name into a URL-safe slug used as the JSON file name and id field.</summary>
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

    // ─── Generated regexes ────────────────────────────────────────────────────────

    [GeneratedRegex(@"\bAPL\b.*\bMOVE\b.*\bSAVE\b.*\bWOUNDS\b")]
    private static partial Regex StatsHeaderRegex();

    [GeneratedRegex(@"^[A-Z][A-Z\s\-]+$")]
    private static partial Regex AllCapsNameRegex();

    /// <summary>Variant of AllCapsNameRegex that also permits apostrophes and digits for rule names.</summary>
    [GeneratedRegex(@"^[A-Z][A-Z0-9\s\-'']+$")]
    private static partial Regex AllCapsRuleNameRegex();

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

    [GeneratedRegex(@"ARCHETYPE:\s*(.+)", RegexOptions.IgnoreCase)]
    private static partial Regex ArchetypeRegex();
}
