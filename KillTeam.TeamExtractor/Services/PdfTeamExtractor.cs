using System.Diagnostics;
using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using KillTeam.TeamExtractor.Models;
using KillTeam.TeamExtractor;

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

    /// <summary>
    /// ALL-CAPS tokens that appear in raw-mode pdftotext output but are stats labels or
    /// page markers, not operative names.  These must be excluded when scanning for
    /// operative name boundaries in <see cref="BuildRawBackCardSections"/>.
    /// </summary>
    private static readonly HashSet<string> RawModeStatsKeywords = new(StringComparer.OrdinalIgnoreCase)
    {
        "SAVE", "MOVE", "WOUNDS", "APL", "APL WOUNDS", "APL MOVE",
        "MOVE SAVE", "WOUNDS SAVE", "APL MOVE SAVE WOUNDS",
        "OPERATIVES",
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

        var factionEquipment = factionEquipPath != null
            ? this.ParseEquipmentWithDescriptions([factionEquipPath])
            : [];
        var universalEquipment = universalEquipPath != null
            ? this.ParseEquipmentWithDescriptions([universalEquipPath])
            : [];
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
            Datacards = operatives,
            FactionEquipment = factionEquipment,
            UniversalEquipment = universalEquipment,
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
        // Layout mode is required for the weapon-stats regex (column-aligned positions).
        var lines = GetPdfLines(pdfPath);
        // Raw mode is used for back-card prose content — avoids two-column interleaving.
        var rawLines = GetPdfLines(pdfPath, raw: true);
        var rawBackSections = BuildRawBackCardSections(rawLines);

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
                    i = this.ParseBackOfCard(lines, rawBackSections, i, existingOp);
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
                    var wDmgNormal = int.Parse(wm.Groups[4].Value, CultureInfo.InvariantCulture);
                    var wDmgCrit = int.Parse(wm.Groups[5].Value, CultureInfo.InvariantCulture);
                    var wRulesRaw = StripControlChars(wm.Groups[6].Value.Trim());

                    while (i + 1 < count)
                    {
                        var next = lines[i + 1];

                        if (next.Length >= 15 && next[..15].All(c => c == ' ') && !ContinuationExcludeRegex().IsMatch(next))
                        {
                            wRulesRaw = (wRulesRaw + " " + next.Trim()).Trim();
                            i++;
                        }
                        else
                        {
                            break;
                        }
                    }

                    if (wRulesRaw == "-")
                    {
                        wRulesRaw = "";
                    }

                    var wRules = wRulesRaw
                        .Split(',')
                        .Select(r => r.Trim())
                        .Where(r => r.Length > 0 && r != "-")
                        .ToList();

                    var weaponType = ResolveWeaponType(wName, wRulesRaw, weaponTypes);

                    weapons.Add(new ExtractedWeapon
                    {
                        Name = wName,
                        Type = weaponType,
                        Atk = wAtk,
                        Hit = wHit,
                        DmgNormal = wDmgNormal,
                        DmgCrit = wDmgCrit,
                        WeaponRules = wRules,
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
                    SpecialRules = frontWeaponRules,
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
    private int ParseBackOfCard(
        List<string> lines,
        IReadOnlyDictionary<string, List<string>> rawBackSections,
        int nameLineIdx,
        ExtractedOperative operative)
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

        // Prefer raw-mode lines for back-card content to avoid two-column interleaving.
        // Fall back to layout-mode lines when no raw section was found for this operative.
        var contentLines = rawBackSections.TryGetValue(operative.Name, out var rawBackLines)
            ? rawBackLines
            : backLines;

        ParseBackContent(contentLines, operative);

        return i;
    }

    /// <summary>
    /// Scans raw-mode pdftotext lines and builds a lookup of operative name →
    /// back-card body lines.
    ///
    /// In these Kill Team PDFs the back-card block in raw mode has the following structure:
    /// <list type="number">
    ///   <item>"RULES CONTINUE ON OTHER SIDE" — end-of-front-card marker (start of block)</item>
    ///   <item>Ability / action text lines</item>
    ///   <item>ALL-CAPS operative name — appears AFTER the content (end of block / key)</item>
    /// </list>
    /// We therefore use "RULES CONTINUE ON OTHER SIDE" as the start boundary and the first
    /// standalone ALL-CAPS name (letters + spaces only, no digits, no commas, not a known
    /// stats keyword) as both the end boundary and the dictionary key.
    /// </summary>
    private static Dictionary<string, List<string>> BuildRawBackCardSections(List<string> rawLines)
    {
        var result = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
        var i = 0;
        var count = rawLines.Count;

        while (i < count)
        {
            // "RULES CONTINUE ON OTHER SIDE" marks the end of a front card.
            // Everything that follows — up to the ALL-CAPS operative name — is back-card content.
            if (!rawLines[i].Trim().Contains("CONTINUE ON OTHER SIDE", StringComparison.OrdinalIgnoreCase))
            {
                i++;
                continue;
            }

            i++; // past the marker line

            var backLines = new List<string>();
            string? operativeName = null;

            while (i < count)
            {
                var line = rawLines[i];
                var lineTrimmed = line.Trim();

                // A standalone ALL-CAPS name (letters/spaces/hyphens only, no digits or commas,
                // not a known stats label) marks the end of the back-card block and IS the key.
                if (AllCapsNameRegex().IsMatch(lineTrimmed)
                    && !lineTrimmed.Contains(',')
                    && !RawModeStatsKeywords.Contains(lineTrimmed))
                {
                    operativeName = ToTitleCase(lineTrimmed);
                    i++; // past the name line
                    break;
                }

                backLines.Add(line);
                i++;
            }

            if (operativeName != null)
            {
                result[operativeName] = backLines;
            }
        }

        return result;
    }

    /// <summary>
    /// Parses back-of-card content lines and appends abilities and weapon rules
    /// to the given operative.  Handles three formats:
    /// <list type="bullet">
    ///   <item>Footnote weapon rules: lines starting with <c>*</c></item>
    ///   <item>Two-column 1AP actions: a header line containing "1AP" twice</item>
    ///   <item>Single-column 1AP actions: a line matching ALL-CAPS name + AP cost pattern</item>
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
                    var ruleDescSb = new StringBuilder(ruleContent[(colonIdx + 1)..].Trim());

                    j++;

                    while (j < count)
                    {
                        var nextLine = backLines[j].TrimStart('\x07').TrimStart();

                        if (string.IsNullOrWhiteSpace(nextLine) || nextLine.StartsWith('*'))
                        {
                            break;
                        }

                        AppendText(ruleDescSb, nextLine);
                        j++;
                    }

                    operative.SpecialRules.Add(new ExtractedWeaponRule
                    {
                        Name = ruleName,
                        Text = TextHelpers.StructureToMarkdown(ruleDescSb.ToString().Trim()),
                    });
                }
                else
                {
                    j++;
                }

                continue;
            }

            // ── 2. Single-column 1AP action header ────────────────────────────
            // NormaliseText is applied to repair AP concatenation (e.g. OPTIC1AP → OPTIC 1AP)
            // before matching, so the raw line need not have a space before the AP cost.
            var normStripped = TextHelpers.NormaliseText(stripped);
            var singleApMatch = SingleColumnApRegex().Match(normStripped);

            if (singleApMatch.Success)
            {
                var actionName = StripControlChars(ToTitleCase(singleApMatch.Groups[1].Value.Trim()));
                var apCost = int.Parse(singleApMatch.Groups[2].Value, CultureInfo.InvariantCulture);
                var actionTextSb = new StringBuilder(singleApMatch.Groups[3].Value.Trim());

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

                    // Another single-column AP header ends this block
                    var nextNorm = TextHelpers.NormaliseText(nextLine);

                    if (SingleColumnApRegex().IsMatch(nextNorm))
                    {
                        break;
                    }

                    // Two-column 1AP header ends this block
                    var cFirst = nextLine.IndexOf("1AP", StringComparison.Ordinal);

                    if (cFirst >= 0 && nextLine.IndexOf("1AP", cFirst + 3, StringComparison.Ordinal) >= 0)
                    {
                        break;
                    }

                    AppendText(actionTextSb, nextLine);
                    j++;
                }

                operative.SpecialActions.Add(new ExtractedAbility
                {
                    Name = actionName,
                    ApCost = apCost,
                    Text = TextHelpers.StructureToMarkdown(actionTextSb.ToString().TrimStart()),
                });

                continue;
            }

            // ── 3. Two-column 1AP header: "1AP" appears at least twice ────────
            var firstApIdx = rawLine.IndexOf("1AP", StringComparison.Ordinal);

            if (firstApIdx >= 0)
            {
                var secondApIdx = rawLine.IndexOf("1AP", firstApIdx + 3, StringComparison.Ordinal);

                if (secondApIdx >= 0)
                {
                    // Left name: everything before the first "1AP"
                    var leftName = rawLine[..firstApIdx].TrimStart('\x07').Trim();

                    var rightPart = rawLine[(firstApIdx + 3)..];
                    var apInRight = rightPart.IndexOf("1AP", StringComparison.Ordinal);

                    var rightName = apInRight >= 0
                        ? rightPart[..apInRight].Trim()
                        : rightPart.Trim();

                    // Content boundary: where the right-column content begins in body lines.
                    // Scan past "1AP" and the following space-gap to find the right column start.
                    var contentBoundary = firstApIdx + 3;

                    while (contentBoundary < rawLine.Length && rawLine[contentBoundary] == ' ')
                    {
                        contentBoundary++;
                    }

                    // If no gap was found (no spaces after first "1AP"), fall back to the left name length
                    if (contentBoundary == firstApIdx + 3)
                    {
                        contentBoundary = firstApIdx;
                    }

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

                        // Distribute characters to left or right column based on content boundary position
                        var safeLen = Math.Min(contentBoundary, contentLine.Length);
                        var leftPart = contentLine[..safeLen].TrimStart('\x07').Trim();
                        var rightPartC = contentLine.Length > contentBoundary
                            ? contentLine[contentBoundary..].Trim()
                            : "";

                        if (leftPart.Length > 0)
                        {
                            AppendText(leftText, leftPart);
                        }

                        if (rightPartC.Length > 0)
                        {
                            AppendText(rightText, rightPartC);
                        }

                        j++;
                    }

                    if (leftName.Length > 0)
                    {
                        operative.SpecialActions.Add(new ExtractedAbility
                        {
                            Name = StripControlChars(ToTitleCase(leftName)),
                            ApCost = 1,
                            Text = TextHelpers.StructureToMarkdown(leftText.ToString().TrimStart()),
                        });
                    }

                    if (rightName.Length > 0)
                    {
                        operative.SpecialActions.Add(new ExtractedAbility
                        {
                            Name = StripControlChars(ToTitleCase(rightName)),
                            ApCost = 1,
                            Text = TextHelpers.StructureToMarkdown(rightText.ToString().TrimStart()),
                        });
                    }

                    continue;
                }
            }

            // ── 3. Passive rule: may be single-column or two-column layout ───
            var singleColonIdx = stripped.IndexOf(':');

            if (singleColonIdx > 0)
            {
                var possibleName = stripped[..singleColonIdx].Trim();

                if (IsAbilityName(possibleName))
                {
                    // Detect whether this line opens a two-column block (no 1AP marker).
                    // Two-column layout is present when there is a gap of ≥5 spaces after
                    // the primary colon followed by another ability-name-colon pattern.
                    var rawColonInLine = rawLine.IndexOf(':');
                    var rightColStart = rawColonInLine >= 0
                        ? DetectRightColumnStart(rawLine, rawColonInLine)
                        : -1;

                    if (rightColStart > 0)
                    {
                        // ── Two-column passive abilities ──────────────────────────────
                        // Extract the right-column ability name and its opening text from
                        // this first line.
                        var rightColContent = rawLine[rightColStart..].TrimStart('\x07').TrimStart();
                        var rightColonIdx = rightColContent.IndexOf(':');
                        var rightName = rightColonIdx > 0
                            ? rightColContent[..rightColonIdx].Trim()
                            : "";
                        var rightOpenText = rightColonIdx > 0
                            ? rightColContent[(rightColonIdx + 1)..].Trim()
                            : rightColContent.Trim();

                        // Left opening text: everything from the left colon to rightColStart.
                        var leftOpenRaw = rawLine[..rightColStart].TrimStart('\x07').Trim();
                        var leftAbsColon = leftOpenRaw.IndexOf(':');
                        var leftOpenText = leftAbsColon >= 0
                            ? leftOpenRaw[(leftAbsColon + 1)..].Trim()
                            : leftOpenRaw;

                        var leftTextSb = new StringBuilder(leftOpenText);
                        var rightTextSb = new StringBuilder(rightOpenText);

                        j++;

                        while (j < count)
                        {
                            var contentLine = backLines[j];
                            var contentStripped = contentLine.TrimStart('\x07').TrimStart();

                            if (string.IsNullOrWhiteSpace(contentStripped))
                            {
                                j++;
                                break;
                            }

                            if (contentStripped.StartsWith('*'))
                            {
                                break;
                            }

                            var safeLen = Math.Min(rightColStart, contentLine.Length);
                            var leftPart = contentLine[..safeLen].TrimStart('\x07').Trim();
                            var rightPart = contentLine.Length > rightColStart
                                ? contentLine[rightColStart..].TrimStart('\x07').Trim()
                                : "";

                            if (leftPart.Length > 0)
                            {
                                AppendText(leftTextSb, leftPart);
                            }

                            if (rightPart.Length > 0)
                            {
                                AppendText(rightTextSb, rightPart);
                            }

                            j++;
                        }

                        operative.Abilities.Add(new ExtractedAbility
                        {
                            Name = possibleName,
                            ApCost = null,
                            Text = TextHelpers.StructureToMarkdown(leftTextSb.ToString().TrimStart()),
                        });

                        if (rightName.Length > 0)
                        {
                            operative.Abilities.Add(new ExtractedAbility
                            {
                                Name = rightName,
                                ApCost = null,
                                Text = TextHelpers.StructureToMarkdown(rightTextSb.ToString().TrimStart()),
                            });
                        }

                        continue;
                    }

                    // ── Single-column passive rule ────────────────────────────────
                    var textSb = new StringBuilder(stripped[(singleColonIdx + 1)..].Trim());

                    j++;

                    while (j < count)
                    {
                        var nextLine = backLines[j].TrimStart('\x07').TrimStart();

                        if (string.IsNullOrWhiteSpace(nextLine))
                        {
                            // In raw mode, blank lines can occur within an ability's text block
                            // (e.g. between a parenthetical sentence and a bullet list). Do NOT
                            // break here — the next new ability name or AP action header ends the
                            // block. Preserve as a paragraph break marker.
                            j++;
                            continue;
                        }

                        if (nextLine.StartsWith('*'))
                        {
                            break;
                        }

                        // Bullet lines (•, ○, ↘, ↙, ↳) are always continuation list items —
                        // they can never start a new ability even if they contain a colon.
                        if (nextLine.Length > 0 && nextLine[0] is '\u2022' or '\u25CB' or '\u2198' or '\u2199' or '\u21B3')
                        {
                            AppendText(textSb, nextLine);
                            j++;
                            continue;
                        }

                        // A single-column 1AP action header ends this passive ability block.
                        var nextNormLine = TextHelpers.NormaliseText(nextLine);

                        if (SingleColumnApRegex().IsMatch(nextNormLine))
                        {
                            break;
                        }

                        var nextColon = nextLine.IndexOf(':');

                        if (nextColon > 0 && IsAbilityName(nextLine[..nextColon].Trim()))
                        {
                            break;
                        }

                        AppendText(textSb, nextLine);
                        j++;
                    }

                    operative.Abilities.Add(new ExtractedAbility
                    {
                        Name = possibleName,
                        ApCost = null,
                        Text = TextHelpers.StructureToMarkdown(textSb.ToString().Trim()),
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
                var ruleDescSb = new StringBuilder(ruleContent[(colonIdx + 1)..].Trim());

                i++;

                while (i < count)
                {
                    var next = lines[i].TrimStart('\x07').TrimStart();

                    if (string.IsNullOrWhiteSpace(next) || next.StartsWith('*'))
                    {
                        break;
                    }

                    AppendText(ruleDescSb, next);
                    i++;
                }

                result.Add(new ExtractedWeaponRule
                {
                    Name = ruleName,
                    Text = TextHelpers.StructureToMarkdown(ruleDescSb.ToString().Trim()),
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
                    var textSb = new StringBuilder(line[(colonIdx + 1)..].Trim());

                    j++;

                    while (j < count)
                    {
                        var nextLine = lines[j].TrimStart('\x07').Trim();

                        if (string.IsNullOrWhiteSpace(nextLine))
                        {
                            j++;
                            break;
                        }

                        // Bullet lines are always continuation of current ability text
                        if (nextLine.Length > 0 && nextLine[0] is '\u2022' or '\u25CB' or '\u2198' or '\u2199' or '\u21B3')
                        {
                            AppendText(textSb, nextLine);
                            j++;
                            continue;
                        }

                        var nextColon = nextLine.IndexOf(':');

                        if (nextColon > 0 && IsAbilityName(nextLine[..nextColon].Trim()))
                        {
                            break;
                        }

                        AppendText(textSb, nextLine);
                        j++;
                    }

                    abilities.Add(new ExtractedAbility
                    {
                        Name = name,
                        ApCost = null,
                        Text = TextHelpers.StructureToMarkdown(textSb.ToString().Trim()),
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
            var allLines = GetPdfLines(pdfPath, raw: true);
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
                var descSb = new StringBuilder();

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

                    AppendText(descSb, descLine);
                }

                result.Add(new ExtractedEquipmentItem
                {
                    Name = display,
                    Text = TextHelpers.StructureToMarkdown(descSb.ToString().TrimStart()),
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
        var lines = GetPdfLines(path, raw: true);
        var result = new List<ExtractedRule>();
        var currentName = "";
        var currentText = new StringBuilder();
        var pendingContinuation = false;

        foreach (var line in lines)
        {
            var trimmed = line.Trim();

            if (string.IsNullOrWhiteSpace(trimmed))
            {
                // Preserve blank lines as paragraph breaks in accumulated rule text (FIX 4).
                // Append \n so that the next AppendText's leading space creates a \n\n split.
                if (currentName.Length > 0 && currentText.Length > 0
                    && currentText[currentText.Length - 1] != '\n')
                {
                    currentText.Append('\n');
                }

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
                            result.Add(new ExtractedRule { Name = currentName, Text = TextHelpers.StructureToMarkdown(currentText.ToString().Trim()) });
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
                        result.Add(new ExtractedRule { Name = currentName, Text = TextHelpers.StructureToMarkdown(currentText.ToString().Trim()) });
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
                    // Numbered list items always start on their own line (FIX 5).
                    if (NumberedListItemLineRegex().IsMatch(trimmed))
                    {
                        if (currentText.Length > 0 && currentText[currentText.Length - 1] != '\n')
                        {
                            currentText.Append('\n');
                        }

                        currentText.Append(trimmed);
                    }
                    else
                    {
                        AppendText(currentText, trimmed);
                    }
                }
            }
        }

        // Flush the last rule
        if (currentName.Length > 0)
        {
            result.Add(new ExtractedRule { Name = currentName, Text = TextHelpers.StructureToMarkdown(currentText.ToString().Trim()) });
        }

        // Filter out fragment entries (all-caps sentence fragments with no description text)
        // and deduplicate by name (concatenate text if the same rule name appears on multiple pages)
        var deduped = new List<ExtractedRule>();
        var seen = new Dictionary<string, ExtractedRule>(StringComparer.OrdinalIgnoreCase);

        foreach (var rule in result.Where(r => r.Text.Length > 0))
        {
            if (seen.TryGetValue(rule.Name, out var existing))
            {
                // Use a paragraph break when joining continuations so that content from
                // subsequent pages (e.g. numbered list items) starts on its own line.
                var merged = new ExtractedRule
                {
                    Name = existing.Name,
                    Text = (existing.Text + "\n\n" + rule.Text).Trim(),
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
    /// Parses an Operative Selection PDF, extracting the archetype and the selection rules as Markdown text.
    /// Arrows (↘/↙/↳) become level-1 list items, filled bullets (•) become level-2,
    /// hollow circles (○) become level-3 (or level-2 when directly under an arrow).
    /// ALL-CAPS operative-type names are converted to bold title case.
    /// </summary>
    public ExtractedOperativeSelection ParseOperativeSelection(string path)
    {
        var lines = GetPdfLines(path, raw: true);
        var archetype = "";
        var foundArchetype = false;
        var contentLines = new List<string>();

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
                contentLines.Add(line);
            }
        }

        var text = BuildOperativeSelectionMarkdown(contentLines);

        return new ExtractedOperativeSelection
        {
            Archetype = archetype,
            Text = TextHelpers.StructureToMarkdown(text),
        };
    }

    /// <summary>
    /// Converts raw pdftotext lines from the operative selection into Markdown.
    /// Tracks bullet depth and joins word-wrapped continuation lines.
    /// </summary>
    private static string BuildOperativeSelectionMarkdown(List<string> lines)
    {
        var output = new StringBuilder();
        var currentItem = new StringBuilder(); // partially-built current Markdown line
        var parentDepth = 0;                   // depth of most recent arrow or bullet (not circle)

        void FlushItem()
        {
            if (currentItem.Length > 0)
            {
                output.AppendLine(currentItem.ToString());
                currentItem.Clear();
            }
        }

        foreach (var rawLine in lines)
        {
            var stripped = rawLine.TrimStart('\x07').Trim();

            // "CONTINUES ON OTHER SIDE" is a page-boundary marker — flush and treat as paragraph break
            if (stripped.Contains("CONTINUES ON OTHER SIDE", StringComparison.OrdinalIgnoreCase))
            {
                FlushItem();
                output.AppendLine();
                parentDepth = 0;
                continue;
            }

            // Skip "OPERATIVES" header and empty lines
            if (string.IsNullOrEmpty(stripped)
                || stripped.Equals("OPERATIVES", StringComparison.OrdinalIgnoreCase))
            {
                if (string.IsNullOrEmpty(stripped))
                {
                    FlushItem();
                    output.AppendLine(); // blank line → paragraph break
                    parentDepth = 0;
                }

                continue;
            }

            if (stripped[0] is '\u2198' or '\u2199' or '\u21B3') // ↘ ↙ ↳
            {
                FlushItem();
                var rest = ApplyBold(stripped[1..].TrimStart());
                currentItem.Append("- ").Append(rest);
                parentDepth = 1;
            }
            else if (stripped[0] == '\u2022') // •
            {
                FlushItem();
                var rest = ApplyBold(stripped[1..].TrimStart());
                currentItem.Append("  - ").Append(rest);
                parentDepth = 2;
            }
            else if (stripped[0] == '\u25CB') // ○
            {
                FlushItem();
                var rest = ApplyBold(stripped[1..].TrimStart());

                // ○ nests under the most recent arrow (parentDepth≤1) or bullet (parentDepth==2)
                // parentDepth is NOT updated by ○ so consecutive circles stay at the same indent
                if (parentDepth >= 2)
                {
                    currentItem.Append("    - ").Append(rest);
                }
                else
                {
                    currentItem.Append("  - ").Append(rest);
                }
            }
            else
            {
                // Continuation line or new paragraph line
                if (currentItem.Length > 0)
                {
                    currentItem.Append(' ').Append(ApplyBold(stripped));
                }
                else
                {
                    currentItem.Append(ApplyBold(stripped));
                }
            }
        }

        FlushItem();

        // Trim trailing blank lines
        var result = output.ToString().TrimEnd();
        return result;
    }

    /// <summary>
    /// Converts ALL-CAPS sequences in <paramref name="text"/> to bold Markdown with title case.
    /// E.g. "DEATH JESTER" → "**Death Jester**", "VOID-DANCER TROUPE" → "**Void-Dancer Troupe**".
    /// Single ALL-CAPS words of ≥2 letters are also converted.
    /// </summary>
    private static string ApplyBold(string text)
    {
        return AllCapsSequenceRegex().Replace(text, m => $"**{TextHelpers.ToTitleCase(m.Value)}**");
    }

    // ─── Supplementary information parsing ───────────────────────────────────────

    /// <summary>
    /// Parses a Supplementary Information PDF and returns all text content joined with newlines.
    /// Applies text normalisation (Rule 1) and strips PDF chrome (Rule 4).
    /// </summary>
    public string ParseSupplementaryInfo(string path)
    {
        var lines = GetPdfLines(path, raw: true);

        var raw = string.Join(
            '\n',
            lines
                .Select(l => l.Trim())
                .Where(l => l.Length > 0
                    && !l.Equals("CONTINUES ON OTHER SIDE", StringComparison.OrdinalIgnoreCase)));

        return TextHelpers.StructureToMarkdown(raw);
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
        // Names must begin with an uppercase letter; lowercase starts indicate prose
        // continuations (e.g. "per battle"), bullet symbols, or stat values — not ability names.
        if (string.IsNullOrEmpty(name) || !char.IsUpper(name[0]))
        {
            return false;
        }

        // A closing parenthesis inside the name means this is a prose fragment
        // (e.g. "following rules more than once per turning point)") not an ability name.
        if (name.Contains(')'))
        {
            return false;
        }

        return name.Length < 60 && !AllCapsNameRegex().IsMatch(name);
    }

    /// <summary>
    /// Scans <paramref name="rawLine"/> to the right of <paramref name="leftColonPos"/>
    /// for a gap of five or more spaces followed by an ability-name-colon pattern.
    /// Returns the absolute character position in <paramref name="rawLine"/> where the right
    /// column starts, or <c>-1</c> if no right column is detected.
    /// </summary>
    private static int DetectRightColumnStart(string rawLine, int leftColonPos)
    {
        var i = leftColonPos + 1;

        while (i < rawLine.Length)
        {
            if (rawLine[i] != ' ')
            {
                i++;
                continue;
            }

            var spaceStart = i;

            while (i < rawLine.Length && rawLine[i] == ' ')
            {
                i++;
            }

            if (i - spaceStart >= 5 && i < rawLine.Length)
            {
                var remainder = rawLine[i..].TrimStart('\x07').TrimStart();
                var colonInRem = remainder.IndexOf(':');

                if (colonInRem > 0 && IsAbilityName(remainder[..colonInRem].Trim()))
                {
                    return i;
                }
            }
        }

        return -1;
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

    /// <summary>
    /// Appends <paramref name="part"/> to <paramref name="sb"/> with the appropriate leading separator.
    /// <list type="bullet">
    ///   <item><c>•</c> (U+2022) prepends a newline and level-2 Markdown bullet prefix <c>\n  - </c></item>
    ///   <item><c>○</c> (U+25CB) prepends a newline and level-3 Markdown bullet prefix <c>\n    - </c></item>
    ///   <item>All other text prepends a single space (word-wrap join).</item>
    /// </list>
    /// Call <c>sb.ToString().TrimStart()</c> on the final result to strip the leading separator
    /// produced by the first append.
    /// </summary>
    private static void AppendText(StringBuilder sb, string part)
    {
        if (part.Length == 0)
        {
            return;
        }

        switch (part[0])
        {
            case '\u2022': // • level-2 bullet
                sb.Append("\n  - ");
                sb.Append(part[1..].TrimStart());
                break;

            case '\u25CB': // ○ level-3 bullet
                sb.Append("\n    - ");
                sb.Append(part[1..].TrimStart());
                break;

            default:
                sb.Append(' ');
                sb.Append(part);
                break;
        }
    }

    private static bool IsEquipmentSkip(string text)
    {
        return EquipmentSkipPatterns.Any(p => Regex.IsMatch(text, p, RegexOptions.IgnoreCase));
    }

    private static List<string> GetPdfLines(string pdfPath, bool raw = false)
    {
        var psi = new ProcessStartInfo
        {
            FileName = "pdftotext",
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            StandardOutputEncoding = Encoding.UTF8,
        };

        if (raw)
        {
            psi.ArgumentList.Add("-raw");
        }
        else
        {
            psi.ArgumentList.Add("-layout");
        }

        psi.ArgumentList.Add(pdfPath);
        psi.ArgumentList.Add("-");

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

    private static string ToTitleCase(string text) => TextHelpers.ToTitleCase(text);

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

    /// <summary>Matches contiguous ALL-CAPS sequences (possibly hyphenated, multi-word) for bold conversion.</summary>
    [GeneratedRegex(@"[A-Z][A-Z\-]+(?:\s+[A-Z][A-Z\-]+)*")]
    private static partial Regex AllCapsSequenceRegex();

    /// <summary>
    /// Single-column 1AP action header: ALL-CAPS name followed by digit+AP.
    /// Group 1 = action name, Group 2 = AP digit(s), Group 3 = optional inline text.
    /// Applied to NormaliseText-processed input so "OPTIC1AP" has already been repaired to "OPTIC 1AP".
    /// </summary>
    [GeneratedRegex(@"^([A-Z][A-Z0-9'\-]+(?:\s+[A-Z][A-Z0-9'\-]+)*)\s+(\d+)AP(?:\s+(.+))?$")]
    private static partial Regex SingleColumnApRegex();

    /// <summary>
    /// A numbered list item at the start of a line: digit(s), period, space, uppercase letter.
    /// Used in ParseRulesDoc to ensure numbered items always start on their own line.
    /// </summary>
    [GeneratedRegex(@"^\d+\.\s+[A-Z]")]
    private static partial Regex NumberedListItemLineRegex();
}
