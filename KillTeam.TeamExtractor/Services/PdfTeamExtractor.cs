using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using KillTeam.TeamExtractor.Models;
using UglyToad.PdfPig;
using UglyToad.PdfPig.Content;
using UglyToad.PdfPig.Core;
using UglyToad.PdfPig.DocumentLayoutAnalysis.TextExtractor;

namespace KillTeam.TeamExtractor.Services;

/// <summary>
/// Extracts Kill Team operative data from official GW PDF sources.
/// Uses PdfPig for all PDF operations: text extraction, weapon type detection, and strikethrough detection.
/// </summary>
public partial class PdfTeamExtractor
{
    private static readonly HashSet<string> SectionHeaders = new(StringComparer.Ordinal)
    {
        "FACTION EQUIPMENT",
        "UNIVERSAL EQUIPMENT",
    };

    /// <summary>
    /// ALL-CAPS tokens that appear in content-order PDF text but are stats labels or
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
        _weaponTypeDetector = weaponTypeDetector;
    }

    /// <summary>Extracts a team from the PDFs in the given folder and returns the structured result.</summary>
    public ExtractedTeam Extract(string teamName, string teamFolder)
    {
        var datacardsPath = FindPdf(teamFolder, "*Datacards*");
        var factionEquipmentPath = FindPdf(teamFolder, "*Faction Equipment*");
        var factionRulesPath = FindPdf(teamFolder, "*Faction Rules*");
        var firefightPloysPath = FindPdf(teamFolder, "*Firefight Ploys*");
        var operativeSelectionPath = FindPdf(teamFolder, "*Operative Selection*");
        var strategyPloysPath = FindPdf(teamFolder, "*Strategy Ploys*");
        var supplementaryInformationPath = FindPdf(teamFolder, "*Supplementary Information*");
        var universalEquipmentPath = FindPdf(teamFolder, "*Universal Equipment*");

        if (datacardsPath == null)
        {
            throw new InvalidOperationException($"No Datacards PDF found in {teamFolder}");
        }

        var weaponTypes = _weaponTypeDetector.Detect(datacardsPath);
        var (operatives, faction, grandFaction) = ParseDatacards(datacardsPath, weaponTypes);

        var factionEquipment = factionEquipmentPath != null
            ? ParseEquipmentWithDescriptions([factionEquipmentPath])
            : [];
        var universalEquipment = universalEquipmentPath != null
            ? ParseEquipmentWithDescriptions([universalEquipmentPath])
            : [];
        var primaryKeyword = operatives.Count > 0 ? operatives[0].PrimaryKeyword : null;
        var factionRules = factionRulesPath != null ? ParseRulesDoc(factionRulesPath, teamName, primaryKeyword) : [];
        var strategyPloys = strategyPloysPath != null ? ParseRulesDoc(strategyPloysPath, teamName, primaryKeyword, isPloy: true) : [];
        var firefightPloys = firefightPloysPath != null ? ParseRulesDoc(firefightPloysPath, teamName, primaryKeyword, isPloy: true) : [];
        var operativeSelection = operativeSelectionPath != null ? ParseOperativeSelection(operativeSelectionPath) : null;
        var supplementaryInfo = supplementaryInformationPath != null ? ParseSupplementaryInfo(supplementaryInformationPath) : string.Empty;

        if (operatives.Count == 0)
        {
            throw new InvalidOperationException(
                $"No operatives extracted from '{teamName}'. The PDF layout may differ from expected.");
        }

        return new ExtractedTeam
        {
            Id = Slugify(teamName),
            Name = teamName,
            GrandFaction = grandFaction ?? "UNKNOWN — UPDATE ME",
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

    private (List<ExtractedOperative> Operatives, string? Faction, string? GrandFaction) ParseDatacards(
        string pdfPath,
        Dictionary<string, WeaponType> weaponTypes)
    {
        // Layout mode is required for the weapon-stats regex (column-aligned positions).
        var lines = GetPdfLines(pdfPath);
        // Raw mode is used for back-card prose content — avoids two-column interleaving.
        var rawLines = GetPdfLines(pdfPath, raw: true);
        var (rawBackSections, rawFrontOnlySections) = BuildRawBackCardSections(rawLines);

        var count = lines.Count;
        var operatives = new List<ExtractedOperative>();

        // Tracks operative instances for back-of-card lookup
        var operativeMap = new Dictionary<string, ExtractedOperative>(StringComparer.OrdinalIgnoreCase);
        var processed = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        string? grandFaction = null;
        var index2Counts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        string? primaryKeywordFallback = null;
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
                    i = ParseBackOfCard(lines, rawBackSections, i, existingOp);
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

                    // Continuation detection: if rules text ends with comma, the next non-blank
                    // line(s) continue the rule list. PdfPig layout mode may place them on separate
                    // lines with zero indentation and intervening blank lines.
                    while (i + 1 < count && wRulesRaw.TrimEnd().EndsWith(','))
                    {
                        var next = lines[i + 1];

                        if (string.IsNullOrWhiteSpace(next))
                        {
                            i++;
                            continue;
                        }

                        var nextTrimmed = next.Trim();

                        if (nextTrimmed.Length < 80
                            && !WeaponRowRegex().IsMatch(next)
                            && !StatsHeaderRegex().IsMatch(next)
                            && !AllCapsNameRegex().IsMatch(nextTrimmed))
                        {
                            wRulesRaw = wRulesRaw + " " + nextTrimmed;
                            i++;
                        }
                        else
                        {
                            break;
                        }
                    }

                    if (wRulesRaw == "-")
                    {
                        wRulesRaw = string.Empty;
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

            // Parse front-of-card weapon rules (*Name: text) and abilities from lines after the last weapon row.
            // Prefer raw-mode content when available to avoid two-column layout interleaving.
            List<ExtractedAbility> frontAbilities;
            List<ExtractedWeaponRule> frontWeaponRules;
            List<ExtractedAbility> frontSpecialActions;

            if (rawFrontOnlySections.TryGetValue(operativeName, out var rawAbilityLines))
            {
                // Parse abilities, special actions, and weapon rules from the clean raw-mode lines.
                var tempOp = new ExtractedOperative { Name = operativeName, Save = "" };
                ParseBackContent(rawAbilityLines, tempOp);
                frontAbilities = tempOp.Abilities;
                frontWeaponRules = tempOp.SpecialRules;
                frontSpecialActions = tempOp.SpecialActions;
            }
            else
            {
                frontWeaponRules = ExtractFrontWeaponRules(afterWeaponLines);
                frontAbilities = ParseFrontAbilityLines(afterWeaponLines);
                frontSpecialActions = [];
            }

            // Parse keywords from the faction keyword line (currently at position i)
            var keywords = new List<string>();
            var primaryKeyword = string.Empty;

            if (i < count && FactionKeywordLineRegex().IsMatch(lines[i]) && lines[i].Split(',').Length >= 3)
            {
                var kwLine = lines[i].Trim();
                var parts = kwLine.Split(',').Select(p => p.Trim()).ToList();

                // Strip trailing page number from the last token
                if (parts.Count > 0)
                {
                    parts[^1] = PageNumberSuffixRegex().Replace(parts[^1], string.Empty).Trim();
                }

                keywords = parts
                    .Where(p => p.Length > 0)
                    .Select(p => ToTitleCase(p))
                    .ToList();

                primaryKeyword = keywords.FirstOrDefault() ?? string.Empty;

                // Collect keywords[1] (grand faction) and keywords[2] (faction or operative type)
                // across all operatives. After the loop, the most-repeated keywords[2] is the faction;
                // if every operative has a unique keywords[2] (all type labels differ), the team has
                // no distinct faction keyword and keywords[0] (always consistent) is used instead.
                if (keywords.Count >= 3)
                {
                    grandFaction ??= keywords[1];
                    index2Counts.TryGetValue(keywords[2], out var existing);
                    index2Counts[keywords[2]] = existing + 1;
                    primaryKeywordFallback ??= keywords[0];
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

                foreach (var sa in frontSpecialActions)
                {
                    operative.SpecialActions.Add(sa);
                }

                operatives.Add(operative);
                operativeMap[operativeName] = operative;
            }
        }

        // The most-repeated keywords[2] appears more than once → consistent faction keyword.
        // All keywords[2] values are unique (max count == 1) → per-operative type labels;
        // fall back to keywords[0] which is always consistent across the kill team.
        var maxCount = index2Counts.Count > 0 ? index2Counts.Values.Max() : 0;
        var faction = maxCount > 1
            ? index2Counts.OrderByDescending(kv => kv.Value).First().Key
            : primaryKeywordFallback;

        return (operatives, faction, grandFaction);
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
    /// Scans content-order PDF text lines and builds two lookups of operative name →
    /// back-card body lines, distinguished by PDF structure variant:
    /// <list type="bullet">
    ///   <item>
    ///     <b>backCardSections</b> — content from "RULES CONTINUE ON OTHER SIDE" blocks
    ///     (two-page operatives, e.g. Angels of Death, Nemesis Claw).  These should be
    ///     used in <see cref="ParseBackOfCard"/> (second layout-mode occurrence).
    ///   </item>
    ///   <item>
    ///     <b>frontOnlySections</b> — content from weapon-table-header blocks
    ///     (single-page operatives, e.g. Plague Marines).  These should be used when
    ///     building an operative on its first (and only) layout-mode occurrence, to avoid
    ///     two-column layout interleaving.
    ///   </item>
    /// </list>
    ///
    /// In both variants the ALL-CAPS operative name is the <em>end</em> of the block.
    /// Only sections that contain at least one parseable ability, 1AP action, or footnote
    /// weapon rule are stored; pure weapon-row blocks are discarded so those operatives
    /// fall back to layout-mode parsing.
    /// </summary>
    private static (
        Dictionary<string, List<string>> BackCardSections,
        Dictionary<string, List<string>> FrontOnlySections)
    BuildRawBackCardSections(List<string> rawLines)
    {
        var backCard = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
        var frontOnly = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
        var i = 0;
        var count = rawLines.Count;

        while (i < count)
        {
            var line = rawLines[i];
            var trimmed = line.Trim();

            // Trigger 1: "RULES CONTINUE ON OTHER SIDE" — end of front card (two-page operative).
            // Trigger 2: Weapon-table header ("NAME ATK HIT DMG WR") — start of a
            //            combined-card content block (single-page operative, Plague Marines pattern).
            var isRulesContinue = trimmed.Contains("CONTINUE ON OTHER SIDE", StringComparison.OrdinalIgnoreCase);
            var isWeaponTableHeader = !isRulesContinue && WeaponTableHeaderRegex().IsMatch(line);

            if (!isRulesContinue && !isWeaponTableHeader)
            {
                i++;
                continue;
            }

            i++; // past the trigger line

            var blockLines = new List<string>();
            string? operativeName = null;

            while (i < count)
            {
                var contentLine = rawLines[i];
                var contentTrimmed = contentLine.Trim();

                // A standalone ALL-CAPS name (letters/spaces/hyphens only, no digits or
                // commas, not a known stats label) is the end marker and becomes the key.
                // ContentOrderTextExtractor may append trailing digits (e.g. page numbers or stat values)
                // to the name line — strip those before matching.
                var nameCandidate = PageNumberSuffixRegex().Replace(contentTrimmed, string.Empty).Trim();
                if (nameCandidate.Length > 0
                    && AllCapsNameRegex().IsMatch(nameCandidate)
                    && !nameCandidate.Contains(',')
                    && !RawModeStatsKeywords.Contains(nameCandidate))
                {
                    operativeName = ToTitleCase(nameCandidate);
                    i++; // past the name line
                    break;
                }

                // Faction keyword lines (all-caps, comma-separated, e.g.
                // "PLAGUE MARINE , CHAOS, HERETIC ASTARTES, FIGHTER") precede the
                // operative name but are not ability content — skip them.
                if (contentTrimmed.Contains(',') && FactionKeywordLineRegex().IsMatch(contentTrimmed))
                {
                    i++;
                    continue;
                }

                blockLines.Add(contentLine);
                i++;
            }

            // Only store sections that contain at least one parseable ability.
            if (operativeName == null || !ContainsParsableContent(blockLines))
            {
                continue;
            }

            if (isRulesContinue)
            {
                // Back-card section for a two-page operative — use in ParseBackOfCard.
                backCard[operativeName] = blockLines;
            }
            else
            {
                // Combined-card section for a single-page operative — use on first occurrence.
                // Don't overwrite an already-stored back-card section (RULES CONTINUE wins).
                if (!backCard.ContainsKey(operativeName))
                {
                    frontOnly[operativeName] = blockLines;
                }
            }
        }

        return (backCard, frontOnly);
    }

    /// <summary>
    /// Returns <c>true</c> when <paramref name="lines"/> contains at least one line
    /// that <see cref="ParseBackContent"/> would consume as a passive ability,
    /// single-column 1AP action, or footnote weapon rule.
    /// </summary>
    private static bool ContainsParsableContent(List<string> lines)
    {
        foreach (var line in lines)
        {
            var stripped = line.TrimStart('\x07').TrimStart();

            if (stripped.Length == 0)
            {
                continue;
            }

            // Footnote weapon rule: *Name: …
            if (stripped.StartsWith('*') && stripped.Contains(':'))
            {
                return true;
            }

            // Single-column 1AP action
            if (SingleColumnApRegex().IsMatch(TextHelpers.NormaliseText(stripped)))
            {
                return true;
            }

            // Passive ability: Name: …
            var colonIdx = stripped.IndexOf(':');

            if (colonIdx > 0 && IsAbilityName(stripped[..colonIdx].Trim()))
            {
                return true;
            }
        }

        return false;
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
                            : string.Empty;

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
                            : string.Empty;
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
                                : string.Empty;

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
            var fontBreakLines = GetEquipmentFontBreakLines(pdfPath);

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
                var nextNonBlank = string.Empty;

                for (var lineIndex = j + 1; lineIndex < total; lineIndex++)
                {
                    var nextLine = allLines[lineIndex].Trim();

                    if (nextLine.Length > 0)
                    {
                        nextNonBlank = nextLine;
                        break;
                    }
                }

                if (SectionHeaders.Contains(nextNonBlank))
                {
                    continue;
                }

                var itemName = QuantityPrefixRegex().Replace(trimmed, string.Empty);

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

                for (var lineIndex = j + 1; lineIndex < total; lineIndex++)
                {
                    var descLine = allLines[lineIndex].Trim();

                    if (string.IsNullOrWhiteSpace(descLine))
                    {
                        // Preserve blank lines as paragraph breaks (same as ParseRulesDoc).
                        // The PDF has structural blank lines between lore and rules text.
                        if (descSb.Length > 0)
                        {
                            while (descSb.Length > 0 && descSb[descSb.Length - 1] == '\n')
                            {
                                descSb.Remove(descSb.Length - 1, 1);
                            }

                            descSb.Append("\n\n");
                        }

                        continue;
                    }

                    if (SectionHeaders.Contains(descLine))
                    {
                        break;
                    }

                    // Weapon sub-table: "NAME ATK HIT DMG" header inside the description.
                    // Collect the data row(s), optional WR + weapon rule text, and emit a
                    // Markdown table so it doesn't get appended as flat prose.
                    if (WeaponTableHeaderRegex().IsMatch(descLine))
                    {
                        // Ensure a paragraph break before the table
                        while (descSb.Length > 0 && descSb[descSb.Length - 1] == '\n')
                        {
                            descSb.Remove(descSb.Length - 1, 1);
                        }

                        if (descSb.Length > 0)
                        {
                            descSb.Append("\n\n");
                        }

                        var tableRows = new List<string>();
                        var wrText = string.Empty;
                        lineIndex++; // advance past the header

                        while (lineIndex < total)
                        {
                            var tableLine = allLines[lineIndex].Trim();

                            if (string.IsNullOrWhiteSpace(tableLine))
                            {
                                lineIndex++;
                                continue;
                            }

                            // WR header — next non-blank line is the weapon rules list
                            if (string.Equals(tableLine, "WR", StringComparison.OrdinalIgnoreCase))
                            {
                                lineIndex++; // advance past "WR"

                                while (lineIndex < total)
                                {
                                    var weaponRuleLine = allLines[lineIndex].Trim();

                                    if (!string.IsNullOrWhiteSpace(weaponRuleLine))
                                    {
                                        wrText = weaponRuleLine;
                                        break;
                                    }

                                    lineIndex++;
                                }

                                break; // table complete
                            }

                            // Another weapon table header — step back so outer loop handles it
                            if (WeaponTableHeaderRegex().IsMatch(tableLine))
                            {
                                lineIndex--;
                                break;
                            }

                            // Next equipment item — step back so outer loop handles it
                            if (AllCapsEquipmentRegex().IsMatch(tableLine) && !IsEquipmentSkip(tableLine))
                            {
                                var stripped = QuantityPrefixRegex().Replace(tableLine, string.Empty);

                                if (stripped.Length >= 4)
                                {
                                    lineIndex--;
                                    break;
                                }
                            }

                            tableRows.Add(tableLine);
                            lineIndex++;
                        }

                        descSb.Append(BuildInlineWeaponTableMarkdown(string.Join("\n", tableRows), wrText));
                        descSb.Append("\n\n");
                        continue; // outer for lineIndex++ advances past the WR text line
                    }

                    // Next ALL CAPS item name ends the description block
                    if (AllCapsEquipmentRegex().IsMatch(descLine) && !IsEquipmentSkip(descLine))
                    {
                        var stripped = QuantityPrefixRegex().Replace(descLine, string.Empty);

                        if (stripped.Length >= 4 && !WeaponTableHeaderRegex().IsMatch(descLine))
                        {
                            break;
                        }
                    }

                    AppendText(descSb, descLine);

                    // If this line is the last lore-font line before a rules-font transition
                    // (detected via word-level font-size analysis), insert a paragraph break.
                    if (fontBreakLines.Contains(descLine))
                    {
                        while (descSb.Length > 0 && descSb[descSb.Length - 1] == '\n')
                        {
                            descSb.Remove(descSb.Length - 1, 1);
                        }

                        descSb.Append("\n\n");
                    }                }

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
    public List<ExtractedRule> ParseRulesDoc(string path, string? teamName = null, string? primaryKeyword = null, bool isPloy = false)
    {
        var lines = GetPdfLines(path, raw: true);
        var result = new List<ExtractedRule>();
        var currentName = string.Empty;
        var currentText = new StringBuilder();
        var pendingContinuation = false;
        var emptyChain = new List<string>(); // consecutive empty-text ALL-CAPS names before the next rule
        var teamNameTitleCase = teamName != null ? ToTitleCase(teamName) : null;
        var primaryKeywordTitleCase = primaryKeyword != null ? ToTitleCase(primaryKeyword) : null;

        // Format rule text with optional ploy-specific paragraph breaks
        string FormatText(string raw)
        {
            var text = TextHelpers.StructureToMarkdown(raw.Trim());

            if (isPloy)
            {
                text = TextHelpers.ApplyPloyParagraphBreaks(text);
            }

            // Suppress paragraph break between "...below." and prose continuation.
            // "Presented below." always leads directly into its detail text; ploy
            // paragraph-break patterns can fire between them and must be undone.
            text = Regex.Replace(text, @"below\.\n\n(?=[A-Z])", "below. ");
            return text;
        }

        foreach (var line in lines)
        {
            // Strip control characters (BEL and others that PDFs embed as rendering artefacts)
            // before Trim(), so lines containing only \x07 are treated as empty.
            // Also apply text normalisation (smart quotes, mojibake apostrophes) so that
            // rule names containing apostrophes (e.g. SCORPION'S EYE) match the regex.
            var trimmed = TextHelpers.NormaliseText(new string(line.Where(c => c >= 32).ToArray()));

            if (string.IsNullOrWhiteSpace(trimmed))
            {
                // Preserve blank lines as paragraph breaks in accumulated rule text.
                // Strip any trailing \n already appended, then add exactly \n\n.
                if (currentName.Length > 0 && currentText.Length > 0)
                {
                    while (currentText.Length > 0 && currentText[currentText.Length - 1] == '\n')
                    {
                        currentText.Remove(currentText.Length - 1, 1);
                    }

                    currentText.Append("\n\n");
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
                            result.Add(new ExtractedRule { Category = DetermineRuleCategory(emptyChain, teamNameTitleCase, primaryKeywordTitleCase), Name = currentName, Text = FormatText(currentText.ToString()) });
                            emptyChain.Clear();
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
                        if (currentText.Length > 0)
                        {
                            // Rule has text — determine category from the empty-header chain
                            result.Add(new ExtractedRule { Category = DetermineRuleCategory(emptyChain, teamNameTitleCase, primaryKeywordTitleCase), Name = currentName, Text = FormatText(currentText.ToString()) });
                            emptyChain.Clear();
                        }
                        else
                        {
                            // Rule has no text — it's a category/type label.
                            // Accumulate into the empty-header chain, but only after at
                            // least one real rule has been saved (preamble headers like the
                            // team name and rule-type watermark at the top of each PDF are
                            // excluded so they don't corrupt the first technique's category).
                            if (result.Count > 0)
                            {
                                emptyChain.Add(currentName);
                            }
                        }
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
            result.Add(new ExtractedRule { Category = DetermineRuleCategory(emptyChain, teamNameTitleCase, primaryKeywordTitleCase), Name = currentName, Text = FormatText(currentText.ToString()) });
        }

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

        // Post-process: fold inline weapon tables (NAME ATK HIT DMG + WR) into the preceding rule's text.
        // PDF extraction parses "NAME ATK HIT DMG" as an ALL-CAPS rule name.  The data row and "WR" header
        // follow as separate rules.  Merge them back as a Markdown table appended to the rule that
        // ended with "...can use the following ranged weapon:" (or similar).
        for (var i = deduped.Count - 1; i >= 0; i--)
        {
            if (!string.Equals(deduped[i].Name, "Name Atk Hit Dmg", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var dataText = deduped[i].Text.Trim();
            var wrText = string.Empty;

            if (i + 1 < deduped.Count && string.Equals(deduped[i + 1].Name, "Wr", StringComparison.OrdinalIgnoreCase))
            {
                wrText = deduped[i + 1].Text.Trim();
                deduped.RemoveAt(i + 1);
            }

            var tableMarkdown = BuildInlineWeaponTableMarkdown(dataText, wrText);

            if (i > 0)
            {
                var preceding = deduped[i - 1];
                deduped[i - 1] = new ExtractedRule
                {
                    Category = preceding.Category,
                    Name = preceding.Name,
                    Text = preceding.Text.TrimEnd() + "\n\n" + tableMarkdown,
                };
            }

            deduped.RemoveAt(i);
        }

        return deduped;
    }

    /// <summary>
    /// Formats a weapon data row and optional weapon-rules text as a Markdown table.
    /// </summary>
    /// <param name="dataText">Space-delimited row(s): name tokens followed by ATK, HIT, DMG.</param>
    /// <param name="wrText">Weapon rule list, e.g. "Range 6&quot;, Saturate, Stun".</param>
    private static string BuildInlineWeaponTableMarkdown(string dataText, string wrText)
    {
        var hasWr = !string.IsNullOrWhiteSpace(wrText);
        var sb = new StringBuilder();

        if (hasWr)
        {
            sb.AppendLine("| NAME | ATK | HIT | DMG | WR |");
            sb.AppendLine("|------|-----|-----|-----|----|");
        }
        else
        {
            sb.AppendLine("| NAME | ATK | HIT | DMG |");
            sb.AppendLine("|------|-----|-----|-----|");
        }

        foreach (var row in dataText.Split('\n', StringSplitOptions.RemoveEmptyEntries))
        {
            var tokens = row.Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries);

            if (tokens.Length >= 4)
            {
                var damage = tokens[^1];
                var hit = tokens[^2];
                var attackCount = tokens[^3];
                var name = string.Join(" ", tokens[..^3]);

                if (hasWr)
                {
                    sb.AppendLine($"| {name} | {attackCount} | {hit} | {damage} | {wrText} |");
                }
                else
                {
                    sb.AppendLine($"| {name} | {attackCount} | {hit} | {damage} |");
                }
            }
        }

        return sb.ToString().TrimEnd();
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
        var archetype = string.Empty;
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
                    var val = m.Groups[1].Value.Trim();

                    // "SEE REVERSE" means the archetype rules are complex and printed on the back
                    // of the card — keep it as the archetype label in title case.
                    archetype = TextHelpers.ToTitleCase(val);
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
            Text = text,
        };
    }

    /// <summary>
    /// Converts content-order PDF text lines from the operative selection into Markdown.
    /// Tracks bullet depth and joins word-wrapped continuation lines.
    /// Applies text normalisation, bold conversion, and paragraph-break logic inline —
    /// the result must NOT be passed through <see cref="TextHelpers.StructureToMarkdown"/>
    /// again, to avoid double-bolding already-processed content.
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
                var itemStr = currentItem.ToString();
                // If a • bullet begins with a bold ALL-CAPS span (operative name) immediately
                // followed by qualifying text ("with one of the following options:" etc.),
                // re-wrap the entire content in a single bold span for consistent formatting.
                var m = OperativeBulletRewrapRegex().Match(itemStr);
                output.AppendLine(m.Success
                    ? $"  - **{m.Groups[1].Value} {m.Groups[2].Value}**"
                    : itemStr);
                currentItem.Clear();
            }
        }

        // Section headings that appear as standalone ALL-CAPS lines and should be rendered as Markdown headings.
        var sectionHeadings = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "OPERATIVES", "ARCHETYPES",
        };

        foreach (var rawLine in lines)
        {
            // Normalise each line individually before processing (smart quotes, AP concat, etc.)
            var stripped = TextHelpers.NormaliseText(rawLine.TrimStart('\x07').Trim());

            // "CONTINUES ON OTHER SIDE" is a page-boundary marker — flush and treat as paragraph break
            if (stripped.Contains("CONTINUES ON OTHER SIDE", StringComparison.OrdinalIgnoreCase))
            {
                FlushItem();
                output.AppendLine();
                parentDepth = 0;
                continue;
            }

            // Section headings (OPERATIVES, ARCHETYPES) → Markdown # heading
            if (sectionHeadings.Contains(stripped))
            {
                FlushItem();
                output.AppendLine();
                output.AppendLine($"# {TextHelpers.ToTitleCase(stripped)}");
                output.AppendLine();
                parentDepth = 0;
                continue;
            }

            // Skip empty lines — only emit one blank line for consecutive empties
            if (string.IsNullOrEmpty(stripped))
            {
                FlushItem();
                // Check if output already ends with a double newline (\n\n or \r\n\r\n)
                var len = output.Length;
                var endsNewline = len >= 1 && output[len - 1] == '\n';
                var endsDoubleNewline = endsNewline && (
                    (len >= 2 && output[len - 2] == '\n') ||
                    (len >= 3 && output[len - 2] == '\r' && output[len - 3] == '\n'));
                if (endsDoubleNewline == false)
                {
                    output.AppendLine();
                }
                parentDepth = 0;
                continue;
            }

            if (stripped[0] is '\u2198' or '\u2199' or '\u21B3') // ↘ ↙ ↳
            {
                FlushItem();
                var rest = ApplyBold(stripped[1..].TrimStart());

                // Digit-start after an arrow = group size header (e.g. "↘ 1 ANGEL OF DEATH operative...").
                // These are section headings, not bulleted list items.
                if (rest.Length > 0 && char.IsAsciiDigit(rest[0]))
                {
                    currentItem.Append(rest);
                    parentDepth = 0;
                }
                else
                {
                    currentItem.Append("- ").Append(rest);
                    parentDepth = 1;
                }
            }
            else if (stripped[0] == '\u2022') // •
            {
                FlushItem();
                var bulletContent = stripped[1..].TrimStart();
                // Apply selective ALL-CAPS bolding (e.g. "**ASSAULT INTERCESSOR SERGEANT** with...")
                // FlushItem's OperativeBulletRewrapRegex will then re-wrap to bold the full text
                // including lowercase qualifier ("with one of the following options:").
                var rest = ApplyBold(bulletContent);
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
                var boldedStripped = ApplyBold(stripped);

                // Uppercase-first heuristic: if we already have REAL item content AND this plain
                // line starts with an uppercase letter, it is a new paragraph — not a word-wrap
                // continuation. Genuine word-wrap always starts lowercase
                // ("and one of the following:", "grenade launcher", etc.).
                // Guard: a bare bullet prefix ("  - " with no text) has no real content,
                // so we must NOT treat the following uppercase operative name as a new paragraph.
                var hasRealContent = currentItem.ToString().Trim('-', ' ').Length > 0;
                var isNewParagraph = hasRealContent && char.IsUpper(stripped[0]);

                if (isNewParagraph)
                {
                    FlushItem();
                    output.AppendLine(); // blank line before new paragraph
                    parentDepth = 0;

                    // Lines starting with "Some " are rules callout boxes in the PDF — format
                    // as a Markdown blockquote.
                    if (stripped.StartsWith("Some ", StringComparison.OrdinalIgnoreCase))
                    {
                        currentItem.Append("> ").Append(boldedStripped);
                    }
                    else
                    {
                        currentItem.Append(boldedStripped);
                    }
                }
                else if (currentItem.Length > 0)
                {
                    // Avoid double-space when appending to a bare bullet prefix (ends with ' ')
                    if (currentItem[currentItem.Length - 1] == ' ')
                    {
                        currentItem.Append(boldedStripped);
                    }
                    else
                    {
                        currentItem.Append(' ').Append(boldedStripped);
                    }
                }
                else
                {
                    currentItem.Append(boldedStripped);
                }
            }
        }

        FlushItem();

        // Apply sentence-break patterns (same as StructureToMarkdown Step 8)
        var result = output.ToString();

        foreach (var pattern in TextHelpers.ConstraintSentencePatterns)
        {
            result = result.Replace(pattern, "\n\n" + pattern, StringComparison.OrdinalIgnoreCase);
        }

        result = result.Replace(". Your kill team", ".\n\nYour kill team");
        result = result.Replace(". Use this ", ".\n\nUse this ");

        // Collapse 3+ consecutive newlines to a single paragraph break
        result = Regex.Replace(result, @"\n{3,}", "\n\n");

        // Trim trailing blank lines
        return result.TrimEnd();
    }

    /// <summary>
    /// Converts ALL-CAPS sequences in <paramref name="text"/> to bold Markdown,
    /// preserving the original capitalisation.
    /// E.g. "DEATH JESTER" → "**DEATH JESTER**", "VOID-DANCER TROUPE" → "**VOID-DANCER TROUPE**".
    /// Single ALL-CAPS words of ≥2 letters are also converted.
    /// </summary>
    private static string ApplyBold(string text)
    {
        return AllCapsSequenceRegex().Replace(text, m => $"**{m.Value}**");
    }

    // ─── Supplementary information parsing ───────────────────────────────────────

    /// <summary>
    /// Parses a Supplementary Information PDF and returns formatted Markdown text.
    /// Uses context-aware processing to detect section headers (all-caps lines),
    /// ability name sub-headers (short mixed-case lines before action lines ending in ':'),
    /// and prose text joined with proper word-wrap spacing.
    /// </summary>
    public string ParseSupplementaryInfo(string path)
    {
        var rawLines = GetPdfLines(path, raw: true);

        // Pre-process: strip control chars, normalise text, discard "CONTINUES ON OTHER SIDE"
        var lines = rawLines
            .Select(l => TextHelpers.NormaliseText(new string(l.Where(c => c >= 32).ToArray())))
            .Where(l => l.Length > 0 && !l.Equals("CONTINUES ON OTHER SIDE", StringComparison.OrdinalIgnoreCase))
            .ToList();

        // Remove the Kill Team Selection operative list — already captured in operativeSelection.
        // The list starts at "Below you will find..." and runs until the first flavour-text quote
        // (starts with ') or the ARCHETYPES heading, whichever comes first.
        // Content from that re-entry point onward (ARCHETYPES explanation, KILL TEAM heading,
        // extra rules) is kept; stat-card pages are excluded via ktsStatCardsMode below.
        var ktsIntroIdx = lines.FindIndex(l =>
            l.StartsWith("Below you will find", StringComparison.OrdinalIgnoreCase));
        if (ktsIntroIdx >= 0)
        {
            var reentryIdx = lines.FindIndex(ktsIntroIdx, l =>
                l.StartsWith("'", StringComparison.Ordinal) ||
                l.Equals("ARCHETYPES", StringComparison.OrdinalIgnoreCase));
            lines = reentryIdx > ktsIntroIdx
                ? [.. lines.Take(ktsIntroIdx), .. lines.Skip(reentryIdx)]
                : lines.Take(ktsIntroIdx).ToList();
        }

        var struckPhrases = GetStruckPhrases(path);

        var output = new StringBuilder();
        var prevWasHeader = false;
        var prevAllCapsQuoteMode = false; // true when inside a flavour-text / quote ALL-CAPS block
        var quoteBuffer = new StringBuilder(); // accumulates ALL-CAPS quote-block lines
        string? pendingAttributionText = null; // non-null after a quote block is flushed; accumulates attribution lines
        var pendingHeaderText = new StringBuilder(); // accumulates consecutive all-caps lines into one header

        void FlushQuoteBuffer()
        {
            if (quoteBuffer.Length == 0)
            {
                return;
            }

            if (output.Length > 0)
            {
                while (output.Length > 0 && output[output.Length - 1] == '\n')
                {
                    output.Remove(output.Length - 1, 1);
                }

                output.Append("\n\n");
            }

            output.Append("> **").Append(quoteBuffer.ToString().Trim()).Append("**\n>\n");
            quoteBuffer.Clear();
            pendingAttributionText = string.Empty; // waiting for the "- Attribution" line
        }

        void FlushPendingAttribution()
        {
            if (pendingAttributionText == null)
            {
                return;
            }

            if (pendingAttributionText.Length > 0)
            {
                output.Append("> \u2014 ").Append(pendingAttributionText).Append("\n\n");
            }
            else
            {
                // Quote block with no attribution found — just close it
                output.Append("\n\n");
            }

            pendingAttributionText = null;
        }

        void FlushPendingHeader()
        {
            FlushPendingAttribution();

            if (pendingHeaderText.Length == 0)
            {
                return;
            }

            // Blank line before header (not at start of document)
            if (output.Length > 0)
            {
                while (output.Length > 0 && output[output.Length - 1] == '\n')
                {
                    output.Remove(output.Length - 1, 1);
                }

                output.Append("\n\n");
            }

            var headerText = pendingHeaderText.ToString();
            var headerLevel = headerText.Contains(',') ? "##" : "#";
            output.Append(headerLevel).Append(' ').Append(headerText).Append("\n\n");
            pendingHeaderText.Clear();
        }

        var lastLineWasBulletSymbol = false; // set when prev line was a bare • or ○ symbol

        // Kill Team Selection section state
        var ktsTeamName = string.Empty; // e.g. "ANGELS OF DEATH"
        var ktsNameWords = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var ktsArchetypesProcessed = false; // true once the ARCHETYPES heading line has been seen
        var ktsStatCardsMode = false; // true once stat-card pages begin (skip everything)

        for (var i = 0; i < lines.Count; i++)
        {
            var trimmed = lines[i];

            // Once stat-card pages start, skip everything.
            if (ktsStatCardsMode)
            {
                continue;
            }

            if (trimmed.Length == 0)
            {
                lastLineWasBulletSymbol = false;
                FlushQuoteBuffer();
                prevAllCapsQuoteMode = false;
                FlushPendingAttribution();

                continue;
            }

            // Fix 1: bare bullet/circle lines on their own (content on next line)
            // Track them so the next line is NOT treated as a header or ability sub-header.
            var isBareSymbol = trimmed is "\u2022" or "\u25CB"; // • or ○
            if (isBareSymbol)
            {
                lastLineWasBulletSymbol = true;
                AppendText(output, trimmed);
                prevWasHeader = false;
                FlushQuoteBuffer();
                prevAllCapsQuoteMode = false;
                continue;
            }

            // KTS: the » (U+00BB) character is a decorative separator in the Kill Team Selection
            // page header. Each page repeats the title fragments — skip them.
            // Once the ARCHETYPES section has been processed and the KILL TEAM heading has been
            // emitted, a subsequent » marks the start of the stat-card pages; skip everything after.
            if (trimmed == "\u00BB")
            {
                if (ktsTeamName.Length > 0 && ktsArchetypesProcessed)
                {
                    ktsStatCardsMode = true;
                }

                continue;
            }

            // KTS: once the Kill Team Selection heading is emitted, team name words
            // (e.g. "ANGELS", "OF", "DEATH") are ALWAYS page header fragments and must
            // be skipped everywhere in the section — not just immediately after the title.
            if (ktsTeamName.Length > 0 && ktsNameWords.Contains(trimmed))
            {
                continue;
            }

            var wasBulletSymbol = lastLineWasBulletSymbol;
            lastLineWasBulletSymbol = false;

            // Fix 2: arrow group-header lines (↘ ↙ ↳) must start on their own line so
            // FormatBulletSymbols can convert them. Ensure a paragraph break before them.
            if (trimmed[0] is '\u2198' or '\u2199' or '\u21B3')
            {
                if (prevWasHeader)
                {
                    FlushPendingHeader();
                    prevWasHeader = false;
                }

                while (output.Length > 0 && output[output.Length - 1] == '\n')
                {
                    output.Remove(output.Length - 1, 1);
                }

                if (output.Length > 0)
                {
                    output.Append("\n\n");
                }
            }

            // If the previous line was a bare bullet symbol, skip header and sub-header
            // detection — the content belongs inside that bullet item.
            if (!wasBulletSymbol)
            {
                var isAllCaps = !trimmed.Any(char.IsLower);

                if (isAllCaps && trimmed.Length >= 3)
                {
                    // ALL-CAPS lines containing '?' or '!' are sentence terminators — they appear
                    // in flavour-text quote blocks, not as section headings. Once we detect one,
                    // stay in quote mode for all subsequent all-caps lines until a non-all-caps
                    // line resets the mode.
                    var hasStrongTerminator = trimmed.Contains('?') || trimmed.Contains('!')
                        || trimmed.StartsWith('\''); // opening single-quote = callout/flavour-text box

                    if (!hasStrongTerminator && !prevAllCapsQuoteMode)
                    {
                    // KTS: detect "[TEAM NAME] KILL TEAM" — the Kill Team Selection page heading.
                    // Emit the section title once and skip repeated header fragments.
                    if (trimmed.EndsWith(" KILL TEAM", StringComparison.OrdinalIgnoreCase)
                        && ktsTeamName.Length == 0)
                    {
                        ktsTeamName = trimmed[..^" KILL TEAM".Length];
                        foreach (var word in ktsTeamName.Split(' ', StringSplitOptions.RemoveEmptyEntries))
                        {
                            ktsNameWords.Add(word);
                        }

                        FlushPendingHeader();
                        while (output.Length > 0 && output[output.Length - 1] == '\n')
                        {
                            output.Remove(output.Length - 1, 1);
                        }

                        output.Append($"\n\n# {trimmed}\n\n");
                        prevWasHeader = false;
                        continue;
                    }

                    // KTS: flag the ARCHETYPES heading so » can detect when stat-card pages begin.
                    if (trimmed.Equals("ARCHETYPES", StringComparison.OrdinalIgnoreCase))
                    {
                        ktsArchetypesProcessed = true;
                    }

                    // KTS: skip repeated heading fragments (team name words + KILL/TEAM/SELECTION)
                    if (ktsTeamName.Length > 0 && (ktsNameWords.Contains(trimmed)
                        || trimmed is "KILL" or "TEAM" or "SELECTION"))
                    {
                        continue;
                    }

                    // Merge into pending header when:
                    // - pending ends with '&' or ',' (mid-phrase word-wrap), OR
                    // - pending contains ',' but doesn't end with one (word-wrapped mid-phrase,
                    //   e.g. "MALIGNANT PLAGUECASTER OPERATIVE, PUTRESCENT" + "VITALITY ACTION")
                    //   AND incoming doesn't contain ',' (new items have commas, continuations don't)
                    var pendingTrimmed = pendingHeaderText.ToString().TrimEnd();
                    if (prevWasHeader && (pendingTrimmed.EndsWith('&') || pendingTrimmed.EndsWith(',')
                        || (pendingTrimmed.Contains(',') && !trimmed.Contains(','))))
                    {
                        pendingHeaderText.Append(' ').Append(trimmed);
                    }
                    else
                    {
                        FlushPendingHeader();
                        pendingHeaderText.Append(trimmed);
                    }

                    prevWasHeader = true;
                        continue;
                    }

                    // Quote/flavour-text mode: flush any pending heading and treat as body text.
                    if (prevWasHeader)
                    {
                        FlushPendingHeader();
                        prevWasHeader = false;
                    }

                    prevAllCapsQuoteMode = true;
                    // Fall through to body text processing (no 'continue')
                }
                else
                {
                    // Non-all-caps content — exit quote mode
                    FlushQuoteBuffer();
                    prevAllCapsQuoteMode = false;
                }

                // Flush any pending all-caps header before processing non-header content
                if (prevWasHeader)
                {
                    FlushPendingHeader();
                    prevWasHeader = false;
                }

                // Mixed-case ability name heuristic: a short capitalised line (< 50 chars)
                // immediately followed by an action line ending in ':' (e.g. "Changed to read:").
                // These are bold sub-headers within errata sections.
                var isAbilitySubHeader = trimmed.Length < 50
                    && char.IsUpper(trimmed[0])
                    && trimmed.Any(char.IsLower)
                    && !trimmed.Contains(':')
                    && !trimmed.StartsWith('\'')
                    && i + 1 < lines.Count
                    && lines[i + 1].TrimEnd().EndsWith(':');

                if (isAbilitySubHeader)
                {
                    // Blank line before sub-header
                    if (output.Length > 0)
                    {
                        while (output.Length > 0 && output[output.Length - 1] == '\n')
                        {
                            output.Remove(output.Length - 1, 1);
                        }

                        output.Append("\n\n");
                    }

                    output.Append("## ").Append(trimmed).Append("\n\n");
                    continue;
                }
            }
            else if (prevWasHeader)
            {
                // Bare-bullet content terminates any pending header
                FlushPendingHeader();
                prevWasHeader = false;
                FlushQuoteBuffer();
                prevAllCapsQuoteMode = false;
            }

            // Fix 3: paragraph break before specific sentence starters that follow inline
            // content with no raw blank line in the PDF.
            if (!prevAllCapsQuoteMode
                && output.Length > 0 && output[output.Length - 1] != '\n'
                && (trimmed.StartsWith("Other than ", StringComparison.OrdinalIgnoreCase)
                    || trimmed.StartsWith("Some ", StringComparison.OrdinalIgnoreCase)))
            {
                output.Append("\n\n");
            }

            // Accumulate ALL-CAPS quote blocks into quoteBuffer; regular prose via AppendText.
            if (prevAllCapsQuoteMode)
            {
                // Flush any attribution pending from a previous quote before this new one.
                FlushPendingAttribution();

                if (quoteBuffer.Length > 0)
                {
                    quoteBuffer.Append(' ');
                }

                quoteBuffer.Append(trimmed);
            }
            else if (pendingAttributionText != null)
            {
                if (pendingAttributionText.Length == 0 && trimmed.StartsWith("- ", StringComparison.Ordinal))
                {
                    // First attribution line — buffer it (don't emit yet; next line may be continuation)
                    var attribution = trimmed[2..].TrimStart();
                    attribution = Regex.Replace(attribution, @"\s+\d+$", string.Empty);
                    pendingAttributionText = attribution;
                }
                else if (pendingAttributionText.Length > 0 && trimmed.Any(char.IsLower) && !trimmed.StartsWith("- ", StringComparison.Ordinal))
                {
                    // Continuation of a wrapped attribution line (e.g. "the Onyx Claw Shrine, Biel-Tan Craftworld")
                    var continuation = Regex.Replace(trimmed.TrimEnd(), @"\s+\d+$", string.Empty);
                    pendingAttributionText += " " + continuation;
                }
                else
                {
                    // Not an attribution or continuation — flush and process line normally
                    FlushPendingAttribution();
                    AppendText(output, trimmed);
                }
            }
            else
            {
                AppendText(output, trimmed);
            }
        }

        FlushQuoteBuffer();
        FlushPendingAttribution();
        FlushPendingHeader();

        var text = TextHelpers.StructureToMarkdown(output.ToString().TrimStart());

        // Heading lines (# / ##) are already emphasised by the heading marker — strip
        // any bold markers that StructureToMarkdown added to ALL-CAPS heading text.
        text = Regex.Replace(text, @"^(#{1,2}\s+)(.+)$", m =>
        {
            var prefix = m.Groups[1].Value;
            var content = m.Groups[2].Value.Replace("**", string.Empty);
            return prefix + content;
        }, RegexOptions.Multiline);

        // Strikethrough for explicitly labelled deleted rule text: deleted: '...'
        // Pattern: allow apostrophes inside contractions (e.g. "it's", "doesn't") by requiring
        // them to be followed by a word character; stop at paragraph boundaries (newlines) to
        // prevent the match from spanning across sections when a closing quote is absent.
        text = Regex.Replace(
            text,
            @"deleted: '((?:[^'\n]|'(?=[a-z]))+)'",
            "deleted: ~~'$1'~~");

        // Strikethrough for visually struck text detected by PdfPig path analysis.
        // This runs AFTER the deleted: regex so we can skip regions already marked as struck.
        if (struckPhrases.Count > 0)
        {
            text = ApplyVisualStrikethrough(text, struckPhrases);
        }

        return text;
    }

    /// <summary>
    /// Detects visually struck-through text in a PDF by finding thin horizontal vector
    /// paths (drawn over text as strikethrough marks) and identifying which letters they
    /// overlap. Returns a deduplicated list of struck phrases after text normalisation.
    /// </summary>
    private static IReadOnlyList<string> GetStruckPhrases(string pdfPath)
    {
        try
        {
            using var doc = PdfDocument.Open(pdfPath);
            var phrases = new List<string>();

            for (var pageNum = 1; pageNum <= doc.NumberOfPages; pageNum++)
            {
                var page = doc.GetPage(pageNum);
                var letters = page.Letters
                    .Where(l => !string.IsNullOrWhiteSpace(l.Value))
                    .ToList();

                if (letters.Count == 0)
                {
                    continue;
                }

                // Collect thin horizontal lines (potential strikethrough marks)
                var strikeRects = new List<PdfRectangle>();
                foreach (var path in page.ExperimentalAccess.Paths)
                {
                    try
                    {
                        var bb = path.GetBoundingRectangle();
                        if (bb == null)
                        {
                            continue;
                        }

                        var r = bb.Value;
                        if (path.IsStroked
                            && path.LineWidth <= 1.0
                            && r.Height <= Math.Max(path.LineWidth, 0.5) + 0.5
                            && r.Width > 15)
                        {
                            strikeRects.Add(r);
                        }
                    }
                    catch
                    {
                        // Ignore malformed paths
                    }
                }

                if (strikeRects.Count == 0)
                {
                    continue;
                }

                // Group letters into visual lines and extract struck phrases
                var lineGroups = GroupLettersIntoLines(letters);

                foreach (var group in lineGroups)
                {
                    var sorted = group.Letters.OrderBy(l => l.GlyphRectangle.Left).ToList();
                    var currentPhrase = new StringBuilder();
                    Letter? prevStruck = null;

                    foreach (var letter in sorted)
                    {
                        if (IsLetterStruck(letter, strikeRects))
                        {
                            if (prevStruck != null)
                            {
                                var gap = letter.GlyphRectangle.Left - prevStruck.GlyphRectangle.Right;
                                var avgW = (letter.GlyphRectangle.Width + prevStruck.GlyphRectangle.Width) / 2;
                                if (gap > Math.Max(avgW * 0.25, 0.5))
                                {
                                    currentPhrase.Append(' ');
                                }
                            }

                            currentPhrase.Append(letter.Value);
                            prevStruck = letter;
                        }
                        else
                        {
                            if (currentPhrase.Length > 0)
                            {
                                var phrase = TextHelpers.NormaliseText(currentPhrase.ToString());
                                if (phrase.Length > 0)
                                {
                                    phrases.Add(phrase);
                                }

                                currentPhrase.Clear();
                            }

                            prevStruck = null;
                        }
                    }

                    if (currentPhrase.Length > 0)
                    {
                        var phrase = TextHelpers.NormaliseText(currentPhrase.ToString());
                        if (phrase.Length > 0)
                        {
                            phrases.Add(phrase);
                        }
                    }
                }
            }

            return phrases
                .Where(p => p.Length > 0)
                .Distinct(StringComparer.Ordinal)
                .ToList();
        }
        catch
        {
            return [];
        }
    }

    /// <summary>
    /// Applies Markdown strikethrough for visually struck phrases detected by PdfPig
    /// vector path analysis. Bullet prefixes (• ○) are stripped from phrases before
    /// matching because <see cref="AppendText"/> has already converted them to Markdown
    /// list syntax. All occurrences of each phrase are struck. Positions already inside
    /// <c>~~...~~</c> markers (from the <c>deleted:</c> regex) are skipped.
    /// </summary>
    private static string ApplyVisualStrikethrough(string text, IReadOnlyList<string> struckPhrases)
    {
        foreach (var rawPhrase in struckPhrases)
        {
            if (rawPhrase.Length == 0)
            {
                continue;
            }

            // Strip leading bullet characters — AppendText converts • / ○ to Markdown list syntax
            var phrase = rawPhrase.TrimStart('\u2022', '\u25CB').TrimStart();
            if (phrase.Length == 0)
            {
                continue;
            }

            // Apply to ALL occurrences in the text (a phrase can appear more than once)
            var searchPos = 0;
            while (searchPos < text.Length)
            {
                var idx = text.IndexOf(phrase, searchPos, StringComparison.Ordinal);
                if (idx < 0)
                {
                    break;
                }

                // Don't double-wrap regions already struck by the deleted: regex
                if (IsInsideStrikethrough(text, idx))
                {
                    searchPos = idx + phrase.Length;
                    continue;
                }

                text = text[..idx] + "~~" + phrase + "~~" + text[(idx + phrase.Length)..];
                searchPos = idx + phrase.Length + 4; // skip past the inserted ~~...~~
            }
        }

        return text;
    }

    /// <summary>
    /// Returns <c>true</c> when <paramref name="idx"/> falls inside an existing
    /// <c>~~...~~</c> struck region, detected by counting <c>~~</c> occurrences before
    /// <paramref name="idx"/> (odd count = inside a struck region).
    /// </summary>
    private static bool IsInsideStrikethrough(string text, int idx)
    {
        var count = 0;
        var pos = 0;
        while (pos < idx)
        {
            var found = text.IndexOf("~~", pos, StringComparison.Ordinal);
            if (found < 0 || found >= idx)
            {
                break;
            }

            count++;
            pos = found + 2;
        }

        return count % 2 == 1;
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
    /// PDF text extraction can emit characters such as BEL (0x07) and BS (0x08) as rendering artefacts;
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
                // Don't add a leading space when sb already ends with a newline (paragraph break)
                // or a space (e.g. bare bullet prefix "  - ") — avoids double-spacing.
                if (sb.Length > 0 && sb[sb.Length - 1] != '\n' && sb[sb.Length - 1] != ' ')
                {
                    sb.Append(' ');
                }

                sb.Append(part);
                break;
        }
    }

    private static bool IsEquipmentSkip(string text)
    {
        return EquipmentSkipPatterns.Any(p => Regex.IsMatch(text, p, RegexOptions.IgnoreCase));
    }

    /// <summary>
    /// Extracts text lines from a PDF using PdfPig word extraction.
    /// <list type="bullet">
    ///   <item><c>raw: true</c> — reading-order text with single-space word joins (prose content)</item>
    ///   <item><c>raw: false</c> — layout-preserving text with gap-proportional spacing (weapon tables)</item>
    /// </list>
    /// Unicode quotes and common mojibake sequences are normalised for downstream regex compatibility.
    /// </summary>
    private static List<string> GetPdfLines(string pdfPath, bool raw = false)
    {
        using var doc = PdfDocument.Open(pdfPath);
        var allLines = new List<string>();

        for (var pageNum = 1; pageNum <= doc.NumberOfPages; pageNum++)
        {
            if (pageNum > 1)
            {
                allLines.Add(string.Empty);
            }

            var page = doc.GetPage(pageNum);

            if (raw)
            {
                // Raw mode: use content-stream order to preserve column sequencing.
                // This prevents two-column ability text from being interleaved.
                var rawText = ContentOrderTextExtractor.GetText(page);

                foreach (var line in rawText.Split('\n'))
                {
                    var trimmed = line.TrimEnd();

                    if (trimmed.Length == 0)
                    {
                        continue;
                    }

                    allLines.Add(NormalizePdfText(trimmed));
                }
            }
            else
            {
                // Layout mode: spatial word grouping with gap-proportional spacing.
                var words = page.GetWords().ToList();

                if (words.Count == 0)
                {
                    continue;
                }

                var allLetters = page.Letters
                    .Where(l => !string.IsNullOrWhiteSpace(l.Value))
                    .ToList();
                var avgCharWidth = allLetters.Count > 0
                    ? allLetters.Average(l => l.GlyphRectangle.Width)
                    : 3.0;

                var lineGroups = GroupWordsIntoLines(words);
                lineGroups.Sort((a, b) => b.Y.CompareTo(a.Y));

                var medianHeight = GetMedianWordHeight(words);

                for (var g = 0; g < lineGroups.Count; g++)
                {
                    if (g > 0)
                    {
                        var gap = lineGroups[g - 1].Y - lineGroups[g].Y;
                        if (gap > medianHeight * 1.8)
                        {
                            allLines.Add(string.Empty);
                        }
                    }

                    var sortedWords = lineGroups[g].Words
                        .OrderBy(w => w.BoundingBox.Left)
                        .ToList();

                    var lineText = BuildLayoutLineText(sortedWords, avgCharWidth);
                    lineText = NormalizePdfText(lineText);
                    allLines.Add(lineText);
                }
            }
        }

        return allLines;
    }

    /// <summary>Groups words into visual lines based on Y-position proximity.</summary>
    private static List<(double Y, List<Word> Words)> GroupWordsIntoLines(List<Word> words)
    {
        if (words.Count == 0)
        {
            return [];
        }

        var sorted = words.OrderByDescending(w => w.BoundingBox.Bottom).ToList();
        var groups = new List<(double Y, List<Word> Words)>();
        var currentWords = new List<Word> { sorted[0] };
        var currentY = sorted[0].BoundingBox.Bottom;

        for (var i = 1; i < sorted.Count; i++)
        {
            var wordY = sorted[i].BoundingBox.Bottom;
            var height = sorted[i].BoundingBox.Height;
            var tolerance = Math.Max(height * 0.4, 1.5);

            if (currentY - wordY <= tolerance)
            {
                currentWords.Add(sorted[i]);
            }
            else
            {
                groups.Add((currentY, currentWords));
                currentWords = new List<Word> { sorted[i] };
                currentY = wordY;
            }
        }

        groups.Add((currentY, currentWords));
        return groups;
    }

    /// <summary>
    /// Builds a layout-mode string with gap-proportional spacing between words.
    /// Column gaps produce multiple spaces, enabling regex patterns like <c>\s{2,}</c>
    /// to detect table column boundaries.
    /// Also merges split words where PdfPig produces a single uppercase letter
    /// followed by a closely-spaced lowercase continuation (e.g. "R" + "ange" → "Range").
    /// </summary>
    private static string BuildLayoutLineText(List<Word> sortedWords, double avgCharWidth)
    {
        if (sortedWords.Count == 0)
        {
            return string.Empty;
        }

        var sb = new StringBuilder();
        var i = 0;

        while (i < sortedWords.Count)
        {
            if (i > 0)
            {
                var gap = sortedWords[i].BoundingBox.Left - sortedWords[i - 1].BoundingBox.Right;
                var spaceCount = Math.Max(1, (int)Math.Round(gap / avgCharWidth));
                sb.Append(' ', spaceCount);
            }

            // Merge split word: single letter closely followed by the rest of the word.
            // Kill Team PDFs render the first letter of some words as separate text objects.
            // Handles both "R" + "ange" (lowercase) and "P" + "SYCHIC" (uppercase) continuations.
            if (sortedWords[i].Text.Length == 1
                && char.IsUpper(sortedWords[i].Text[0])
                && i + 1 < sortedWords.Count
                && sortedWords[i + 1].Text.Length > 1)
            {
                var wordGap = sortedWords[i + 1].BoundingBox.Left - sortedWords[i].BoundingBox.Right;
                if (wordGap < avgCharWidth * 2)
                {
                    sb.Append(sortedWords[i].Text);
                    sb.Append(sortedWords[i + 1].Text);
                    i += 2;
                    continue;
                }
            }

            sb.Append(sortedWords[i].Text);
            i++;
        }

        return sb.ToString();
    }

    /// <summary>Returns the median word height for blank-line gap detection.</summary>
    private static double GetMedianWordHeight(IReadOnlyList<Word> words)
    {
        if (words.Count == 0)
        {
            return 10.0;
        }

        var heights = words.Select(w => w.BoundingBox.Height).OrderBy(h => h).ToList();
        return heights[heights.Count / 2];
    }

    /// <summary>
    /// Normalises common mojibake sequences and Unicode characters in PDF-extracted text
    /// for downstream regex compatibility.
    /// </summary>
    private static string NormalizePdfText(string text)
    {
        return text
            .TrimStart('\uFEFF')                       // strip BOM
            .Replace("\u0393\u00C7\u00F3", "\u2022")   // mojibake → • (bullet)
            .Replace("\u0393\u00C7\u00D6", "'")         // mojibake → ' (right single quote)
            .Replace('\u2019', '\'')
            .Replace('\u2018', '\'')
            .Replace('\u201C', '"')
            .Replace('\u201D', '"')
            .Replace("\u00AE", string.Empty)
            .Replace("\u2122", string.Empty);
    }

    /// <summary>Checks whether a letter is vertically overlapped by any strikethrough line.</summary>
    private static bool IsLetterStruck(Letter letter, List<PdfRectangle> strikeRects)
    {
        var letterBottom = letter.GlyphRectangle.Bottom;
        var letterTop = letter.GlyphRectangle.Top;
        return strikeRects.Any(r =>
            r.Bottom <= letterTop
            && r.Top >= letterBottom
            && letter.GlyphRectangle.Left >= r.Left - 2
            && letter.GlyphRectangle.Right <= r.Right + 2);
    }

    /// <summary>Groups letters into visual lines based on Y-position proximity (for strikethrough detection).</summary>
    private static List<(double Y, List<Letter> Letters)> GroupLettersIntoLines(IReadOnlyList<Letter> letters)
    {
        if (letters.Count == 0)
        {
            return [];
        }

        var sorted = letters.OrderByDescending(l => l.GlyphRectangle.Bottom).ToList();
        var groups = new List<(double Y, List<Letter> Letters)>();
        var currentLetters = new List<Letter> { sorted[0] };
        var currentY = sorted[0].GlyphRectangle.Bottom;

        for (var i = 1; i < sorted.Count; i++)
        {
            var letterY = sorted[i].GlyphRectangle.Bottom;
            var height = sorted[i].GlyphRectangle.Height;
            var tolerance = Math.Max(height * 0.4, 1.5);

            if (currentY - letterY <= tolerance)
            {
                currentLetters.Add(sorted[i]);
            }
            else
            {
                groups.Add((currentY, currentLetters));
                currentLetters = new List<Letter> { sorted[i] };
                currentY = letterY;
            }
        }

        groups.Add((currentY, currentLetters));
        return groups;
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
            .Replace("'", string.Empty)
            .Replace("(", string.Empty)
            .Replace(")", string.Empty);
    }

    private static string ToTitleCase(string text) => TextHelpers.ToTitleCase(text);

    /// <summary>
    /// Pure document-level type labels that appear on every card as a structural header.
    /// These carry no sub-category meaning and are excluded from the category label.
    /// Note: "Aspect Technique" is intentionally NOT here — it is a meaningful type suffix
    /// that combines with the preceding category (e.g. "Howling Banshee Aspect Technique").
    /// </summary>
    private static readonly HashSet<string> PureTypeLabels = new(StringComparer.OrdinalIgnoreCase)
    {
        "Faction Rule", "Strategy Ploy", "Firefight Ploy",
    };

    /// <summary>
    /// Derives the category label from a chain of consecutive empty-text ALL-CAPS headers
    /// that appeared before a rule with text. The chain may interleave page headers (team name),
    /// doc-level type labels, and genuine category / type-specific labels in any order.
    ///
    /// Filters out <see cref="PureTypeLabels"/>, the team name, and the primary keyword, then
    /// takes the last two remaining elements and joins them (e.g. "Howling Banshee" +
    /// "Aspect Technique" → "Howling Banshee Aspect Technique"). The two-element ceiling
    /// prevents spurious preamble text earlier in the chain from leaking into the output.
    /// </summary>
    private static string? DetermineRuleCategory(List<string> chain, string? teamNameTitleCase = null, string? primaryKeywordTitleCase = null)
    {
        var meaningful = chain
            .Where(e =>
                !PureTypeLabels.Contains(e)
                && (teamNameTitleCase == null || !string.Equals(e, teamNameTitleCase, StringComparison.OrdinalIgnoreCase))
                && (primaryKeywordTitleCase == null || !string.Equals(e, primaryKeywordTitleCase, StringComparison.OrdinalIgnoreCase)))
            .ToList();

        if (meaningful.Count == 0)
        {
            return null;
        }

        // Take at most the last 2 elements to exclude any spurious preamble lines
        // that may have been captured early in the chain.
        var elements = meaningful.Count > 2 ? meaningful.Skip(meaningful.Count - 2) : meaningful;
        return string.Join(" ", elements.Select(ToTitleCase));
    }

    // ─── Generated regexes ────────────────────────────────────────────────────────

    [GeneratedRegex(@"\bAPL\b.*\bMOVE\b.*\bSAVE\b.*\bWOUNDS\b")]
    private static partial Regex StatsHeaderRegex();

    [GeneratedRegex(@"^[A-Z][A-Z\s\-]+$")]
    private static partial Regex AllCapsNameRegex();

    /// <summary>Variant of AllCapsNameRegex that also permits apostrophes, digits, and commas for rule names (e.g. "UNSTINTING, IMMOVABLE").</summary>
    [GeneratedRegex(@"^[A-Z][A-Z0-9\s\-'',]+$")]
    private static partial Regex AllCapsRuleNameRegex();

    [GeneratedRegex(@"\bNAME\b.*\bATK\b.*\bHIT\b.*\bDMG\b")]
    private static partial Regex WeaponTableHeaderRegex();

    [GeneratedRegex(@"^[A-Z0-9\s,\-']+$")]
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

    [GeneratedRegex(@"ARCHETYPES?:\s*(.+)", RegexOptions.IgnoreCase)]
    private static partial Regex ArchetypeRegex();

    /// <summary>Matches contiguous ALL-CAPS sequences (possibly hyphenated, multi-word) for bold conversion.</summary>
    [GeneratedRegex(@"[A-Z][A-Z\-]+(?:\s+[A-Z][A-Z\-]+)*")]
    private static partial Regex AllCapsSequenceRegex();

    /// <summary>
    /// Matches a • bullet item whose content begins with a bold ALL-CAPS operative-name span
    /// followed by qualifying lowercase text (e.g. "with one of the following options:").
    /// Used by FlushItem to re-wrap the entire content in a single bold span.
    /// Group 1 = ALL-CAPS name (without the ** markers).
    /// Group 2 = the remaining qualifier text (space-prefixed).
    /// </summary>
    [GeneratedRegex(@"^  - \*\*([A-Z][A-Z '\-]+(?:\s+[A-Z][A-Z '\-]+)*)\*\* (.+)$")]
    private static partial Regex OperativeBulletRewrapRegex();

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

    // ─── Equipment font-size break detection ─────────────────────────────────────

    /// <summary>
    /// Scans an equipment PDF using PdfPig's word-level API and returns the normalised
    /// text of the last lore-font line on each page where a 9pt → 8.5pt font transition
    /// occurs. These lines mark the boundary between flavour/lore text (9pt) and rules
    /// text (8.5pt) on the physical card.
    /// </summary>
    private static HashSet<string> GetEquipmentFontBreakLines(string pdfPath)
    {
        var breakLines = new HashSet<string>(StringComparer.Ordinal);

        using var doc = PdfDocument.Open(pdfPath);

        foreach (var page in doc.GetPages())
        {
            var words = page.GetWords().ToList();

            if (words.Count == 0)
            {
                continue;
            }

            var lineGroups = GroupWordsIntoLines(words);
            lineGroups.Sort((a, b) => b.Y.CompareTo(a.Y)); // top-to-bottom

            // Walk lines top-to-bottom looking for a font-size transition.
            // Lore text is ~9pt, rules text is ~8.5pt. Headers are 12pt+.
            // We detect the last ≥8.8pt body-text line before the first ≤8.6pt line.
            string? lastLoreLine = null;

            foreach (var group in lineGroups)
            {
                var avgFontSize = group.Words.Average(w => w.Letters[0].PointSize);

                // Skip header lines (12pt+)
                if (avgFontSize >= 10)
                {
                    lastLoreLine = null;
                    continue;
                }

                if (avgFontSize >= 8.8)
                {
                    // Lore-font line — build text and remember it
                    var text = string.Join(" ", group.Words.OrderBy(w => w.BoundingBox.Left).Select(w => w.Text));
                    lastLoreLine = NormalizePdfText(text.TrimEnd());
                }
                else if (avgFontSize <= 8.6 && lastLoreLine != null)
                {
                    // First rules-font line after lore — record the break
                    breakLines.Add(lastLoreLine);
                    lastLoreLine = null;
                }
            }
        }

        return breakLines;
    }
}
